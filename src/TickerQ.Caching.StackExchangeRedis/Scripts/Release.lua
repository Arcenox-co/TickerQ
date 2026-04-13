-- KEYS[1] = ticker key
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
obj['Status'] = tonumber(ARGV[3])
obj['status'] = nil
obj['UpdatedAt'] = ARGV[2]
obj['updatedAt'] = nil
local updated = cjson.encode(obj)
redis.call('SET', KEYS[1], updated)
return updated
