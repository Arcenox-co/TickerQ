-- Recover one stale queued or InProgress row atomically.
-- KEYS: entity, result side key
-- ARGV: now, queuedCutoff, maxRestarts, staleAction, idle, queued, inProgress, cancelled, staleReason
-- Returns Q|json, R|json, C|json, or nil.
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local obj = cjson.decode(json)
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
