-- KEYS[1] = ticker key, optional next key = authoritative Cron, final key = activation metadata
-- ARGV[1] = lockHolder, ARGV[2] = expected acquisition token,
-- ARGV[3] = now (ISO), ARGV[4] = statusQueued, ARGV[5] = statusInProgress, ARGV[6] = leaseUntil,
-- ARGV[7] = exact supported epoch, ARGV[8] = scoped | legacy | invalid runtime admission mode
-- Returns: updated JSON on success, nil on failure
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local ok, obj = pcall(cjson.decode, json)
if not ok or type(obj) ~= 'table' then return nil end
local activationType = redis.call('TYPE', KEYS[#KEYS])['ok']
if ARGV[8] == 'invalid' then return nil end
if ARGV[8] ~= 'scoped' and ARGV[8] ~= 'legacy' then return redis.error_reply('invalid runtime admission mode') end
if activationType ~= 'none' and activationType ~= 'hash' then
    return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
end
if ARGV[8] == 'legacy' and activationType == 'hash' and
   redis.call('HGET', KEYS[#KEYS], 'legacyAdoptionState') then return nil end
if ARGV[8] == 'scoped' and activationType == 'none' then return nil end
if ARGV[8] == 'scoped' and activationType == 'hash' then
    local metadata = redis.call('HGETALL', KEYS[#KEYS])
    if #metadata ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
    local fields = {}
    for i = 1, #metadata, 2 do fields[metadata[i]] = metadata[i + 1] end
    local phase = fields['phase']
    if not fields['epoch'] or not phase or fields['checkpoint'] == nil or
       not string.match(fields['epoch'], '^[0-9]+$') or #fields['epoch'] > 19 or #fields['checkpoint'] > 200 then
        return redis.error_reply('reconciliation activation metadata is corrupt')
    end
    if phase ~= '0' and phase ~= '1' and phase ~= '2' then return redis.error_reply('reconciliation activation metadata is corrupt') end
    if phase ~= '2' or fields['epoch'] ~= ARGV[7] then return nil end
end
if #KEYS == 3 then
    local definitionJson = redis.call('GET', KEYS[2])
    if not definitionJson then return nil end
    local definitionOk, definition = pcall(cjson.decode, definitionJson)
    if not definitionOk or type(definition) ~= 'table' then return nil end
    if tonumber(obj['DefinitionRevision'] or obj['definitionRevision'] or 0) ~=
       tonumber(definition['DefinitionRevision'] or definition['definitionRevision'] or 0) then return nil end
end
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
