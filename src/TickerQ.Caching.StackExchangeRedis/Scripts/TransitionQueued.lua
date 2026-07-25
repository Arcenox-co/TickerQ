-- KEYS[1] = ticker key
-- ARGV[1] = lockHolder, ARGV[2] = expected acquisition token,
-- ARGV[3] = now (ISO), ARGV[4] = statusQueued, ARGV[5] = statusInProgress, ARGV[6] = leaseUntil
-- Returns: updated JSON on success, nil on failure
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local obj = cjson.decode(json)
local status = obj['Status'] or obj['status']
if tonumber(status) ~= tonumber(ARGV[4]) then return nil end
local holder = obj['LockHolder'] or obj['lockHolder']
if not holder or holder == '' or holder == cjson.null or holder ~= ARGV[1] then return nil end
local token = obj['AcquisitionToken'] or obj['acquisitionToken']
if not token or token == cjson.null or string.lower(tostring(token)) ~= string.lower(ARGV[2]) then return nil end
obj['Status'] = tonumber(ARGV[5])
obj['status'] = nil
obj['UpdatedAt'] = ARGV[3]
obj['updatedAt'] = nil
obj['LeaseUntil'] = ARGV[6]
obj['leaseUntil'] = nil
local updated = cjson.encode(obj)
-- cjson encodes an empty Lua table as '{}', turning empty JSON arrays ('[]') into objects.
-- Restore the array-typed fields so C# deserialization does not fail on re-encode.
updated = updated:gsub('"Children":{}', '"Children":[]')
updated = updated:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
redis.call('SET', KEYS[1], updated)
return updated
