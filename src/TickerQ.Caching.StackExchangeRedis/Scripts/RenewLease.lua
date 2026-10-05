-- Renew one lease iff this process still owns the same InProgress generation.
-- ARGV: holder, token, inProgressStatus, leaseUntil
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local obj = cjson.decode(json)
local holder = obj['LockHolder'] or obj['lockHolder']
local token = obj['AcquisitionToken'] or obj['acquisitionToken']
local status = obj['Status'] or obj['status']
if tostring(holder) ~= ARGV[1] then return nil end
if not token or token == cjson.null or string.lower(tostring(token)) ~= string.lower(ARGV[2]) then return nil end
if tonumber(status) ~= tonumber(ARGV[3]) then return nil end
obj['LeaseUntil'] = ARGV[4]
obj['leaseUntil'] = nil
local updated = cjson.encode(obj)
-- cjson encodes an empty Lua table as '{}', turning empty JSON arrays ('[]') into objects.
-- Restore the array-typed fields so C# deserialization does not fail on re-encode.
updated = updated:gsub('"Children":{}', '"Children":[]')
updated = updated:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
redis.call('SET', KEYS[1], updated)
return 1
