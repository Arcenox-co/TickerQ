import { createServer } from 'node:http';
import { pathToFileURL } from 'node:url';
import { join } from 'node:path';

const sdkRoot = process.argv[2];
const load = relative => import(pathToFileURL(join(sdkRoot, 'dist', relative)).href);
const { TickerSdkOptions } = await load('TickerSdkOptions.js');
const { TickerFunctionProvider } = await load('infrastructure/TickerFunctionProvider.js');
const { SdkExecutionEndpoint } = await load('middleware/SdkExecutionEndpoint.js');
const { TickerQTaskScheduler } = await load('worker/TickerQTaskScheduler.js');
const { TickerFunctionConcurrencyGate } = await load('worker/TickerFunctionConcurrencyGate.js');
const { TickerTaskPriority } = await load('enums/index.js');

const options = new TickerSdkOptions();
options.webhookSignature = 'dotnet-node-e2e-secret';
options.nodeEpoch = '8fef33e7-826b-49e4-a36d-8eb0aa570ef1';
let invocationCount = 0;
TickerFunctionProvider.registerFunction('dotnet-e2e-job',
  { message: 'registration-default', count: -1 },
  async ctx => ctx.setResult({ received: ctx.request }),
  {
    priority: TickerTaskPriority.Normal,
    requestType: 'E2eRequest',
    requestContract: { schema: { type: 'object', properties: { message: { type: 'string' }, count: { type: 'integer' } }, required: ['message', 'count'] } },
    resultType: 'E2eResult',
    resultContract: { schema: { type: 'object' } },
  });
TickerFunctionProvider.registerFunction('dotnet-delayed-cancel',
  { delayMs: 500 },
  async ctx => {
    await new Promise(resolve => setTimeout(resolve, ctx.request.delayMs));
  },
  {
    priority: TickerTaskPriority.Normal,
    requestType: 'DelayRequest',
    requestContract: { schema: { type: 'object', properties: { delayMs: { type: 'integer' } }, required: ['delayMs'] } },
  });
TickerFunctionProvider.registerFunction('dotnet-cancel-first', async () => { invocationCount++; });
const endpoint = new SdkExecutionEndpoint(
  options,
  { syncAsync: async () => {} },
  new TickerQTaskScheduler(1),
  new TickerFunctionConcurrencyGate(),
  { updateTimeTicker: async () => {}, updateCronTickerOccurrence: async () => {} },
);
const endpointHandler = endpoint.createHandler();
let droppedFirstExecute = false;
let delayedCancelResponse = null;
let lateFunctionRegistered = false;
const server = createServer((req, res) => {
  if (req.method === 'GET' && req.url === '/stats') {
    res.setHeader('content-type', 'application/json'); res.end(JSON.stringify({ invocationCount })); return;
  }
  if (process.env.DROP_FIRST_EXECUTE === '1' && req.url === '/execute' && !droppedFirstExecute) {
    droppedFirstExecute = true; req.socket.destroy(); return;
  }
  if (process.env.DELAY_CANCEL_AFTER_REJECTION === '1' && req.url === '/cancel' && !delayedCancelResponse && !lateFunctionRegistered) {
    const originalEnd = res.end.bind(res);
    res.end = (chunk, ...args) => { delayedCancelResponse = () => originalEnd(chunk, ...args); };
    endpointHandler(req, res);
    return;
  }
  if (process.env.DELAY_CANCEL_AFTER_REJECTION === '1' && req.url === '/execute' && delayedCancelResponse && !lateFunctionRegistered) {
    const originalEnd = res.end.bind(res);
    res.end = (chunk, ...args) => {
      const parsed = chunk ? JSON.parse(Buffer.from(chunk).toString('utf8')) : {};
      if (parsed.state === 'rejected_not_started') {
        TickerFunctionProvider.registerFunction('dotnet-late-registration', async () => { invocationCount++; });
        lateFunctionRegistered = true;
      }
      originalEnd(chunk, ...args);
      delayedCancelResponse?.();
    };
    endpointHandler(req, res);
    return;
  }
  endpointHandler(req, res);
});
server.listen(0, '127.0.0.1', () => console.log(`PORT:${server.address().port}`));
const stop = () => server.close(() => process.exit(0));
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
