import assert from 'node:assert/strict';
import { normalizeExecutionContext } from '../dist/models/RemoteExecutionContext.js';
import { createFunctionContext } from '../dist/models/TickerFunctionContext.js';
import { TickerFunctionProvider } from '../dist/infrastructure/TickerFunctionProvider.js';
import { TickerFunctionBuilder } from '../dist/infrastructure/TickerFunctionBuilder.js';
import { TickerQFunctionSyncService } from '../dist/infrastructure/TickerQFunctionSyncService.js';
import { SdkExecutionEndpoint } from '../dist/middleware/SdkExecutionEndpoint.js';
import { generateSignature } from '../dist/utils/TickerQSignature.js';

const parentValue = { orderId: 'o-1', count: 2 };
const parentEnvelope = {
  envelopeVersion: 1,
  mediaType: 'application/json',
  contractId: 'sha256:parent',
  contractType: 'OrderResult',
  payload: Buffer.from(JSON.stringify(parentValue)).toString('base64'),
};

const normalized = normalizeExecutionContext({ ParentResult: parentEnvelope });
assert.deepEqual(normalized.parentResult, parentEnvelope);
const normalizedPascalEnvelope = normalizeExecutionContext({ ParentResult: {
  EnvelopeVersion: 1,
  MediaType: 'application/json',
  ContractId: 'sha256:parent',
  ContractType: 'OrderResult',
  Payload: parentEnvelope.payload,
} });
assert.deepEqual(normalizedPascalEnvelope.parentResult, parentEnvelope);
assert.throws(
  () => normalizeExecutionContext({ parentResult: { ...parentEnvelope, envelopeVersion: 2 } }),
  /unsupported result envelope version/i,
);
assert.throws(
  () => normalizeExecutionContext({ parentResult: { ...parentEnvelope, payload: 'not base64!' } }),
  /base64/i,
);
assert.throws(
  () => normalizeExecutionContext({
    parentResult: { ...parentEnvelope, payload: Buffer.alloc(1024 * 1024 + 1).toString('base64') },
  }),
  /payload exceeds/i,
);

const withoutParent = createFunctionContext(normalizeExecutionContext({}));
assert.equal(withoutParent.hasParentResult, false);
assert.equal(withoutParent.getParentResult(), undefined);

const withParent = createFunctionContext(normalizeExecutionContext({ parentResult: parentEnvelope }));
assert.equal(withParent.hasParentResult, true);
assert.deepEqual(withParent.getParentResult(), parentValue);
withParent.setResult({ first: true });
withParent.setResult(null);
assert.equal(withParent.resultSink.hasResult, true);
assert.equal(Buffer.from(withParent.resultSink.resultEnvelope.payload, 'base64').toString(), 'null');
const explicitNullParent = createFunctionContext(normalizeExecutionContext({
  parentResult: { ...parentEnvelope, payload: Buffer.from('null').toString('base64') },
}));
assert.equal(explicitNullParent.hasParentResult, true);
assert.equal(explicitNullParent.getParentResult(), null);
assert.throws(
  () => withParent.setResult('x'.repeat(1024 * 1024 + 1)),
  /payload exceeds/i,
);

TickerFunctionProvider.reset();
const resultContract = {
  schema: { type: 'object', properties: { ok: { type: 'boolean' } }, required: ['ok'] },
};
new TickerFunctionBuilder('ProducesResult')
  .withResult({ ok: false }, resultContract)
  .handle(async ctx => ctx.setResult({ ok: true }));
const resultInfo = TickerFunctionProvider.tickerFunctionResultInfos.get('ProducesResult');
assert.ok(resultInfo?.resultContract);
assert.match(resultInfo.resultContract.fingerprint, /^sha256:[0-9a-f]{64}$/);
assert.equal(resultInfo.resultContract.schemaJson, '{"properties":{"ok":{"type":"boolean"}},"required":["ok"],"type":"object"}');

let syncBody;
const sync = new TickerQFunctionSyncService(
  { postAsync: async (_path, body) => { syncBody = body; return null; } },
  { nodeName: 'node', callbackUri: 'https://node.test' },
);
await sync.syncAsync();
assert.deepEqual(syncBody.functions[0].resultContract, resultInfo.resultContract);
assert.equal(syncBody.functions[0].resultType, 'Object');

async function execute(handler, { parentResult, abort = false, retryCount = 0 } = {}) {
  TickerFunctionProvider.reset();
  new TickerFunctionBuilder('Job').handle(handler);
  let scheduled;
  const scheduler = {
    queueAsync(work) {
      const controller = new AbortController();
      if (abort) controller.abort();
      scheduled = work(controller.signal);
      return scheduled;
    },
  };
  const updates = [];
  const persistence = {
    updateTimeTicker: async body => updates.push(JSON.parse(JSON.stringify(body))),
    updateCronTickerOccurrence: async body => updates.push(JSON.parse(JSON.stringify(body))),
  };
  const options = { webhookSignature: 'secret' };
  const endpoint = new SdkExecutionEndpoint(options, {}, scheduler, { getSemaphore: () => null }, persistence);
  const body = {
    Id: crypto.randomUUID(), Type: 0, RetryCount: retryCount, IsDue: false,
    ScheduledFor: new Date().toISOString(), FunctionName: 'Job',
    ...(parentResult === undefined ? {} : { ParentResult: parentResult }),
  };
  const raw = JSON.stringify(body);
  const timestamp = Math.floor(Date.now() / 1000);
  const req = {
    body,
    originalUrl: '/execute', url: '/execute',
    headers: {
      'x-timestamp': String(timestamp),
      'x-tickerq-signature': generateSignature('secret', 'POST', '/execute', timestamp, raw),
    },
  };
  const res = { status() { return this; }, send() {} };
  await endpoint.expressHandlers().execute(req, res);
  await scheduled;
  return updates[0];
}

const success = await execute(async ctx => {
  assert.deepEqual(ctx.getParentResult(), parentValue);
  ctx.setResult({ stale: true });
  ctx.setResult(null);
}, { parentResult: parentEnvelope });
assert.ok(success.resultEnvelope);
assert.ok(success.parametersToUpdate.includes('ResultEnvelope'));
assert.equal(Buffer.from(success.resultEnvelope.payload, 'base64').toString(), 'null');

const failure = await execute(async ctx => {
  ctx.setResult({ mustNotLeak: true });
  throw new Error('boom');
});
assert.equal('resultEnvelope' in failure, false);

const retryFailure = await execute(async ctx => {
  ctx.setResult({ mustNotLeak: true });
  throw new Error('retry me');
}, { retryCount: 2 });
assert.equal('resultEnvelope' in retryFailure, false);

const cancelled = await execute(async ctx => {
  ctx.setResult({ mustNotLeak: true });
  throw Object.assign(new Error('cancelled'), { name: 'AbortError' });
}, { abort: true });
assert.equal('resultEnvelope' in cancelled, false);

const nextAttempt = await execute(async () => {});
assert.equal('resultEnvelope' in nextAttempt, false, 'a new attempt must not inherit the prior attempt result');

console.log('result propagation and contract tests passed');
