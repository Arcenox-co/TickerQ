-- Bounded, resumable repair of cron definition discoverability.
-- KEYS: ids, phase, cursor, pending, quarantine, activation metadata
-- ARGV: max records, document prefix
if #KEYS ~= 6 or #ARGV ~= 2 then
    return redis.error_reply('invalid cron discoverability repair shape')
end
local maxRecords = tonumber(ARGV[1])
if not maxRecords or maxRecords <= 0 or maxRecords % 1 ~= 0 or ARGV[2] == '' then
    return redis.error_reply('invalid cron discoverability repair arguments')
end
local expectedTypes = {'set', 'string', 'string', 'list', 'hash', 'hash'}
for index = 1, #KEYS do
    local actualType = redis.call('TYPE', KEYS[index])['ok']
    if actualType ~= 'none' and actualType ~= expectedTypes[index] then
        return redis.error_reply('cron discoverability repair key has an incompatible Redis type')
    end
end
if redis.call('HGET', KEYS[6], 'legacyAdoptionState') then
    return {0, 0, 0, 'fenced'}
end

local phase = redis.call('GET', KEYS[2]) or 'ids'
if phase ~= 'ids' and phase ~= 'keys' then
    return redis.error_reply('invalid cron discoverability repair phase')
end
local cursor = redis.call('GET', KEYS[3]) or '0'
if not string.match(cursor, '^%d+$') then
    return redis.error_reply('invalid cron discoverability repair cursor')
end

-- Discovery is read-only. Do not consume pending work or enqueue scan overflow until every
-- fixed and dynamically discovered key has been checked and every mutation has been planned.
local members = {}
local overflow = {}
local nextCursor = cursor
local fromPending = false
local queued = redis.call('LRANGE', KEYS[4], 0, maxRecords - 1)
for _, member in ipairs(queued) do table.insert(members, member) end
fromPending = #members > 0
if #members == 0 then
    local scan
    if phase == 'keys' then
        scan = redis.call('SCAN', cursor, 'MATCH', ARGV[2] .. '????????-????-????-????-????????????',
            'COUNT', maxRecords)
    else
        scan = redis.call('SSCAN', KEYS[1], cursor, 'COUNT', maxRecords)
    end
    nextCursor = scan[1]
    for index, value in ipairs(scan[2]) do
        local id = phase == 'keys' and string.sub(value, string.len(ARGV[2]) + 1) or value
        if index <= maxRecords then table.insert(members, id)
        else table.insert(overflow, id) end
    end
end

local plans = {}
local corrupt = 0
for index, id in ipairs(members) do
    if id == '' then return redis.error_reply('invalid cron discoverability member') end
    local documentKey = ARGV[2] .. id
    local documentType = redis.call('TYPE', documentKey)['ok']
    if documentType ~= 'none' and documentType ~= 'string' then
        return redis.error_reply('cron discoverability document has an incompatible Redis type')
    end
    local raw = redis.call('GET', documentKey)
    local plan = {id = id, action = 'missing', raw = false}
    if raw then
        local ok, obj = pcall(cjson.decode, raw)
        local valid = ok and type(obj) == 'table' and type(obj.Id) == 'string'
            and string.lower(obj.Id) == string.lower(id)
        if valid then
            plan.action = 'valid'
        else
            plan.action = 'corrupt'
            plan.raw = raw
            corrupt = corrupt + 1
        end
    end
    plans[index] = plan
end

-- Mutation-only pass: all commands below are type-safe because fixed and dynamic keys were
-- preflighted above, and all values/scores were fully materialized in the plan.
if fromPending then redis.call('LTRIM', KEYS[4], #members, -1) end
for _, id in ipairs(overflow) do redis.call('RPUSH', KEYS[4], id) end
for _, plan in ipairs(plans) do
    if plan.action == 'valid' then
        redis.call('SADD', KEYS[1], plan.id)
        redis.call('HDEL', KEYS[5], plan.id)
    elseif plan.action == 'corrupt' then
        redis.call('HSET', KEYS[5], plan.id, plan.raw)
        redis.call('SREM', KEYS[1], plan.id)
    else
        redis.call('SREM', KEYS[1], plan.id)
    end
end

local completed = 0
if nextCursor == '0' and redis.call('LLEN', KEYS[4]) == 0 then
    if phase == 'ids' then phase = 'keys'
    else phase = 'ids'; completed = 1 end
end
redis.call('SET', KEYS[2], phase)
redis.call('SET', KEYS[3], nextCursor)
return {#members, corrupt, completed == 0 and 1 or 0, phase .. ':' .. nextCursor}
