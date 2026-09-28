-- KEYS[1] = ticker key, KEYS[2] = result side key, KEYS[3] = activation metadata
-- ARGV[1] = lockHolder, ARGV[2] = now (ISO), ARGV[3] = statusIdle, ARGV[4] = statusQueued,
-- ARGV[5] = exact supported epoch, ARGV[6] = scoped | legacy | invalid runtime admission mode
-- Returns: updated JSON on success, nil on failure
local activationType = redis.call('TYPE', KEYS[3])['ok']
if ARGV[6] == 'invalid' then return nil end
if ARGV[6] ~= 'scoped' and ARGV[6] ~= 'legacy' then return redis.error_reply('invalid runtime admission mode') end
if activationType ~= 'none' and activationType ~= 'hash' then return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type') end
if ARGV[6] == 'legacy' and activationType == 'hash' and
   redis.call('HGET', KEYS[3], 'legacyAdoptionState') then return nil end
if ARGV[6] == 'scoped' and activationType == 'none' then return nil end
if ARGV[6] == 'scoped' and activationType == 'hash' then
  local metadata = redis.call('HGETALL', KEYS[3])
  if #metadata ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
  local fields = {}; for i = 1, #metadata, 2 do fields[metadata[i]] = metadata[i + 1] end
  local phase = fields['phase']
  if not fields['epoch'] or not phase or fields['checkpoint'] == nil or not string.match(fields['epoch'], '^[0-9]+$')
    or #fields['epoch'] > 19 or #fields['checkpoint'] > 200 or (phase ~= '0' and phase ~= '1' and phase ~= '2') then
    return redis.error_reply('reconciliation activation metadata is corrupt')
  end
  if phase ~= '2' or fields['epoch'] ~= ARGV[5] then return nil end
end
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local obj = cjson.decode(json)
local status = obj['Status'] or obj['status']
if status == nil then return nil end
status = tonumber(status)
if status ~= tonumber(ARGV[3]) and status ~= tonumber(ARGV[4]) then return nil end
local holder = obj['LockHolder'] or obj['lockHolder']
if not holder or holder == '' or holder == cjson.null or holder ~= ARGV[1] then return nil end
local token = obj['AcquisitionToken'] or obj['acquisitionToken']
if not token or token == '' or token == cjson.null then return nil end
obj['LockHolder'] = cjson.null
obj['lockHolder'] = nil
obj['LockedAt'] = cjson.null
obj['lockedAt'] = nil
obj['LeaseUntil'] = cjson.null
obj['leaseUntil'] = nil
obj['AcquisitionToken'] = cjson.null
obj['acquisitionToken'] = nil
if obj['ChainGeneration'] ~= nil or obj['chainGeneration'] ~= nil then
  obj['ChainGeneration'] = cjson.null
  obj['chainGeneration'] = nil
end
local resultKey = tostring(KEYS[2])
local resultPrefix = string.match(resultKey, '^(.*:tt:)[^:]+:result$')
local function clearChildren(children)
  if type(children) ~= 'table' then return end
  for _, child in pairs(children) do
    child['LockHolder'] = cjson.null; child['lockHolder'] = nil
    child['LockedAt'] = cjson.null; child['lockedAt'] = nil
    child['LeaseUntil'] = cjson.null; child['leaseUntil'] = nil
    child['AcquisitionToken'] = cjson.null; child['acquisitionToken'] = nil
    local id = child['Id'] or child['id']
    if resultPrefix and id and id ~= cjson.null then redis.call('DEL', resultPrefix .. tostring(id) .. ':result') end
    clearChildren(child['Children'] or child['children'])
  end
end
clearChildren(obj['Children'] or obj['children'])
obj['Status'] = tonumber(ARGV[3])
obj['status'] = nil
obj['UpdatedAt'] = ARGV[2]
obj['updatedAt'] = nil
local updated = cjson.encode(obj)
-- cjson encodes an empty Lua table as '{}', turning empty JSON arrays ('[]') into objects.
-- Restore the array-typed fields so C# deserialization does not fail on re-encode.
updated = updated:gsub('"Children":{}', '"Children":[]')
updated = updated:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
redis.call('SET', KEYS[1], updated)
redis.call('DEL', KEYS[2])
return updated
