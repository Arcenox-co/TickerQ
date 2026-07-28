import assert from 'node:assert/strict';
import { normalizeExecutionContext } from '../dist/models/RemoteExecutionContext.js';

const parentId = '61ae71f5-7cba-4609-90f5-1366718e1d94';
const acquisitionToken = '13bb9d2e-f5ab-4c50-b7e2-1a4d85c00f9a';
const withParent = normalizeExecutionContext({
  Id: '5f4fd7fe-a221-4f7a-96db-a0fa5f1a9ae7',
  ParentId: parentId,
  Type: 1,
  RetryCount: 0,
  IsDue: false,
  ScheduledFor: '2026-07-28T10:00:00.000Z',
  FunctionName: 'RemoteChild@node-a',
  AcquisitionToken: acquisitionToken,
});
assert.equal(withParent.parentId, parentId);

const root = normalizeExecutionContext({
  Id: '5f4fd7fe-a221-4f7a-96db-a0fa5f1a9ae7',
  Type: 1,
  RetryCount: 0,
  IsDue: false,
  ScheduledFor: '2026-07-28T10:00:00.000Z',
  FunctionName: 'RemoteRoot@node-a',
  acquisitionToken,
});
assert.equal(root.parentId, null);

console.log('parent execution context tests passed');
