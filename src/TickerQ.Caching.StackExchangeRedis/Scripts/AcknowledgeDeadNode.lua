-- KEYS: atomic registry set, heartbeat hash, legacy IDistributedCache registry hash.
-- The legacy hash's data field is Microsoft RedisCache's JSON payload. Preserve
-- its expiry/metadata and other owners. Only this compatibility path knows that format.
-- All keys share the runtime partition hash tag, including with an InstanceName prefix.
if redis.call('EXISTS', KEYS[2]) == 1 then return 0 end
local legacy = redis.call('HGET', KEYS[3], 'data')
local remaining = {}
if legacy then
    local owners = cjson.decode(legacy)
    for _, owner in ipairs(owners) do
        if owner ~= ARGV[1] then table.insert(remaining, owner) end
    end
end
-- Parse before mutating: a malformed legacy value must not acknowledge recovery.
redis.call('SREM', KEYS[1], ARGV[1])
if legacy then
    if #remaining == 0 then redis.call('DEL', KEYS[3])
    else redis.call('HSET', KEYS[3], 'data', cjson.encode(remaining)) end
end
return 1
