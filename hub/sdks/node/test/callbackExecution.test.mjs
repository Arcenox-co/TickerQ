import assert from 'node:assert/strict';
import crypto, { createHmac, timingSafeEqual } from 'node:crypto';
import { createServer, request as httpRequest } from 'node:http';
import { once } from 'node:events';
import { TickerSdkOptions } from '../dist/TickerSdkOptions.js';
import { TickerFunctionProvider } from '../dist/infrastructure/TickerFunctionProvider.js';
import { SdkExecutionEndpoint } from '../dist/middleware/SdkExecutionEndpoint.js';
import { TickerQTaskScheduler } from '../dist/worker/TickerQTaskScheduler.js';
import { TickerFunctionConcurrencyGate } from '../dist/worker/TickerFunctionConcurrencyGate.js';
import { TickerTaskPriority, TickerStatus, TickerType } from '../dist/enums/index.js';
import { generateSignature } from '../dist/utils/TickerQSignature.js';

const secret = 'callback-outcome-secret';
const epoch = crypto.randomUUID();
const options = new TickerSdkOptions();
options.webhookSignature = secret;
options.nodeEpoch = epoch;
let persistenceCalls = 0;
const persistence = {
  updateTimeTicker: async () => { persistenceCalls++; },
  updateCronTickerOccurrence: async () => { persistenceCalls++; },
};

const functionName = `callback-result-${Date.now()}`;
let receivedRequest;
TickerFunctionProvider.registerFunction(functionName,
  { message: 'registration-default', count: -1 },
  async ctx => {
    receivedRequest = ctx.request;
    await new Promise(resolve => setTimeout(resolve, 20));
    ctx.setResult({ answer: 42 });
  }, {
    priority: TickerTaskPriority.Normal,
    requestType: 'CallbackRequest',
    requestContract: {
      schema: {
        type: 'object',
        properties: { message: { type: 'string' }, count: { type: 'integer' } },
        required: ['message', 'count'],
        additionalProperties: false,
      },
    },
    resultType: 'CallbackResult',
    resultContract: { schema: { type: 'object', properties: { answer: { type: 'integer' } }, required: ['answer'] } },
  });

const cancellationFunctionName = `callback-cancel-${Date.now()}`;
let markExecutionStarted;
const executionStarted = new Promise(resolve => { markExecutionStarted = resolve; });
let markExecutionAborted;
const executionAborted = new Promise(resolve => { markExecutionAborted = resolve; });
let cancellationHandlerReachedSuccess = false;
TickerFunctionProvider.registerFunction(cancellationFunctionName, async (_ctx, signal) => {
  markExecutionStarted();
  await new Promise((resolve, reject) => {
    if (signal.aborted) {
      markExecutionAborted();
      reject(Object.assign(new Error('aborted'), { name: 'AbortError' }));
      return;
    }
    signal.addEventListener('abort', () => {
      markExecutionAborted();
      reject(Object.assign(new Error('aborted'), { name: 'AbortError' }));
    }, { once: true });
  });
  cancellationHandlerReachedSuccess = true;
});

const delayedCancellationFunctionName = `callback-delayed-cancel-${Date.now()}`;
let markDelayedStarted;
const delayedStarted = new Promise(resolve => { markDelayedStarted = resolve; });
let releaseDelayed;
const delayedRelease = new Promise(resolve => { releaseDelayed = resolve; });
let delayedExited = false;
TickerFunctionProvider.registerFunction(delayedCancellationFunctionName, async () => {
  markDelayedStarted();
  await delayedRelease;
  delayedExited = true;
});

const endpoint = new SdkExecutionEndpoint(options, { syncAsync: async () => {} },
  new TickerQTaskScheduler(1), new TickerFunctionConcurrencyGate(), persistence);
const server = createServer(endpoint.createHandler());
server.listen(0, '127.0.0.1');
await once(server, 'listening');

function identity() {
  return {
    tickerType: TickerType.TimeTicker,
    tickerId: crypto.randomUUID(),
    acquisitionToken: crypto.randomUUID(),
    dispatchId: crypto.randomUUID(),
    nodeEpoch: epoch,
  };
}

function executionBody(id, callbackName, overrides = {}) {
  return JSON.stringify({
    ...id,
    id: id.tickerId,
    type: id.tickerType,
    executionId: id.dispatchId,
    functionName: callbackName,
    retryCount: 0,
    isDue: false,
    scheduledFor: new Date().toISOString(),
    hasRequest: false,
    requestPayload: null,
    ...overrides,
  });
}

function controlBody(id) {
  return JSON.stringify({ ...id, controlNonce: crypto.randomUUID() });
}

