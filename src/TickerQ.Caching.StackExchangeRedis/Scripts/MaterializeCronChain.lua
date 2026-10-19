-- All scheduler-visible publication and the trigger receipt commit in one standalone Redis operation.
local types = {'string','string','string','set','zset','zset','hash','set','zset','zset'}
for i = 1, #types do
    local actual = redis.call('TYPE', KEYS[i])['ok']
    if actual ~= 'none' and actual ~= types[i] then return redis.error_reply('invalid recurring chain key type') end
end
if redis.call('GET', KEYS[1]) ~= ARGV[1] or redis.call('GET', KEYS[2]) ~= ARGV[2] then return 4 end
if ARGV[10] ~= 'legacy' and ARGV[10] ~= 'scoped' and ARGV[10] ~= 'startup-seeder' then
    return redis.error_reply('invalid runtime admission mode')
end
local activationType = redis.call('TYPE', KEYS[7])['ok']
if activationType ~= 'none' and activationType ~= 'hash' then
    return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
end
if ARGV[10] == 'legacy' and redis.call('HGET', KEYS[7], 'legacyAdoptionState') then return 4 end
if (ARGV[10] == 'scoped' or ARGV[10] == 'startup-seeder') and activationType == 'none' then return 4
elseif ARGV[10] == 'scoped' or ARGV[10] == 'startup-seeder' then
    local activation = redis.call('HGETALL', KEYS[7])
    if #activation ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
    local fields = {}
    for i = 1, #activation, 2 do fields[activation[i]] = activation[i + 1] end
    if not fields['epoch'] or not fields['phase'] or fields['checkpoint'] == nil or
       not string.match(fields['epoch'], '^[0-9]+$') or #fields['epoch'] > 19 or
       #fields['checkpoint'] > 200 or
       (fields['phase'] ~= '0' and fields['phase'] ~= '1' and fields['phase'] ~= '2') then
        return redis.error_reply('reconciliation activation metadata is corrupt')
    end
    local allowedPhase = fields['phase'] == '2' or (ARGV[10] == 'startup-seeder' and fields['phase'] == '1')
    if not allowedPhase or fields['epoch'] ~= ARGV[9] then return 4 end
end
local root = cjson.decode(ARGV[3])
local completed = cjson.decode(ARGV[4])
local skipped = cjson.decode(ARGV[5])
local score = tonumber(ARGV[7])
if not score then return redis.error_reply('invalid recurring chain score') end
if redis.call('EXISTS', KEYS[3]) == 1 then return 4 end
local function unfinished(node)
    local status = node.Status or node.status
    if status == 0 or status == 1 or status == 2 or status == 'Idle' or status == 'Queued' or status == 'InProgress' then return true end
    local children = node.Children or node.children
    if type(children) == 'table' then
        for _, child in ipairs(children) do if unfinished(child) then return true end end
    end
    return false
end
local active = false
local retired = {}
for _, key in ipairs(redis.call('SMEMBERS', KEYS[8])) do
    local json = redis.call('GET', key)
    if json and unfinished(cjson.decode(json)) then active = true else table.insert(retired, key) end
end
-- Finish every read/validation before the first write; Lua errors do not roll back earlier writes.
for _, key in ipairs(retired) do redis.call('SREM', KEYS[8], key) end
if ARGV[8] == '1' and active then
    redis.call('SET', KEYS[1], ARGV[5])
    redis.call('ZREM', KEYS[6], ARGV[6])
    redis.call('ZADD', KEYS[10], score, ARGV[6])
    return 3
end
redis.call('SET', KEYS[3], ARGV[3])
redis.call('SADD', KEYS[4], ARGV[6])
redis.call('ZADD', KEYS[5], score, ARGV[6])
redis.call('SADD', KEYS[8], KEYS[3])
redis.call('SET', KEYS[1], ARGV[4])
redis.call('ZREM', KEYS[6], ARGV[6])
redis.call('ZADD', KEYS[9], score, ARGV[6])
return 1
