-- Atomically mutate derived indexes only while legacy adoption has not fenced this partition.
-- KEYS[1] is the partition activation metadata key. Remaining KEYS are derived index keys.
-- ARGV[1] is the member, ARGV[2] is the operation count, followed by triples:
-- command (SADD/SREM/ZADD/ZREM), one-based KEYS index, optional sorted-set score.
local activationType = redis.call('TYPE', KEYS[1])['ok']
if activationType ~= 'none' and activationType ~= 'hash' then
    return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
end
if activationType == 'hash' and redis.call('HGET', KEYS[1], 'legacyAdoptionState') then
    return 0
end

local count = tonumber(ARGV[2])
if #ARGV < 2 or not count or count < 0 or count % 1 ~= 0 or
   #ARGV ~= 2 + count * 3 or #KEYS ~= 1 + count then
    return redis.error_reply('invalid derived index mutation shape')
end
for index = 0, count - 1 do
    local offset = 3 + index * 3
    local command = ARGV[offset]
    local keyIndex = tonumber(ARGV[offset + 1])
    if not keyIndex or keyIndex % 1 ~= 0 or keyIndex < 2 or keyIndex > #KEYS then
        return redis.error_reply('invalid derived index key index')
    end
    if command ~= 'SADD' and command ~= 'SREM' and command ~= 'DELSET' and
       command ~= 'ZADD' and command ~= 'ZREM' then
        return redis.error_reply('unsupported derived index mutation')
    end
    local expectedType = (command == 'SADD' or command == 'SREM' or command == 'DELSET') and 'set' or 'zset'
    local actualType = redis.call('TYPE', KEYS[keyIndex])['ok']
    if actualType ~= 'none' and actualType ~= expectedType then
        return redis.error_reply('derived index key has an incompatible Redis type')
    end
    if command == 'ZADD' then
        local score = tonumber(ARGV[offset + 2])
        if not score or score ~= score or score == math.huge or score == -math.huge then
            return redis.error_reply('derived index score is not a finite number')
        end
    end
end

for index = 0, count - 1 do
    local offset = 3 + index * 3
    local command = ARGV[offset]
    local keyIndex = tonumber(ARGV[offset + 1])
    if command == 'SADD' then
        redis.call('SADD', KEYS[keyIndex], ARGV[1])
    elseif command == 'SREM' then
        redis.call('SREM', KEYS[keyIndex], ARGV[1])
    elseif command == 'DELSET' then
        redis.call('DEL', KEYS[keyIndex])
    elseif command == 'ZADD' then
        redis.call('ZADD', KEYS[keyIndex], ARGV[offset + 2], ARGV[1])
    elseif command == 'ZREM' then
        redis.call('ZREM', KEYS[keyIndex], ARGV[1])
    end
end
return 1
