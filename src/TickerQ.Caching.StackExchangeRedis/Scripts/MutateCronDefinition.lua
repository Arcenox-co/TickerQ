-- Atomically publish/remove one Cron definition and, when requested, quarantine every
-- safely-unleased pending occurrence from the previous revision before publication.
-- No acquisition can interleave with this script on standalone Redis.
-- KEYS: definition,definition ids,definition quarantine,byCron,all occurrence ids,pending,
--       occurrence retention x4,occurrence quarantine,activation metadata
-- ARGV: cron id,operation,replacement json,reconcile(0/1),now,idle,queued,skipped,reason,
--       retention score,occurrence prefix,slot prefix,exact supported epoch,allow exact Activating (0/1),
--       compare-owner (0/1),expected owner namespace,expected seed key,expected definition revision (-1 skips),
--       scoped | startup-seeder | legacy | invalid runtime admission mode
if #KEYS ~= 12 or #ARGV ~= 19 then return redis.error_reply('invalid cron mutation argument count') end
local function type_ok(key, expected)
    local t = redis.call('TYPE', key)['ok']
    return t == 'none' or t == expected
end
local expected = {'string','set','hash','set','set','zset','zset','zset','zset','zset','hash','hash'}
for i = 1, #KEYS do
    if not type_ok(KEYS[i], expected[i]) then
        return redis.error_reply('cron mutation key has an incompatible Redis type')
    end
end
if ARGV[1] == '' or (ARGV[2] ~= 'upsert' and ARGV[2] ~= 'delete' and ARGV[2] ~= 'markDeleting') or
   (ARGV[4] ~= '0' and ARGV[4] ~= '1') or (ARGV[14] ~= '0' and ARGV[14] ~= '1') or
   (ARGV[15] ~= '0' and ARGV[15] ~= '1') or
   (ARGV[19] ~= 'scoped' and ARGV[19] ~= 'startup-seeder' and ARGV[19] ~= 'legacy' and ARGV[19] ~= 'invalid') then
    return redis.error_reply('invalid cron mutation argument')
end
if ARGV[19] == 'invalid' then return -3 end
if ARGV[19] ~= 'legacy' and redis.call('EXISTS', KEYS[12]) == 0 then
    return -3
elseif ARGV[19] ~= 'legacy' then
    local metadata = redis.call('HGETALL', KEYS[12])
    if #metadata ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
    local fields = {}; for i = 1, #metadata, 2 do fields[metadata[i]] = metadata[i + 1] end
    local phase = fields['phase']
    if not fields['epoch'] or not phase or fields['checkpoint'] == nil or not string.match(fields['epoch'], '^[0-9]+$')
       or #fields['epoch'] > 19 or #fields['checkpoint'] > 200 or (phase ~= '0' and phase ~= '1' and phase ~= '2') then
        return redis.error_reply('reconciliation activation metadata is corrupt')
    end
    local allowActivating = ARGV[19] == 'startup-seeder' and ARGV[14] == '1'
    if fields['epoch'] ~= ARGV[13] or (phase ~= '2' and not (phase == '1' and allowActivating)) then return -3 end
end
local current = redis.call('GET', KEYS[1])
local currentObject = nil
if current then
    local currentOk
    currentOk, currentObject = pcall(cjson.decode, current)
    if not currentOk then
        redis.call('HSET', KEYS[3], ARGV[1], current)
        redis.call('SREM', KEYS[2], ARGV[1])
        return -1
    end
end
local expectedRevision = tonumber(ARGV[18])
if not expectedRevision then return redis.error_reply('invalid expected definition revision') end
if expectedRevision >= 0 then
    if not currentObject then
        if expectedRevision ~= 0 then return -5 end
    else
        local currentRevision = tonumber(currentObject['DefinitionRevision'] or currentObject['definitionRevision'] or 0)
        if not currentRevision or currentRevision ~= expectedRevision then return -5 end
    end
end
if ARGV[15] == '1' then
    if not currentObject then return -4 end
    local function normalized(value)
        if value == nil or value == cjson.null then return '' end
        return tostring(value)
    end
    local currentOwner = normalized(currentObject['SeedOwnerNamespace'] or currentObject['seedOwnerNamespace'])
    local currentSeedKey = normalized(currentObject['SeedKey'] or currentObject['seedKey'])
    if currentOwner ~= ARGV[16] or currentSeedKey ~= ARGV[17] then return -4 end
end
if ARGV[2] == 'markDeleting' then
    if not current then return 0 end
    currentObject.TickerQLifecycle = 'Deleting'
    local deleting = cjson.encode(currentObject)
    deleting = deleting:gsub('"Request":{}', '"Request":[]')
    deleting = deleting:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
    deleting = deleting:gsub('"Children":{}', '"Children":[]')
    redis.call('SET', KEYS[1], deleting)
    return 1
end
if ARGV[2] == 'upsert' then
    local replacementOk, replacement = pcall(cjson.decode, ARGV[3])
    if not replacementOk or type(replacement) ~= 'table' then
        return redis.error_reply('replacement cron JSON is invalid')
    end
    if string.lower(tostring(replacement['Id'] or replacement['id'] or '')) ~= string.lower(ARGV[1]) then
        return redis.error_reply('replacement cron identity does not match argument')
    end
end

