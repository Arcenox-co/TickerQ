import assert from 'node:assert/strict';
import { createServer, request as httpRequest } from 'node:http';
import { once } from 'node:events';
import crypto, { createHmac, timingSafeEqual } from 'node:crypto';
import { TickerSdkOptions } from '../dist/TickerSdkOptions.js';
import { TickerFunctionProvider } from '../dist/infrastructure/TickerFunctionProvider.js';
import { SdkExecutionEndpoint } from '../dist/middleware/SdkExecutionEndpoint.js';
import { TickerQTaskScheduler } from '../dist/worker/TickerQTaskScheduler.js';
import { TickerFunctionConcurrencyGate } from '../dist/worker/TickerFunctionConcurrencyGate.js';
import { TickerTaskPriority, TickerStatus, TickerType } from '../dist/enums/index.js';
import { generateSignature } from '../dist/utils/TickerQSignature.js';

const secret = 'proof-backed-protocol-secret';
const epoch = crypto.randomUUID();
const options = new TickerSdkOptions();
options.webhookSignature = secret;
options.nodeEpoch = epoch;
const scheduler = new TickerQTaskScheduler(1);
let invocationCount = 0;
let releaseRunning;
const runningGate = new Promise(resolve => { releaseRunning = resolve; });
const functionName = `protocol-${Date.now()}`;
TickerFunctionProvider.registerFunction(functionName, async (_ctx, signal) => {
  invocationCount++;
  await Promise.race([
    runningGate,
    new Promise((_, reject) => signal.addEventListener('abort', () => reject(Object.assign(new Error('aborted'), { name: 'AbortError' })), { once: true })),
  ]);
});
const endpoint = new SdkExecutionEndpoint(options, { syncAsync: async () => {} }, scheduler,
  new TickerFunctionConcurrencyGate(), {}, undefined);
const server = createServer(endpoint.createHandler());
server.listen(0, '127.0.0.1');
await once(server, 'listening');

function identity() {
  return { tickerType: TickerType.TimeTicker, tickerId: crypto.randomUUID(), acquisitionToken: crypto.randomUUID(), dispatchId: crypto.randomUUID(), nodeEpoch: epoch };
}
function executeBody(id, overrides = {}) {
  return JSON.stringify({ ...id, id: id.tickerId, type: id.tickerType, executionId: id.dispatchId,
    functionName, retryCount: 0, isDue: false, scheduledFor: new Date().toISOString(), hasRequest: false, requestPayload: null, ...overrides });
}
function send(path, body, nonce = crypto.randomUUID(), discardResponse = false) {
  const timestamp = Math.floor(Date.now() / 1000);
  const signature = generateSignature(secret, 'POST', path, timestamp, body, nonce);
  return new Promise((resolve, reject) => {
    const req = httpRequest({ host: '127.0.0.1', port: server.address().port, path, method: 'POST', headers: {
      'content-type': 'application/json', 'content-length': Buffer.byteLength(body), 'x-timestamp': String(timestamp),
      'x-request-nonce': nonce, 'x-tickerq-signature': signature,
    }}, res => {
      if (discardResponse) {
        res.destroy();
        reject(new Error('simulated response loss'));
        return;
      }
      const chunks = [];
      res.on('data', c => chunks.push(c));
      res.on('end', () => {
        const bytes = Buffer.concat(chunks);
        const responseTimestamp = res.headers['x-response-timestamp'];
        const responseNonce = res.headers['x-request-nonce'];
        const responseSignature = res.headers['x-tickerq-signature'];
        if (responseSignature) {
          assert.equal(responseNonce, nonce);
          const canonical = Buffer.from(`${res.statusCode}\n${path}\n${responseTimestamp}\n${nonce}\n`);
          const expected = createHmac('sha256', secret).update(Buffer.concat([canonical, bytes])).digest();
          const actual = Buffer.from(responseSignature, 'base64');
          assert.equal(actual.length, expected.length);
          assert.ok(timingSafeEqual(actual, expected), 'response must authenticate exact status/path/nonce/body bytes');
        }
        resolve({ status: res.statusCode, body: bytes.toString('utf8'), headers: res.headers });
      });
    });
    req.on('error', reject); req.end(body);
  });
}

