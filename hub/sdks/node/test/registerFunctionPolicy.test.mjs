import assert from 'node:assert/strict';
import { TickerFunctionProvider } from '../dist/infrastructure/TickerFunctionProvider.js';

const SCHEMA_DIALECT_2020_12 = 'https://json-schema.org/draft/2020-12/schema';
const noop = async () => {};

function fresh() {
  TickerFunctionProvider.reset();
}

// ── Request-less functions publish without a contract ──
fresh();
TickerFunctionProvider.registerFunction('Cleanup', noop);
assert.equal(TickerFunctionProvider.hasFunction('Cleanup'), true);
assert.equal(TickerFunctionProvider.tickerFunctionRequestInfos.has('Cleanup'), false);

// ── Request-bearing WITHOUT a request contract is rejected before publication ──
fresh();
assert.throws(
  () =>
    TickerFunctionProvider.registerFunction(
      'SendEmail',
      { to: '', subject: '' },
      noop,
    ),
  /request contract|Draft 2020-12|schema/i,
  'request-bearing registration without requestContract must be rejected',
);
// Nothing was published — the registry is untouched.
assert.equal(TickerFunctionProvider.hasFunction('SendEmail'), false);
assert.equal(TickerFunctionProvider.tickerFunctions.has('SendEmail'), false);

// ── Request-bearing WITH a full canonical Draft 2020-12 schema publishes ──
fresh();
TickerFunctionProvider.registerFunction(
  'SendEmail',
  { to: '', subject: '' },
  noop,
  {
    requestContract: {
      schema: { type: 'object', properties: { to: { type: 'string' }, subject: { type: 'string' } } },
    },
  },
);
const info = TickerFunctionProvider.tickerFunctionRequestInfos.get('SendEmail');
assert.ok(info, 'request-bearing registration with a contract must publish request info');
assert.ok(info.requestContract, 'request contract must be present');
assert.equal(info.requestContract.schemaDialect, SCHEMA_DIALECT_2020_12);
assert.ok(info.requestContract.schemaJson.length > 0, 'canonical schema JSON must be emitted');
assert.match(info.requestContract.fingerprint, /^sha256:[0-9a-f]{64}$/);

// ── Conflicting embedded $schema dialect is rejected before publication ──
fresh();
assert.throws(
  () =>
    TickerFunctionProvider.registerFunction(
      'Conflicting',
      { x: 1 },
      noop,
      {
        requestContract: {
          schema: { $schema: 'https://json-schema.org/draft-07/schema#', type: 'object' },
        },
      },
    ),
  /conflicts|dialect/i,
  'conflicting embedded dialect must be rejected',
);
assert.equal(TickerFunctionProvider.hasFunction('Conflicting'), false);

// ── Explicit non-2020-12 schemaDialect is rejected before publication ──
fresh();
assert.throws(
  () =>
    TickerFunctionProvider.registerFunction(
      'BadDialect',
      { x: 1 },
      noop,
      {
        requestContract: {
          schemaDialect: 'https://json-schema.org/draft-07/schema#',
          schema: { type: 'object' },
        },
      },
    ),
  /unsupported|dialect/i,
  'non-2020-12 schema dialect must be rejected',
);
assert.equal(TickerFunctionProvider.hasFunction('BadDialect'), false);

fresh();
console.log('registerFunction policy: request-bearing full-schema enforcement passed');
