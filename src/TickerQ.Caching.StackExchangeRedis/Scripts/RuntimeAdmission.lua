-- Shared by the scripts that publish a scheduler-visible time ticker: whether runtime admission
-- allows publishing now. Defines functions only; the including script runs.
local function isAdmissionMode(mode)
    return mode == 'scoped' or mode == 'startup-seeder' or mode == 'legacy'
end

-- Returns true or false, or an error reply the including script must return as is.
local function runtimeAdmits(activationKey, epoch, mode)
    local activationType = redis.call('TYPE', activationKey)['ok']
    if activationType ~= 'none' and activationType ~= 'hash' then
        return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
    end
    if mode == 'legacy' then return not redis.call('HGET', activationKey, 'legacyAdoptionState') end
    if activationType == 'none' then return false end
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
    local allowedPhase = fields['phase'] == '2' or (mode == 'startup-seeder' and fields['phase'] == '1')
    return allowedPhase and fields['epoch'] == epoch
end
