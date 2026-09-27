-- KEYS[1] = ticker key, KEYS[2] = result side key, optional next key = authoritative Cron
-- definition, penultimate key = terminal evidence hash, final key = reconciliation activation metadata
-- ARGV[1] = lockHolder, ARGV[2] = now (ISO), ARGV[3] = targetStatus (int)
-- ARGV[4] = expectedUpdatedAt (ISO or empty), ARGV[5] = statusIdle,
-- ARGV[6] = statusQueued, ARGV[7] = fresh acquisition token, ARGV[8] = leaseUntil or empty,
-- ARGV[9] = time-ticker key prefix (empty for cron occurrences), ARGV[10] = exact supported epoch,
-- ARGV[11] = scoped | legacy | invalid runtime admission mode
-- Returns: updated JSON on success, nil on failure
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local ok, obj = pcall(cjson.decode, json)
if not ok or type(obj) ~= 'table' then return nil end
local activationKey = KEYS[#KEYS]
local activationType = ARGV[11] == 'scoped' and redis.call('TYPE', activationKey)['ok'] or 'none'
if ARGV[11] == 'invalid' then return nil end
if ARGV[11] ~= 'scoped' and ARGV[11] ~= 'legacy' then return redis.error_reply('invalid runtime admission mode') end
if activationType ~= 'none' and activationType ~= 'hash' then
    return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
end
if ARGV[11] == 'scoped' and activationType == 'none' then return nil end
if ARGV[11] == 'scoped' and activationType == 'hash' then
    local activation = redis.call('HGETALL', activationKey)
    if #activation ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
    local fields = {}
    for i = 1, #activation, 2 do fields[activation[i]] = activation[i + 1] end
    if not fields['epoch'] or not fields['phase'] or fields['checkpoint'] == nil or
       not string.match(fields['epoch'], '^[0-9]+$') or #fields['epoch'] > 19 or
       #fields['checkpoint'] > 200 or
       (fields['phase'] ~= '0' and fields['phase'] ~= '1' and fields['phase'] ~= '2') then
        return redis.error_reply('reconciliation activation metadata is corrupt')
    end
    if fields['phase'] ~= '2' or fields['epoch'] ~= ARGV[10] then return nil end
end
if ARGV[9] == '' then
    local definitionJson = redis.call('GET', KEYS[3])
    if not definitionJson then return nil end
    local definitionOk, definition = pcall(cjson.decode, definitionJson)
    if not definitionOk or type(definition) ~= 'table' then return nil end
    local occurrenceRevision = tonumber(obj['DefinitionRevision'] or obj['definitionRevision'] or 0)
    local definitionRevision = tonumber(definition['DefinitionRevision'] or definition['definitionRevision'] or 0)
    if not occurrenceRevision or not definitionRevision or occurrenceRevision ~= definitionRevision then return nil end
end
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
local evidenceKey = KEYS[#KEYS - 1]
local evidencePrefix = ARGV[9] ~= '' and '1:' or '0:'
if rootId and rootId ~= cjson.null then
    redis.call('HDEL', evidenceKey, evidencePrefix .. string.lower(tostring(rootId)))
end
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
            redis.call('HDEL', evidenceKey, evidencePrefix .. string.lower(tostring(id)))
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
