import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { TickerQSdkHttpClient } from '../dist/client/TickerQSdkHttpClient.js';
import { TickerQRemotePersistenceProvider } from '../dist/persistence/TickerQRemotePersistenceProvider.js';
import { generateSignature, validateSignature } from '../dist/utils/TickerQSignature.js';

assert.equal(
  generateSignature(
    'node-compatibility-test-secret',
    'PUT',
    '/tickerq/node/time-tickers/context?attempt=1',
    1722180000,
    '{"ok":true}',
  ),
  'rCc2JrrVjhbHogd7/XKohND6byLkyrLJhYg9ER8qIBY=',
  'Node and ASP.NET must share the exact HMAC canonical input',
);

const receivedPaths = [];
const secret = 'node-compatibility-test-secret';
const binaryPayload = Buffer.from([0x00, 0xff, 0xfe, 0x80, 0xc3, 0x28]);
const server = createServer(async (req, res) => {
  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  const body = Buffer.concat(chunks);
  receivedPaths.push(req.url);
  const authError = validateSignature(
    secret,
    req.method,
    req.url,
    req.headers['x-timestamp'],
    req.headers['x-tickerq-signature'],
    body,
  );
  if (authError) {
    res.writeHead(401).end(authError);
    return;
  }
  if (req.url.endsWith('?ack=empty-200')) {
    res.writeHead(200).end();
    return;
  }
  if (req.url.endsWith('?ack=empty-204')) {
    res.writeHead(204).end();
    return;
  }
  if (req.url.endsWith('?ack=stale')) {
    res.writeHead(409).end('stale acquisition');
    return;
  }
  if (req.url.includes('/request/')) {
    res.writeHead(200, { 'Content-Type': 'application/octet-stream' }).end(binaryPayload);
    return;
  }
  res.setHeader('Content-Type', 'application/json');
  res.end('1');
});
server.listen(0, '127.0.0.1');
await once(server, 'listening');
try {
  const { port } = server.address();
  const client = new TickerQSdkHttpClient({
    apiUri: `http://127.0.0.1:${port}/tickerq/node/`,
    hubUri: 'https://hub.tickerq.net/',
    webhookSignature: secret,
    timeoutMs: 5000,
    allowSelfSignedCerts: false,
  });
  const persistence = new TickerQRemotePersistenceProvider(client);
  const affected = await persistence.addTimeTickers([{}, {}]);
  assert.equal(affected, 1, 'CRUD must return the scheduler provider affected count');

  await client.putAsyncOrThrow('/time-tickers/context?ack=empty-200', { ok: true });
  await client.putAsyncOrThrow('/time-tickers/context?ack=empty-204', { ok: true });
  await assert.rejects(
    client.putAsyncOrThrow('/time-tickers/context?ack=stale', { ok: true }),
    /409.*stale acquisition/,
    'non-2xx terminal acknowledgement must propagate',
  );

  const requestBytes = await persistence.getTimeTickerRequest('4d33dff8-e748-4c7f-969c-9da2a134d85f');
  assert.deepEqual(requestBytes, binaryPayload, 'arbitrary non-UTF8 request bytes must round-trip exactly');
  assert.deepEqual(receivedPaths, [
    '/tickerq/node/time-tickers',
    '/tickerq/node/time-tickers/context?ack=empty-200',
    '/tickerq/node/time-tickers/context?ack=empty-204',
    '/tickerq/node/time-tickers/context?ack=stale',
    '/tickerq/node/time-tickers/request/4d33dff8-e748-4c7f-969c-9da2a134d85f',
  ], 'scheduler prefix must be retained exactly once and the actual path/query must be signed');
} finally {
  server.close();
  await once(server, 'close');
}

console.log('scheduler compatibility path, HMAC, and affected-count tests passed');
