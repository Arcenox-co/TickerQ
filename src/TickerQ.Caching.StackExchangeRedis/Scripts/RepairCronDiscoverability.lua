-- Bounded, resumable repair of cron definition discoverability.
-- KEYS: ids, phase, cursor, pending, quarantine
-- ARGV: max records, document prefix
local phase = redis.call('GET', KEYS[2]) or 'ids'
local cursor = redis.call('GET', KEYS[3]) or '0'
local maxRecords = tonumber(ARGV[1])
local members = {}
local nextCursor = cursor

while #members < maxRecords and redis.call('LLEN', KEYS[4]) > 0 do
    table.insert(members, redis.call('LPOP', KEYS[4]))
end
if #members == 0 then
    local scan
    if phase == 'keys' then
        scan = redis.call('SCAN', cursor, 'MATCH', ARGV[2] .. '????????-????-????-????-????????????', 'COUNT', maxRecords)
    else
        scan = redis.call('SSCAN', KEYS[1], cursor, 'COUNT', maxRecords)
    end
    nextCursor = scan[1]
    for index, value in ipairs(scan[2]) do
        local id = phase == 'keys' and string.sub(value, string.len(ARGV[2]) + 1) or value
        if index <= maxRecords then table.insert(members, id)
        else redis.call('RPUSH', KEYS[4], id) end
    end
end

local corrupt = 0
for _, id in ipairs(members) do
    local raw = redis.call('GET', ARGV[2] .. id)
    if not raw then
        redis.call('SREM', KEYS[1], id)
    else
        local ok, obj = pcall(cjson.decode, raw)
        local valid = ok and type(obj) == 'table' and obj.Id ~= nil
            and string.lower(tostring(obj.Id)) == string.lower(id)
        if not valid then
            redis.call('HSET', KEYS[5], id, raw)
            redis.call('SREM', KEYS[1], id)
            corrupt = corrupt + 1
        else
            redis.call('SADD', KEYS[1], id)
            redis.call('HDEL', KEYS[5], id)
        end
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
