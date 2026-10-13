import assert from 'node:assert/strict';
import {
  canonicalizeSchema,
  computeContractFingerprint,
} from '../dist/infrastructure/CanonicalContract.js';

const fp = (schema, required = true) =>
  computeContractFingerprint(canonicalizeSchema(schema), 'application/json', required, 1);

const vectors = {
  TypeObject: ['sha256:e1b4b0867d3da6c85f03eed3e2e582801ae74c7fef9d5260ca8e2aa301e38e5e', { type: 'object' }, true],
  CafeKeyValue: ['sha256:85ffa622c738d6d7dd1cd8fd1521cd9f9a239eb6b256274608218f17c2884b6b', { café: 'café' }, true],
  NonBmp: ['sha256:4b42d357f891c1ef7f703cdf0c86e83d04f4dc7bdd79bae3f69a2346f1bd02d5', { k: '😀' }, true],
  Controls: ['sha256:4c34c4eaa6e7fc3f86c8842d55731ffb88d9b32c2721ae4b12b2b2fbc41f5f9c', { k: 'a\u0001\u001b\tb\nc' }, true],
  NegativeZero: ['sha256:4d669982e17a1b6dde3f164334287ede36e6ad1378ed133d94b4f477e8646bcc', { n: -0 }, true],
  IntNormalized: ['sha256:a4f480a63a85b15a560252a12843eb491c2af86c62cb061120ace43353ab6f6b', { n: 1e2 }, true],
  SafeIntBoundary: ['sha256:af96f8bae4d7605a45b9927de270d7559f69512c71a111f07204dbb460fb93f0', { n: Number.MAX_SAFE_INTEGER }, true],
  NestedSorted: ['sha256:3586bbb3bc0643cba3985e45f082e8432f998e012c1bde139d732f6004fc6231', { b: { y: 2, x: 1 }, a: [3, 2, 1] }, true],
  RequiredFalse: ['sha256:5bfb64a13d90f838565e0e9320f10f02bbf31d395987e728c2804d7f8c465e2a', { type: 'object' }, false],
};

for (const [name, [expected, schema, required]] of Object.entries(vectors)) {
  assert.equal(fp(schema, required), expected, name);
}

assert.equal(canonicalizeSchema({ café: 'café' }), '{"café":"café"}');
assert.equal(canonicalizeSchema({ k: '😀' }), '{"k":"😀"}');
assert.equal(canonicalizeSchema({ n: -0 }), '{"n":0}');
assert.throws(() => canonicalizeSchema({ n: 0.1 }), /not integer-valued/);
assert.throws(() => canonicalizeSchema({ n: Number.MAX_SAFE_INTEGER + 1 }), /safe-integer range/);
assert.throws(() => canonicalizeSchema({ k: '\ud800' }), /unpaired surrogate/);

console.log(`canonical contract parity: ${Object.keys(vectors).length} golden vectors passed`);
