-- Atomic JSON replacement guarded by the observed generation and version.
-- ARGV: expectedHolder, expectedToken, expectedStatus (-1 skips), expectedUpdatedAt, replacementJson
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local obj = cjson.decode(json)
local holder = obj['LockHolder'] or obj['lockHolder']
local token = obj['AcquisitionToken'] or obj['acquisitionToken']
local status = obj['Status'] or obj['status']
local updatedAt = obj['UpdatedAt'] or obj['updatedAt']
if ARGV[1] ~= '' and tostring(holder) ~= ARGV[1] then return nil end
if ARGV[2] ~= '' and (not token or token == cjson.null or string.lower(tostring(token)) ~= string.lower(ARGV[2])) then return nil end
if ARGV[3] ~= '-1' and tonumber(status) ~= tonumber(ARGV[3]) then return nil end
if ARGV[4] ~= '' and tostring(updatedAt) ~= ARGV[4] then return nil end
redis.call('SET', KEYS[1], ARGV[5])
return ARGV[5]
