-- KEYS[1] = ticker key, KEYS[2] = result side key
-- ARGV[1] = lockHolder, ARGV[2] = now (ISO), ARGV[3] = statusIdle, ARGV[4] = statusQueued
-- Returns: updated JSON on success, nil on failure
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local obj = cjson.decode(json)
local status = obj['Status'] or obj['status']
if status == nil then return nil end
status = tonumber(status)
if status ~= tonumber(ARGV[3]) and status ~= tonumber(ARGV[4]) then return nil end
local holder = obj['LockHolder'] or obj['lockHolder']
if holder and holder ~= '' and holder ~= cjson.null and holder ~= ARGV[1] then return nil end
obj['LockHolder'] = cjson.null
obj['lockHolder'] = nil
obj['LockedAt'] = cjson.null
obj['lockedAt'] = nil
obj['LeaseUntil'] = cjson.null
obj['leaseUntil'] = nil
obj['AcquisitionToken'] = cjson.null
obj['acquisitionToken'] = nil
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
