-- The single mutation boundary for durable reconciliation activation metadata.
-- KEYS[1] = dedicated activation metadata hash
-- ARGV[1] = begin | checkpoint | commit
-- ARGV[2] = canonical positive Int64 epoch
-- ARGV[3] = checkpoint (only for checkpoint; empty otherwise)
-- Returns { epoch, phase, checkpoint }, all as strings.
-- IMPORTANT: every key, argument, and existing field is validated before the first write.
if #KEYS ~= 1 or #ARGV ~= 3 then
    return redis.error_reply('invalid activation mutation argument count')
end

local keyType = redis.call('TYPE', KEYS[1])['ok']
if keyType ~= 'none' and keyType ~= 'hash' then
    return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
end

local operation, target, requestedCheckpoint = ARGV[1], ARGV[2], ARGV[3]
local function validEpoch(value, allowZero)
    if value == '0' then return allowZero end
    if not string.match(value, '^[1-9][0-9]*$') or #value > 19 then return false end
    return #value < 19 or value <= '9223372036854775807'
end
if operation ~= 'begin' and operation ~= 'checkpoint' and operation ~= 'commit' then
    return redis.error_reply('invalid activation mutation operation')
end
if not validEpoch(target, false) then
    return redis.error_reply('invalid activation target epoch')
end
if operation == 'checkpoint' then
    if requestedCheckpoint == '' or #requestedCheckpoint > 200 then
        return redis.error_reply('invalid activation checkpoint')
    end
elseif requestedCheckpoint ~= '' then
    return redis.error_reply('unexpected activation checkpoint')
end

local epoch, phase, checkpoint = '0', '0', ''
if keyType == 'hash' then
    local values = redis.call('HGETALL', KEYS[1])
    if #values ~= 6 then
        return redis.error_reply('reconciliation activation metadata is corrupt')
    end
    local fields = {}
    for i = 1, #values, 2 do fields[values[i]] = values[i + 1] end
    epoch, phase, checkpoint = fields['epoch'], fields['phase'], fields['checkpoint']
    if not epoch or not phase or checkpoint == nil or
       not validEpoch(epoch, true) or
       (phase ~= '0' and phase ~= '1' and phase ~= '2') or #checkpoint > 200 then
        return redis.error_reply('reconciliation activation metadata is corrupt')
    end
end

local function compareEpoch(left, right)
    if #left < #right then return -1 end
    if #left > #right then return 1 end
    if left < right then return -1 end
    if left > right then return 1 end
    return 0
end

local relation = compareEpoch(target, epoch)
local shouldWrite = false
if operation == 'begin' then
    if relation > 0 then
        epoch, phase, checkpoint, shouldWrite = target, '1', '', true
    elseif relation == 0 and phase == '0' then
        phase, shouldWrite = '1', true
    end
elseif operation == 'checkpoint' then
    if relation == 0 and phase == '1' and checkpoint ~= requestedCheckpoint then
        local function rank(value)
            local prefix = string.match(value, '^(%d%d)%-')
            return prefix and tonumber(prefix) or 0
        end
        local currentRank, requestedRank = rank(checkpoint), rank(requestedCheckpoint)
        if checkpoint == '' or currentRank == 0 or requestedRank > currentRank then
            checkpoint, shouldWrite = requestedCheckpoint, true
        end
    end
elseif relation == 0 and phase == '1' then
    phase, shouldWrite = '2', true
end

if shouldWrite then
    redis.call('HSET', KEYS[1], 'epoch', epoch, 'phase', phase, 'checkpoint', checkpoint)
end
return {epoch, phase, checkpoint}
