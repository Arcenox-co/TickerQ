-- Configuration aggregate CAS preserves lifecycle, results, and terminal evidence.
-- KEYS: document, activation metadata, pending, ids, four terminal retention indexes.
-- ARGV: expected JSON, replacement JSON, epoch, admission mode, id, pending score,
--       retention index (0 disables), retention score.
if #KEYS ~= 8 or #ARGV ~= 8 or ARGV[1] == '' or ARGV[2] == '' or ARGV[5] == '' or
   (ARGV[4] ~= 'legacy' and ARGV[4] ~= 'scoped' and ARGV[4] ~= 'startup-seeder') then
    return redis.error_reply('invalid configuration update arguments')
end
-- Redis scripts do not roll back errors: preflight every key before changing the document.
local expectedTypes = { 'string', 'hash', 'zset', 'set', 'zset', 'zset', 'zset', 'zset' }
for i = 1, #KEYS do
    local keyType = redis.call('TYPE', KEYS[i])['ok']
    if keyType ~= 'none' and keyType ~= expectedTypes[i] then
        return redis.error_reply('invalid configuration update key type')
    end
end
if ARGV[6] ~= '' and tonumber(ARGV[6]) == nil then return redis.error_reply('invalid pending score') end
local retentionIndex = tonumber(ARGV[7])
if not retentionIndex or retentionIndex < 0 or retentionIndex > 4 or retentionIndex % 1 ~= 0 or
   (retentionIndex > 0 and tonumber(ARGV[8]) == nil) then
    return redis.error_reply('invalid retention score')
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
redis.call('SADD', KEYS[4], ARGV[5])
if ARGV[6] == '' then redis.call('ZREM', KEYS[3], ARGV[5])
else redis.call('ZADD', KEYS[3], ARGV[6], ARGV[5]) end
for i = 5, 8 do redis.call('ZREM', KEYS[i], ARGV[5]) end
if retentionIndex > 0 then redis.call('ZADD', KEYS[4 + retentionIndex], ARGV[8], ARGV[5]) end
return 1
