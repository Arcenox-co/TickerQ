import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { createServer, request as httpRequest } from 'node:http';
import { once } from 'node:events';
import { TickerQSdk } from '../dist/TickerQSdk.js';
import { TickerType } from '../dist/enums/index.js';
import { generateSignature } from '../dist/utils/TickerQSignature.js';

const secret = 'public-stop-protocol-secret';
const sdk = new TickerQSdk(options => options
  .setApiKey('key').setApiSecret('secret').setCallbackUri('http://localhost').setNodeName('stop-test'));
sdk.options.webhookSignature = secret;
let invoked = 0;
let release;
const gate = new Promise(resolve => { release = resolve; });
const functionName = `public-stop-${Date.now()}`;
sdk.registerFunction(functionName, async () => { invoked++; await gate; });
const server = createServer(sdk.createHandler());
server.listen(0, '127.0.0.1');
await once(server, 'listening');

const identity = { tickerType: TickerType.TimeTicker, tickerId: crypto.randomUUID(), acquisitionToken: crypto.randomUUID(),
  dispatchId: crypto.randomUUID(), nodeEpoch: sdk.options.nodeEpoch };
const body = JSON.stringify({ ...identity, id: identity.tickerId, type: identity.tickerType, executionId: identity.dispatchId,
  functionName, retryCount: 0, isDue: false, scheduledFor: new Date().toISOString(), hasRequest: false, requestPayload: null });
function sendExecute() {
  const path = '/execute'; const nonce = crypto.randomUUID(); const timestamp = Math.floor(Date.now()/1000);
  return new Promise((resolve, reject) => {
    const req = httpRequest({ host: '127.0.0.1', port: server.address().port, path, method: 'POST', headers: {
      'content-type':'application/json', 'content-length':Buffer.byteLength(body), 'x-timestamp':String(timestamp),
      'x-request-nonce':nonce, 'x-tickerq-signature':generateSignature(secret,'POST',path,timestamp,body,nonce),
    }}, res => { res.resume(); res.on('end', () => resolve(res.statusCode)); });
    req.on('error', reject); req.end(body);
  });
}

try {
  const execution = sendExecute();
  while (invoked === 0) await new Promise(resolve => setImmediate(resolve));
  await assert.rejects(sdk.stop(20), /did not settle/i,
    'public stop must fail explicitly without disposing noncooperative work');
  release();
  assert.equal(await execution, 200);
  await sdk.stop(1000);
} finally {
  server.close(); await once(server, 'close');
}

const drainingSdk = new TickerQSdk(options => options
  .setApiKey('key').setApiSecret('secret').setCallbackUri('http://localhost').setNodeName('drain-test'));
drainingSdk.options.webhookSignature = secret;
let activeInvoked = 0;
let pendingInvoked = 0;
let releaseActive;
const activeGate = new Promise(resolve => { releaseActive = resolve; });
const settledName = `public-stop-settled-${Date.now()}`;
const activeName = `public-stop-active-${Date.now()}`;
const pendingName = `public-stop-pending-${Date.now()}`;
drainingSdk.registerFunction(settledName, async () => {});
drainingSdk.registerFunction(activeName, async () => { activeInvoked++; await activeGate; });
drainingSdk.registerFunction(pendingName, async () => { pendingInvoked++; });
const drainingServer = createServer(drainingSdk.createHandler());
drainingServer.listen(0, '127.0.0.1');
await once(drainingServer, 'listening');

function drainingIdentity() {
  return { tickerType: TickerType.TimeTicker, tickerId: crypto.randomUUID(), acquisitionToken: crypto.randomUUID(),
    dispatchId: crypto.randomUUID(), nodeEpoch: drainingSdk.options.nodeEpoch };
}
function drainingBody(identity, name) {
  return JSON.stringify({ ...identity, id: identity.tickerId, type: identity.tickerType, executionId: identity.dispatchId,
    functionName: name, retryCount: 0, isDue: false, scheduledFor: new Date().toISOString(), hasRequest: false, requestPayload: null });
}
function drainingSend(path, requestBody) {
  const nonce = crypto.randomUUID(); const timestamp = Math.floor(Date.now()/1000);
  return new Promise((resolve, reject) => {
    const req = httpRequest({ host: '127.0.0.1', port: drainingServer.address().port, path, method: 'POST', headers: {
      'content-type':'application/json', 'content-length':Buffer.byteLength(requestBody), 'x-timestamp':String(timestamp),
      'x-request-nonce':nonce, 'x-tickerq-signature':generateSignature(secret,'POST',path,timestamp,requestBody,nonce),
    }}, res => { res.resume(); res.on('end', () => resolve(res.statusCode)); });
    req.on('error', reject); req.end(requestBody);
  });
}

try {
  const settledIdentity = drainingIdentity();
  const settledBody = drainingBody(settledIdentity, settledName);
  assert.equal(await drainingSend('/execute', settledBody), 200);

  const activeIdentity = drainingIdentity();
  const activeBody = drainingBody(activeIdentity, activeName);
  const activeExecution = drainingSend('/execute', activeBody);
  while (activeInvoked === 0) await new Promise(resolve => setImmediate(resolve));

  const pendingIdentity = drainingIdentity();
  const pendingBody = drainingBody(pendingIdentity, pendingName);
  const pendingControl = JSON.stringify({ ...pendingIdentity, controlNonce: crypto.randomUUID() });
  assert.equal(await drainingSend('/cancel', pendingControl), 202);

  const stopping = drainingSdk.stop(1000);
  assert.equal(await drainingSend('/execute', settledBody), 200, 'settled duplicate must recover during public stop');
  const activeDuplicate = drainingSend('/execute', activeBody);
  assert.equal(await drainingSend('/execute', pendingBody), 200, 'matching execute must reconcile cancelPending during public stop');
  assert.equal(pendingInvoked, 0);
  assert.equal(await drainingSend('/execute', drainingBody(drainingIdentity(), settledName)), 503,
    'public stop must reject only genuinely new dispatches');
  releaseActive();
  assert.equal(await activeExecution, 200);
  assert.equal(await activeDuplicate, 200, 'active duplicate must join during public stop');
  assert.equal(activeInvoked, 1);
  await stopping;
} finally {
  releaseActive();
  drainingServer.close(); await once(drainingServer, 'close');
}
console.log('public SDK stop protocol tests passed');