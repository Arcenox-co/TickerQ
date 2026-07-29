-- KEYS[1] = ticker key, KEYS[2] = result side key
-- ARGV[1] = lockHolder, ARGV[2] = now (ISO), ARGV[3] = targetStatus (int)
-- ARGV[4] = expectedUpdatedAt (ISO or empty), ARGV[5] = statusIdle,
-- ARGV[6] = statusQueued, ARGV[7] = fresh acquisition token, ARGV[8] = leaseUntil or empty,
-- ARGV[9] = time-ticker key prefix (empty for cron occurrences)
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
obj['AcquisitionToken'] = ARGV[7]
obj['acquisitionToken'] = nil
local rootId = obj['Id'] or obj['id']
if ARGV[9] ~= '' and rootId and rootId ~= cjson.null then
    obj['ChainRootId'] = tostring(rootId); obj['chainRootId'] = nil
    obj['ChainGeneration'] = ARGV[7]; obj['chainGeneration'] = nil
end
if ARGV[8] ~= '' then
    obj['LeaseUntil'] = ARGV[8]
    obj['leaseUntil'] = nil
end
local function fenceChildren(children)
    if type(children) ~= 'table' then return end
    for _, child in pairs(children) do
        child['LockHolder'] = ARGV[1]; child['lockHolder'] = nil
        child['LockedAt'] = ARGV[2]; child['lockedAt'] = nil
        child['AcquisitionToken'] = ARGV[7]; child['acquisitionToken'] = nil
        if ARGV[9] ~= '' and rootId and rootId ~= cjson.null then
            child['ChainRootId'] = tostring(rootId); child['chainRootId'] = nil
            child['ChainGeneration'] = ARGV[7]; child['chainGeneration'] = nil
        end
        local id = child['Id'] or child['id']
        if ARGV[9] ~= '' and id and id ~= cjson.null then
            redis.call('DEL', ARGV[9] .. tostring(id) .. ':result')
        end
        fenceChildren(child['Children'] or child['children'])
    end
end
fenceChildren(obj['Children'] or obj['children'])
local updated = cjson.encode(obj)
-- cjson encodes an empty Lua table as '{}', turning empty JSON arrays ('[]') into objects.
-- Restore the array-typed fields so C# deserialization does not fail on re-encode.
updated = updated:gsub('"Children":{}', '"Children":[]')
updated = updated:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
redis.call('SET', KEYS[1], updated)
redis.call('DEL', KEYS[2])
return updated