function send(path, body, signatureSecret = secret, nonce = crypto.randomUUID()) {
  const timestamp = Math.floor(Date.now() / 1000);
  const signature = generateSignature(signatureSecret, 'POST', path, timestamp, body, nonce);
  return new Promise((resolve, reject) => {
    const req = httpRequest({
      host: '127.0.0.1', port: server.address().port, path, method: 'POST',
      headers: {
        'content-type': 'application/json', 'content-length': Buffer.byteLength(body),
        'x-timestamp': String(timestamp), 'x-request-nonce': nonce, 'x-tickerq-signature': signature,
      },
    }, res => {
      const chunks = [];
      res.on('data', chunk => chunks.push(chunk));
      res.on('end', () => {
        const bytes = Buffer.concat(chunks);
        if (res.headers['x-tickerq-signature']) {
          const responseTimestamp = res.headers['x-response-timestamp'];
          assert.equal(res.headers['x-request-nonce'], nonce);
          const canonical = Buffer.from(`${res.statusCode}\n${path}\n${responseTimestamp}\n${nonce}\n`);
          const expected = createHmac('sha256', secret).update(Buffer.concat([canonical, bytes])).digest();
          const actual = Buffer.from(res.headers['x-tickerq-signature'], 'base64');
          assert.equal(actual.length, expected.length);
          assert.ok(timingSafeEqual(actual, expected), 'response must authenticate exact status, path, nonce, and body bytes');
        }
        resolve({ status: res.statusCode, body: bytes.toString('utf8'), headers: res.headers });
      });
    });
    req.on('error', reject);
    req.end(body);
  });
}

try {
  const resultIdentity = identity();
  const invocationRequest = { message: 'persisted-not-registration-default', count: 7 };
  const body = executionBody(resultIdentity, functionName, {
    hasRequest: true,
    requestPayload: Buffer.from(JSON.stringify(invocationRequest)).toString('base64'),
  });
  const response = await send('/execute', body);
  assert.equal(response.status, 200);
  const outcome = JSON.parse(response.body);
  assert.deepEqual(outcome.identity, resultIdentity);
  assert.equal(outcome.status, TickerStatus.Done);
  assert.deepEqual(receivedRequest, invocationRequest, 'callback must execute the persisted invocation request, not the registration default');
  assert.equal(outcome.resultEnvelope.envelopeVersion, 1);
  assert.deepEqual(JSON.parse(Buffer.from(outcome.resultEnvelope.payload, 'base64').toString('utf8')), { answer: 42 });
  assert.equal(persistenceCalls, 0, 'callback runtime must return outcome; only scheduler commits terminal state');
  assert.equal((await send('/finalize', controlBody(resultIdentity))).status, 200);
  assert.equal((await send('/execute', body)).status, 410, 'finalized execution must retain a replay-safe tombstone');

  const cancellationIdentity = identity();
  const cancellationExecution = send('/execute', executionBody(cancellationIdentity, cancellationFunctionName));
  await executionStarted;
  const cancelResponse = await send('/cancel', controlBody(cancellationIdentity));
  assert.equal(cancelResponse.status, 200);
  assert.equal(JSON.parse(cancelResponse.body).status, undefined);
  assert.equal(JSON.parse(cancelResponse.body).outcome.status, TickerStatus.Cancelled);
  await executionAborted;
  assert.equal((await cancellationExecution).status, 200);
  assert.equal(cancellationHandlerReachedSuccess, false, 'aborted user code must not reach terminal success');

  const forged = await send('/cancel', controlBody(cancellationIdentity), 'forged-secret');
  assert.equal(forged.status, 401, 'forged cancellation must be rejected');

  const delayedIdentity = identity();
  const delayedExecution = send('/execute', executionBody(delayedIdentity, delayedCancellationFunctionName));
  await delayedStarted;
  let cancelAcknowledged = false;
  const delayedCancel = send('/cancel', controlBody(delayedIdentity)).then(value => {
    cancelAcknowledged = true;
    return value;
  });
  await new Promise(resolve => setTimeout(resolve, 50));
  assert.equal(cancelAcknowledged, false, 'cancel must not acknowledge before noncooperative user code exits');
  releaseDelayed();
  assert.equal((await delayedCancel).status, 200);
  assert.equal(delayedExited, true);
  assert.equal((await delayedExecution).status, 200);

  const earlyIdentity = identity();
  const earlyCancel = send('/cancel', controlBody(earlyIdentity));
  await new Promise(resolve => setTimeout(resolve, 20));
  const earlyExecution = send('/execute', executionBody(earlyIdentity, cancellationFunctionName));
  const [earlyCancelResponse, earlyExecutionResponse] = await Promise.all([earlyCancel, earlyExecution]);
  assert.equal(earlyCancelResponse.status, 200, 'cancel-before-registration must be retained and acknowledged after settlement');
  assert.equal(JSON.parse(earlyCancelResponse.body).outcome.status, TickerStatus.Cancelled);
  assert.equal(earlyExecutionResponse.status, 200);

  const oversized = await send('/execute', 'x'.repeat(2 * 1024 * 1024 + 1));
  assert.equal(oversized.status, 413);
} finally {
  server.close();
  await once(server, 'close');
}
console.log('callback terminal outcome and body-limit tests passed');
