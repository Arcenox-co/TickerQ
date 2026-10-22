-- Atomically acquires an existing embedded time-ticker chain for an in-place retry.
-- KEYS: aggregate, activation metadata, pending, ids, four retention indexes,
--       terminal evidence hash, then one result key per reset execution.
-- ARGV: expected aggregate JSON, replacement JSON, epoch, admission mode, root id,
--       reset count, then one terminal-evidence field per reset execution.
if #KEYS < 9 or #ARGV < 6 or ARGV[1] == '' or ARGV[2] == '' or ARGV[5] == '' then
    return redis.error_reply('invalid chain retry arguments')
end
local resetCount = tonumber(ARGV[6])
if not resetCount or resetCount < 0 or resetCount % 1 ~= 0 or
   #KEYS ~= 9 + resetCount or #ARGV ~= 6 + resetCount then
    return redis.error_reply('invalid chain retry reset shape')
end
local expectedTypes = { 'string', 'hash', 'zset', 'set', 'zset', 'zset', 'zset', 'zset', 'hash' }
for i = 1, 9 do
    local keyType = redis.call('TYPE', KEYS[i])['ok']
    if keyType ~= 'none' and keyType ~= expectedTypes[i] then
        return redis.error_reply('invalid chain retry key type')
    end
end
for i = 10, #KEYS do
    local keyType = redis.call('TYPE', KEYS[i])['ok']
    if keyType ~= 'none' and keyType ~= 'string' then
        return redis.error_reply('invalid chain retry result key type')
    end
end

local activationType = redis.call('TYPE', KEYS[2])['ok']
if ARGV[4] == 'legacy' then
    if redis.call('HGET', KEYS[2], 'legacyAdoptionState') then return 0 end
elseif ARGV[4] == 'scoped' or ARGV[4] == 'startup-seeder' then
    if activationType == 'none' then return 0 end
    local activation = redis.call('HGETALL', KEYS[2])
    if #activation ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
    local fields = {}
    for i = 1, #activation, 2 do fields[activation[i]] = activation[i + 1] end
    local allowedPhase = fields['phase'] == '2' or
        (ARGV[4] == 'startup-seeder' and fields['phase'] == '1')
    if not allowedPhase or fields['epoch'] ~= ARGV[3] then return 0 end
else
    return redis.error_reply('invalid chain retry admission mode')
end

if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end
redis.call('SET', KEYS[1], ARGV[2])
redis.call('SADD', KEYS[4], ARGV[5])
redis.call('ZREM', KEYS[3], ARGV[5])
for i = 5, 8 do redis.call('ZREM', KEYS[i], ARGV[5]) end
for i = 1, resetCount do
    redis.call('HDEL', KEYS[9], ARGV[6 + i])
    redis.call('DEL', KEYS[9 + i])
end
return 1
