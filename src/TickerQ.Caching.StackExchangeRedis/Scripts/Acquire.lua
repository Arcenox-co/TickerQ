-- KEYS[1] = ticker key
-- ARGV[1] = lockHolder, ARGV[2] = now (ISO), ARGV[3] = targetStatus (int)
-- ARGV[4] = expectedUpdatedAt (ISO or empty), ARGV[5] = statusIdle, ARGV[6] = statusQueued
-- Returns: updated JSON on success, nil on failure
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local obj = cjson.decode(json)
local status = obj['Status'] or obj['status']
if status == nil then return nil end
status = tonumber(status)
if status ~= tonumber(ARGV[5]) and status ~= tonumber(ARGV[6]) then return nil end
local holder = obj['LockHolder'] or obj['lockHolder']
if holder and holder ~= '' and holder ~= cjson.null and holder ~= ARGV[1] then return nil end
local expectedUpdatedAt = ARGV[4]
if expectedUpdatedAt ~= '' then
    local currentUpdatedAt = obj['UpdatedAt'] or obj['updatedAt']
    if currentUpdatedAt ~= expectedUpdatedAt then return nil end
end
obj['LockHolder'] = ARGV[1]
obj['lockHolder'] = nil
obj['LockedAt'] = ARGV[2]
obj['lockedAt'] = nil
obj['UpdatedAt'] = ARGV[2]
obj['updatedAt'] = nil
obj['Status'] = tonumber(ARGV[3])
obj['status'] = nil
-- Redis's bundled cjson can't tell an empty JSON array from an empty JSON
-- object once decoded (both become an empty Lua table), so it always
-- re-encodes an empty table as {}. TimeTickerEntity.Children (at any
-- nesting depth, since children are themselves TimeTickerEntity) is always
-- a JSON array, so any "Children":{} in the freshly re-encoded object is
-- always a corrupted empty array, never a legitimate empty object.
local updated = cjson.encode(obj):gsub('"Children":{}', '"Children":[]')
redis.call('SET', KEYS[1], updated)
return updated