local function dateTimeTicks(value)
    local year, month, day, hour, minute, second, fraction = string.match(
        tostring(value or ''), '^(%d%d%d%d)%-(%d%d)%-(%d%d)T(%d%d):(%d%d):(%d%d)%.?(%d*)')
    if not year then return nil end
    year, month, day = tonumber(year), tonumber(month), tonumber(day)
    hour, minute, second = tonumber(hour), tonumber(minute), tonumber(second)
    local priorYear = year - 1
    local days = priorYear * 365 + math.floor(priorYear / 4)
        - math.floor(priorYear / 100) + math.floor(priorYear / 400)
    local beforeMonth = {0,31,59,90,120,151,181,212,243,273,304,334}
    if not beforeMonth[month] then return nil end
    days = days + beforeMonth[month] + day - 1
    if month > 2 and (year % 400 == 0 or (year % 4 == 0 and year % 100 ~= 0)) then days = days + 1 end
    fraction = string.sub((fraction or '') .. '0000000', 1, 7)
    return string.format('%.0f', days * 864000000000 + hour * 36000000000
        + minute * 600000000 + second * 10000000 + tonumber(fraction))
end

local candidates = {}
if ARGV[4] == '1' then
    local idle, queued, skipped, retentionScore = tonumber(ARGV[6]), tonumber(ARGV[7]), tonumber(ARGV[8]), tonumber(ARGV[10])
    if not idle or not queued or not skipped or not retentionScore or ARGV[5] == '' or
       ARGV[9] == '' or ARGV[11] == '' or ARGV[12] == '' then
        return redis.error_reply('invalid cron quarantine argument')
    end
    local ids = redis.call('SMEMBERS', KEYS[4])
    -- Validate the full set first. A corrupt row is preserved/quarantined and prevents revision
    -- publication, so no valid old-revision row is partially quarantined under the old definition.
    for _, id in ipairs(ids) do
        local documentKey = ARGV[11] .. id
        if not type_ok(documentKey, 'string') then
            return redis.error_reply('occurrence document has an incompatible Redis type')
        end
        local raw = redis.call('GET', documentKey)
        if raw then
            local ok, obj = pcall(cjson.decode, raw)
            if not ok or type(obj) ~= 'table' then
                redis.call('HSET', KEYS[11], id, raw)
                redis.call('SREM', KEYS[5], id)
                redis.call('ZREM', KEYS[6], id)
                redis.call('SREM', KEYS[4], id)
                for i = 7, 10 do redis.call('ZREM', KEYS[i], id) end
                return -2
            end
            if string.lower(tostring(obj.CronTickerId or obj.cronTickerId or '')) == string.lower(ARGV[1]) then
                table.insert(candidates, {id=id, key=documentKey, raw=raw, obj=obj})
            end
        else
            redis.call('SREM', KEYS[5], id)
            redis.call('ZREM', KEYS[6], id)
            redis.call('SREM', KEYS[4], id)
            for i = 7, 10 do redis.call('ZREM', KEYS[i], id) end
        end
    end
    for _, candidate in ipairs(candidates) do
        local obj, id = candidate.obj, candidate.id
        local status = tonumber(obj.Status or obj.status)
        local holder = obj.LockHolder or obj.lockHolder
        local token = obj.AcquisitionToken or obj.acquisitionToken
        local lease = obj.LeaseUntil or obj.leaseUntil
        local holderEmpty = holder == nil or holder == cjson.null or holder == ''
        local tokenEmpty = token == nil or token == cjson.null or token == ''
        local leaseExpired = lease == nil or lease == cjson.null or lease == '' or tostring(lease) <= ARGV[5]
        if (status == idle or status == queued) and holderEmpty and tokenEmpty and leaseExpired then
            obj.Status = skipped; obj.status = nil
            obj.SkippedReason = ARGV[9]; obj.skippedReason = nil
            if obj.ExecutedAt == nil or obj.ExecutedAt == cjson.null or obj.ExecutedAt == '' then obj.ExecutedAt = ARGV[5] end
            obj.executedAt = nil
            obj.UpdatedAt = ARGV[5]; obj.updatedAt = nil
            obj.LockHolder = cjson.null; obj.lockHolder = nil
            obj.LockedAt = cjson.null; obj.lockedAt = nil
            obj.LeaseUntil = cjson.null; obj.leaseUntil = nil
            obj.AcquisitionToken = cjson.null; obj.acquisitionToken = nil
            local updated = cjson.encode(obj)
            updated = updated:gsub('"Children":{}', '"Children":[]')
            updated = updated:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
            redis.call('SET', candidate.key, updated)
            redis.call('ZREM', KEYS[6], id)
            for i = 7, 9 do redis.call('ZREM', KEYS[i], id) end
            redis.call('ZADD', KEYS[10], retentionScore, id)
            redis.call('HDEL', KEYS[11], id)
            local ticks = dateTimeTicks(obj.ExecutionTime or obj.executionTime)
            if ticks then
                local slotKey = ARGV[12] .. ticks
                if redis.call('GET', slotKey) == id then redis.call('DEL', slotKey) end
            end
        end
    end
end

if ARGV[2] == 'delete' then
    if not currentObject or tostring(currentObject.TickerQLifecycle or '') ~= 'Deleting' then return 0 end
    if redis.call('SCARD', KEYS[4]) ~= 0 then return 0 end
    redis.call('DEL', KEYS[1])
    redis.call('SREM', KEYS[2], ARGV[1])
    return current and 1 or 0
end
redis.call('SET', KEYS[1], ARGV[3])
redis.call('SADD', KEYS[2], ARGV[1])
redis.call('HDEL', KEYS[3], ARGV[1])
return 1
