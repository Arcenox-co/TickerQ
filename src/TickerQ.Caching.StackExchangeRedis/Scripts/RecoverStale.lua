-- Recover one stale queued or InProgress row atomically.
-- KEYS: entity, result side key, optional authoritative Cron definition, activation metadata
-- ARGV: now, queuedCutoff, maxRestarts, staleAction, idle, queued, inProgress, cancelled,
--       staleReason, skipped, staleRevisionReason, exact supported epoch,
--       scoped | legacy | invalid runtime admission mode
-- Returns Q|json, R|json, C|json, S|json, or nil.
local activationType = ARGV[13] == 'scoped' and redis.call('TYPE', KEYS[#KEYS])['ok'] or 'none'
if ARGV[13] == 'invalid' then return nil end
if ARGV[13] ~= 'scoped' and ARGV[13] ~= 'legacy' then return redis.error_reply('invalid runtime admission mode') end
if activationType ~= 'none' and activationType ~= 'hash' then return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type') end
if ARGV[13] == 'scoped' and activationType == 'none' then return nil end
if ARGV[13] == 'scoped' and activationType == 'hash' then
  local metadata = redis.call('HGETALL', KEYS[#KEYS])
  if #metadata ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
  local fields = {}; for i = 1, #metadata, 2 do fields[metadata[i]] = metadata[i + 1] end
  local phase = fields['phase']
  if not fields['epoch'] or not phase or fields['checkpoint'] == nil or not string.match(fields['epoch'], '^[0-9]+$')
    or #fields['epoch'] > 19 or #fields['checkpoint'] > 200 or (phase ~= '0' and phase ~= '1' and phase ~= '2') then
    return redis.error_reply('reconciliation activation metadata is corrupt')
  end
  if phase ~= '2' or fields['epoch'] ~= ARGV[12] then return nil end
end
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local ok, obj = pcall(cjson.decode, json)
if not ok or type(obj) ~= 'table' then return nil end
local status = tonumber(obj['Status'] or obj['status'])
local holder = obj['LockHolder'] or obj['lockHolder']
local lockedAt = obj['LockedAt'] or obj['lockedAt']
local leaseUntil = obj['LeaseUntil'] or obj['leaseUntil']
-- cjson encodes an empty Lua table as '{}', turning empty JSON arrays ('[]') into objects.
-- Restore the array-typed fields so C# deserialization does not fail on re-encode.
local function encode(o)
  local s = cjson.encode(o)
  s = s:gsub('"Children":{}', '"Children":[]')
  s = s:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
  return s
end
local resultKey = tostring(KEYS[2])
local resultPrefix = string.match(resultKey, '^(.*:tt:)[^:]+:result$')
local function clearOwnership(target)
  target['LockHolder'] = cjson.null; target['lockHolder'] = nil
  target['LockedAt'] = cjson.null; target['lockedAt'] = nil
  target['LeaseUntil'] = cjson.null; target['leaseUntil'] = nil
  target['AcquisitionToken'] = cjson.null; target['acquisitionToken'] = nil
  local children = target['Children'] or target['children']
  if type(children) == 'table' then
    for _, child in pairs(children) do
      local id = child['Id'] or child['id']
      if resultPrefix and id and id ~= cjson.null then redis.call('DEL', resultPrefix .. tostring(id) .. ':result') end
      clearOwnership(child)
    end
  end
end
if #KEYS == 4 then
  local definitionJson = redis.call('GET', KEYS[3])
  local definitionOk, definition = pcall(cjson.decode, definitionJson or '')
  local occurrenceRevision = tonumber(obj['DefinitionRevision'] or obj['definitionRevision'] or 0)
  local definitionRevision = definitionOk and type(definition) == 'table' and
    tonumber(definition['DefinitionRevision'] or definition['definitionRevision'] or 0) or nil
  if not definitionRevision or occurrenceRevision ~= definitionRevision then
    local holderEmpty = not holder or holder == '' or holder == cjson.null
    local leaseExpired = not leaseUntil or leaseUntil == '' or leaseUntil == cjson.null or tostring(leaseUntil) < ARGV[1]
    local pendingRecoverable = (status == tonumber(ARGV[5]) or status == tonumber(ARGV[6])) and
      leaseExpired and (holderEmpty or (lockedAt and lockedAt ~= cjson.null and tostring(lockedAt) < ARGV[2]))
    local runningRecoverable = status == tonumber(ARGV[7]) and leaseExpired
    if not pendingRecoverable and not runningRecoverable then return nil end
    obj['Status'] = tonumber(ARGV[10]); obj['status'] = nil
    obj['SkippedReason'] = ARGV[11]; obj['skippedReason'] = nil
    obj['ExecutedAt'] = obj['ExecutedAt'] or ARGV[1]; obj['executedAt'] = nil
    obj['UpdatedAt'] = ARGV[1]; obj['updatedAt'] = nil
    clearOwnership(obj)
    local updated = encode(obj); redis.call('SET', KEYS[1], updated); return 'S|' .. updated
  end
end
if (status == tonumber(ARGV[5]) or status == tonumber(ARGV[6])) and holder and holder ~= cjson.null and lockedAt and lockedAt ~= cjson.null and tostring(lockedAt) < ARGV[2] then
  obj['Status'] = tonumber(ARGV[5]); obj['status'] = nil
  obj['UpdatedAt'] = ARGV[1]; obj['updatedAt'] = nil
  clearOwnership(obj)
  local updated = encode(obj); redis.call('SET', KEYS[1], updated); redis.call('DEL', KEYS[2]); return 'Q|' .. updated
end
if status ~= tonumber(ARGV[7]) or not leaseUntil or leaseUntil == cjson.null or tostring(leaseUntil) >= ARGV[1] then return nil end
local count = tonumber(obj['StaleRestartCount'] or obj['staleRestartCount'] or 0)
if tonumber(ARGV[4]) == 0 and count < tonumber(ARGV[3]) then
  obj['Status'] = tonumber(ARGV[5]); obj['status'] = nil
  obj['StaleRestartCount'] = count + 1; obj['staleRestartCount'] = nil
  obj['UpdatedAt'] = ARGV[1]; obj['updatedAt'] = nil
  clearOwnership(obj)
  local updated = encode(obj); redis.call('SET', KEYS[1], updated); redis.call('DEL', KEYS[2]); return 'R|' .. updated
end
obj['Status'] = tonumber(ARGV[8]); obj['status'] = nil
obj['ExceptionMessage'] = ARGV[9]; obj['exceptionMessage'] = nil
obj['ExecutedAt'] = ARGV[1]; obj['executedAt'] = nil
obj['UpdatedAt'] = ARGV[1]; obj['updatedAt'] = nil
clearOwnership(obj)
local updated = encode(obj); redis.call('SET', KEYS[1], updated); redis.call('DEL', KEYS[2]); return 'C|' .. updated
