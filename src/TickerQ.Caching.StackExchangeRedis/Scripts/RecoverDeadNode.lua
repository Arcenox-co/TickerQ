-- KEYS[1] = ticker key
-- ARGV[1] = deadNodeId, ARGV[2] = now (ISO)
-- ARGV[3] = statusIdle, ARGV[4] = statusQueued, ARGV[5] = statusInProgress
-- Returns: updated JSON on success, nil on failure
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local obj = cjson.decode(json)
local holder = obj['LockHolder'] or obj['lockHolder']
if holder ~= ARGV[1] then return nil end
local status = obj['Status'] or obj['status']
if status == nil then return nil end
status = tonumber(status)
if status ~= tonumber(ARGV[3]) and status ~= tonumber(ARGV[4]) and status ~= tonumber(ARGV[5]) then return nil end
obj['LockHolder'] = cjson.null
obj['lockHolder'] = nil
obj['LockedAt'] = cjson.null
obj['lockedAt'] = nil
obj['LeaseUntil'] = cjson.null
obj['leaseUntil'] = nil
obj['AcquisitionToken'] = cjson.null
obj['acquisitionToken'] = nil
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
return updated
