-- The single mutation boundary for durable reconciliation activation metadata.
-- KEYS[1] = dedicated activation metadata hash (exactly epoch, phase, checkpoint; other scripts rely on that)
-- KEYS[2] = startup manifest hash for the same scope (current, history)
-- ARGV[1] = begin | checkpoint | commit | read
-- ARGV[2] = canonical positive Int64 epoch (ignored for read)
-- ARGV[3] = checkpoint (only for checkpoint; empty otherwise)
-- ARGV[4] = startup manifest fingerprint recorded by a writing begin (empty = none)
-- ARGV[5] = maximum number of earlier fingerprints kept
-- Returns { epoch, phase, checkpoint, manifest, history }, all as strings.
-- IMPORTANT: every key, argument, and existing field is validated before the first write.
if #KEYS ~= 2 or #ARGV ~= 5 then
    return redis.error_reply('invalid activation mutation argument count')
end

local keyType = redis.call('TYPE', KEYS[1])['ok']
if keyType ~= 'none' and keyType ~= 'hash' then
    return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
end

local manifestType = redis.call('TYPE', KEYS[2])['ok']
if manifestType ~= 'none' and manifestType ~= 'hash' then
    return redis.error_reply('reconciliation activation manifest key has an incompatible Redis type')
end

local operation, target, requestedCheckpoint, requestedManifest = ARGV[1], ARGV[2], ARGV[3], ARGV[4]
local maxHistory = tonumber(ARGV[5])
local function validEpoch(value, allowZero)
    if value == '0' then return allowZero end
    if not string.match(value, '^[1-9][0-9]*$') or #value > 19 then return false end
    return #value < 19 or value <= '9223372036854775807'
end
if operation ~= 'begin' and operation ~= 'checkpoint' and operation ~= 'commit' and operation ~= 'read' then
    return redis.error_reply('invalid activation mutation operation')
end
if not maxHistory or maxHistory < 0 then
    return redis.error_reply('invalid activation manifest history bound')
end
if #requestedManifest > 48 or string.find(requestedManifest, ',', 1, true) or
   (requestedManifest ~= '' and operation ~= 'begin') then
    return redis.error_reply('invalid activation manifest')
end
if operation ~= 'read' and not validEpoch(target, false) then
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

local manifest, history = '', ''
if manifestType == 'hash' then
    manifest = redis.call('HGET', KEYS[2], 'current') or ''
    history = redis.call('HGET', KEYS[2], 'history') or ''
end
if operation == 'read' then
    return {epoch, phase, checkpoint, manifest, history}
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
local claimed = false
if operation == 'begin' then
    if relation > 0 then
        epoch, phase, checkpoint, shouldWrite, claimed = target, '1', '', true, true
    elseif relation == 0 and phase == '0' then
        phase, shouldWrite, claimed = '1', true, true
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

if claimed then
    -- Same transition as ActivationManifests.Advance: the previous fingerprint moves to the front,
    -- the new one leaves the history, duplicates collapse, and the list stays bounded.
    local nextHistory, seen = {}, {}
    local function keep(value)
        if value ~= '' and value ~= requestedManifest and not seen[value] and #nextHistory < maxHistory then
            seen[value] = true
            table.insert(nextHistory, value)
        end
    end
    keep(manifest)
    for entry in string.gmatch(history, '[^,]+') do keep(entry) end
    manifest, history = requestedManifest, table.concat(nextHistory, ',')
    redis.call('HSET', KEYS[2], 'current', manifest, 'history', history)
end
if shouldWrite then
    redis.call('HSET', KEYS[1], 'epoch', epoch, 'phase', phase, 'checkpoint', checkpoint)
end
return {epoch, phase, checkpoint, manifest, history}
