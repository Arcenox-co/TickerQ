-- Metadata-only aggregate update, fenced against concurrent document writes and runtime activation.
-- KEYS: document, activation metadata. ARGV: expected JSON, replacement JSON, epoch, admission mode.
if #KEYS ~= 2 or #ARGV ~= 4 or ARGV[1] == '' or ARGV[2] == '' or
   (ARGV[4] ~= 'legacy' and ARGV[4] ~= 'scoped' and ARGV[4] ~= 'startup-seeder') then
    return redis.error_reply('invalid chain deletion policy arguments')
end
local activationType = redis.call('TYPE', KEYS[2])['ok']
if activationType ~= 'none' and activationType ~= 'hash' then
    return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
end
if ARGV[4] == 'legacy' and redis.call('HGET', KEYS[2], 'legacyAdoptionState') then return 0 end
if ARGV[4] ~= 'legacy' then
    if activationType == 'none' then return 0 end
    local activation = redis.call('HGETALL', KEYS[2])
    if #activation ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
    local fields = {}
    for i = 1, #activation, 2 do fields[activation[i]] = activation[i + 1] end
    if not fields['epoch'] or not fields['phase'] or fields['checkpoint'] == nil or
       not string.match(fields['epoch'], '^[0-9]+$') or #fields['epoch'] > 19 or
       #fields['checkpoint'] > 200 or
       (fields['phase'] ~= '0' and fields['phase'] ~= '1' and fields['phase'] ~= '2') then
        return redis.error_reply('reconciliation activation metadata is corrupt')
    end
    local allowedPhase = fields['phase'] == '2' or (ARGV[4] == 'startup-seeder' and fields['phase'] == '1')
    if not allowedPhase or fields['epoch'] ~= ARGV[3] then return 0 end
end
if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end
redis.call('SET', KEYS[1], ARGV[2])
return 1
