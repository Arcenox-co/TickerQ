-- Returns 1 iff this process still owns the same InProgress generation.
-- ARGV: holder, token, inProgressStatus
local json = redis.call('GET', KEYS[1])
if not json then return 0 end
local obj = cjson.decode(json)
local holder = obj['LockHolder'] or obj['lockHolder']
local token = obj['AcquisitionToken'] or obj['acquisitionToken']
local status = obj['Status'] or obj['status']
if tostring(holder) ~= ARGV[1] then return 0 end
if not token or token == cjson.null or string.lower(tostring(token)) ~= string.lower(ARGV[2]) then return 0 end
if tonumber(status) ~= tonumber(ARGV[3]) then return 0 end
return 1
