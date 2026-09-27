-- Atomically publishes a TimeTicker document and its scheduler-critical indexes behind runtime admission.
-- KEYS[1] document key, KEYS[2] all-id set, KEYS[3] pending sorted set,
-- KEYS[4] reconciliation activation metadata, KEYS[5] terminal evidence hash,
-- KEYS[6..n] result keys to clear on replacement
-- ARGV[1] id, ARGV[2] JSON, ARGV[3] pending score or empty,
-- ARGV[4] "once" to reject an existing document or "upsert", ARGV[5] exact supported epoch,
-- ARGV[6] scoped | startup-seeder | legacy runtime admission mode,
-- ARGV[7..n] typed terminal-evidence fields to clear on replacement
local activationType = (ARGV[6] == 'scoped' or ARGV[6] == 'startup-seeder') and redis.call('TYPE', KEYS[4])['ok'] or 'none'
if ARGV[6] ~= 'scoped' and ARGV[6] ~= 'startup-seeder' and ARGV[6] ~= 'legacy' then return 0 end
if activationType ~= 'none' and activationType ~= 'hash' then
    return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
end
if (ARGV[6] == 'scoped' or ARGV[6] == 'startup-seeder') and activationType == 'none' then return 0
elseif ARGV[6] == 'scoped' or ARGV[6] == 'startup-seeder' then
    local activation = redis.call('HGETALL', KEYS[4])
    if #activation ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
    local fields = {}
    for i = 1, #activation, 2 do fields[activation[i]] = activation[i + 1] end
    if not fields['epoch'] or not fields['phase'] or fields['checkpoint'] == nil or
       not string.match(fields['epoch'], '^[0-9]+$') or #fields['epoch'] > 19 or
       #fields['checkpoint'] > 200 or
       (fields['phase'] ~= '0' and fields['phase'] ~= '1' and fields['phase'] ~= '2') then
        return redis.error_reply('reconciliation activation metadata is corrupt')
    end
    local allowedPhase = fields['phase'] == '2' or (ARGV[6] == 'startup-seeder' and fields['phase'] == '1')
    if not allowedPhase or fields['epoch'] ~= ARGV[5] then return 0 end
end
if ARGV[4] == 'once' and redis.call('EXISTS', KEYS[1]) == 1 then return 0 end
for i = 7, #ARGV do redis.call('HDEL', KEYS[5], ARGV[i]) end
for i = 6, #KEYS do redis.call('DEL', KEYS[i]) end
redis.call('SET', KEYS[1], ARGV[2])
redis.call('SADD', KEYS[2], ARGV[1])
if ARGV[3] ~= '' then
    redis.call('ZADD', KEYS[3], ARGV[3], ARGV[1])
else
    redis.call('ZREM', KEYS[3], ARGV[1])
end
return 1