try {
  const id = identity();
  const body = executeBody(id);
  const nonce = crypto.randomUUID();
  const first = send('/execute', body, nonce);
  while (invocationCount === 0) await new Promise(resolve => setImmediate(resolve));
  const duplicate = send('/execute', body, nonce);
  const crossBody = await send('/execute', executeBody(id, { retryCount: 1 }), nonce);
  assert.equal(crossBody.status, 409, 'same authenticated nonce with different bytes must be rejected');
  releaseRunning();
  const [one, two] = await Promise.all([first, duplicate]);
  assert.equal(one.status, 200); assert.equal(two.status, 200);
  assert.equal(invocationCount, 1, 'duplicate execute must join the same pipeline');
  assert.equal(JSON.parse(one.body).status, TickerStatus.Done);

  const recovered = await send('/execute', body, crypto.randomUUID());
  assert.equal(recovered.status, 200);
  assert.equal(invocationCount, 1, 'settled outcome must be recoverable without reinvocation');

  const finalize = await send('/finalize', JSON.stringify({ ...id, controlNonce: crypto.randomUUID() }));
  assert.equal(finalize.status, 200);
  const finalizedReplay = await send('/execute', body, crypto.randomUUID());
  assert.equal(finalizedReplay.status, 410, 'finalized generation must retain a replay-safe tombstone');
  assert.equal(invocationCount, 1);

  const lost = identity();
  const lostBody = executeBody(lost);
  await assert.rejects(send('/execute', lostBody, crypto.randomUUID(), true), /response loss/);
  const reconciled = await send('/execute', lostBody, crypto.randomUUID());
  assert.equal(reconciled.status, 200);
  assert.equal(invocationCount, 2, 'response-loss reconciliation must return the retained outcome without reinvocation');

  const pre = identity();
  const controlNonce = crypto.randomUUID();
  const cancel = send('/cancel', JSON.stringify({ ...pre, controlNonce }));
  await new Promise(resolve => setImmediate(resolve));
  const preExecute = send('/execute', executeBody(pre), crypto.randomUUID());
  const [cancelAck, cancelOutcome] = await Promise.all([cancel, preExecute]);
  assert.equal(cancelAck.status, 200);
  assert.equal(JSON.parse(cancelAck.body).state, 'stopped');
  assert.equal(JSON.parse(cancelOutcome.body).status, TickerStatus.Cancelled);
  assert.equal(invocationCount, 2, 'pre-cancelled execution must never invoke user code');

  const wrongEpoch = identity(); wrongEpoch.nodeEpoch = crypto.randomUUID();
  assert.equal((await send('/execute', executeBody(wrongEpoch), crypto.randomUUID())).status, 409);

  const blocker = new TickerQTaskScheduler(1);
  let blockerRelease;
  const blocked = blocker.queueAsync(() => new Promise(resolve => { blockerRelease = resolve; }), TickerTaskPriority.Normal);
  const queuedAbort = new AbortController();
  let queuedInvoked = false;
  const queued = blocker.queueAsync(async () => { queuedInvoked = true; }, TickerTaskPriority.Normal, queuedAbort.signal);
  queuedAbort.abort();
  await assert.rejects(queued, /abort/i);
  blockerRelease(); await blocked;
  assert.equal(queuedInvoked, false, 'aborted queued work must be removed before invocation');
  blocker.dispose();

  endpoint.beginShutdown();
  assert.equal((await send('/execute', executeBody(identity()), crypto.randomUUID())).status, 503,
    'shutdown must reject new executions');
  assert.equal(await endpoint.shutdown(1000), true);
} finally {
  server.close(); await once(server, 'close');
}
console.log('proof-backed callback protocol tests passed');
