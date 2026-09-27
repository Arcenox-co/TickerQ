-- Atomically revive one non-running ticker for on-demand execution.
-- KEYS: entity, result side key, terminal evidence hash, activation metadata
-- ARGV: holder, now, leaseUntil, token, inProgressStatus, executionTime, queuedStatus, exact supported epoch,
--       scoped | legacy | invalid runtime admission mode
local activationType = ARGV[9] == 'scoped' and redis.call('TYPE', KEYS[4])['ok'] or 'none'
if ARGV[9] == 'invalid' then return nil end
if ARGV[9] ~= 'scoped' and ARGV[9] ~= 'legacy' then return redis.error_reply('invalid runtime admission mode') end
if activationType ~= 'none' and activationType ~= 'hash' then
  return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
end
if ARGV[9] == 'scoped' and activationType == 'none' then return nil end
if ARGV[9] == 'scoped' and activationType == 'hash' then
  local metadata = redis.call('HGETALL', KEYS[4])
  if #metadata ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
  local fields = {}
  for i = 1, #metadata, 2 do fields[metadata[i]] = metadata[i + 1] end
  local phase = fields['phase']
  if not fields['epoch'] or not phase or fields['checkpoint'] == nil or
     not string.match(fields['epoch'], '^[0-9]+$') or #fields['epoch'] > 19 or #fields['checkpoint'] > 200 then
    return redis.error_reply('reconciliation activation metadata is corrupt')
  end
  if phase ~= '0' and phase ~= '1' and phase ~= '2' then return redis.error_reply('reconciliation activation metadata is corrupt') end
  if phase ~= '2' or fields['epoch'] ~= ARGV[8] then return nil end
end
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local obj = cjson.decode(json)
local status = tonumber(obj['Status'] or obj['status'])
local holder = obj['LockHolder'] or obj['lockHolder']
if status == tonumber(ARGV[5]) then return nil end
if status == tonumber(ARGV[7]) and holder and holder ~= '' and holder ~= cjson.null and holder ~= ARGV[1] then return nil end
obj['LockHolder'] = ARGV[1]; obj['lockHolder'] = nil
obj['LockedAt'] = ARGV[2]; obj['lockedAt'] = nil
obj['LeaseUntil'] = ARGV[3]; obj['leaseUntil'] = nil
obj['AcquisitionToken'] = ARGV[4]; obj['acquisitionToken'] = nil
local rootId = obj['Id'] or obj['id']
if not rootId or rootId == cjson.null then return nil end
redis.call('HDEL', KEYS[3], '1:' .. string.lower(tostring(rootId)))
obj['ChainRootId'] = tostring(rootId); obj['chainRootId'] = nil
obj['ChainGeneration'] = ARGV[4]; obj['chainGeneration'] = nil
obj['Status'] = tonumber(ARGV[5]); obj['status'] = nil
obj['ExecutionTime'] = ARGV[6]; obj['executionTime'] = nil
obj['UpdatedAt'] = ARGV[2]; obj['updatedAt'] = nil
obj['ExecutedAt'] = cjson.null; obj['executedAt'] = nil
obj['ExceptionMessage'] = cjson.null; obj['exceptionMessage'] = nil
obj['SkippedReason'] = cjson.null; obj['skippedReason'] = nil
obj['ElapsedTime'] = 0; obj['elapsedTime'] = nil
obj['RetryCount'] = 0; obj['retryCount'] = nil
obj['StaleRestartCount'] = 0; obj['staleRestartCount'] = nil
local resultKey = tostring(KEYS[2])
local resultPrefix = string.match(resultKey, '^(.*:tt:)[^:]+:result$')
local function fenceChildren(children)
  if type(children) ~= 'table' then return end
  for _, child in pairs(children) do
    child['LockHolder'] = ARGV[1]; child['lockHolder'] = nil
    child['LockedAt'] = ARGV[2]; child['lockedAt'] = nil
    child['AcquisitionToken'] = ARGV[4]; child['acquisitionToken'] = nil
    child['ChainRootId'] = tostring(rootId); child['chainRootId'] = nil
    child['ChainGeneration'] = ARGV[4]; child['chainGeneration'] = nil
    local id = child['Id'] or child['id']
    if resultPrefix and id and id ~= cjson.null then
      redis.call('DEL', resultPrefix .. tostring(id) .. ':result')
      redis.call('HDEL', KEYS[3], '1:' .. string.lower(tostring(id)))
    end
    fenceChildren(child['Children'] or child['children'])
  end
end
fenceChildren(obj['Children'] or obj['children'])
local updated = cjson.encode(obj)
-- cjson encodes an empty Lua table as '{}', turning empty JSON arrays ('[]') into objects.
-- Restore the array-typed fields so C# deserialization does not fail on re-encode.
updated = updated:gsub('"Children":{}', '"Children":[]')
updated = updated:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
redis.call('SET', KEYS[1], updated)
redis.call('DEL', KEYS[2])
return updated
