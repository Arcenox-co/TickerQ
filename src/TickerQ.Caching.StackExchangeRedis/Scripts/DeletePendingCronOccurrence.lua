-- Atomically quarantine one unleased pending occurrence while preserving its document,
-- payload, timestamps, result sidecar, global/reverse evidence and terminal evidence.
-- Corrupt JSON is preserved, quarantined, deindexed, and surfaced to the caller.
-- KEYS: document,result,allIds,pending,byCron,retention x4,quarantine,slot
-- ARGV: occurrence id, cron id, now, idle, queued, skipped, reason, retention score
local function type_ok(key, expected)
    local t = redis.call('TYPE', key)['ok']
    return t == 'none' or t == expected
end
if #KEYS ~= 11 or #ARGV ~= 8 then return redis.error_reply('invalid quarantine argument count') end
if not type_ok(KEYS[1], 'string') or not type_ok(KEYS[2], 'string') or
   not type_ok(KEYS[3], 'set') or not type_ok(KEYS[4], 'zset') or
   not type_ok(KEYS[5], 'set') or not type_ok(KEYS[6], 'zset') or
   not type_ok(KEYS[7], 'zset') or not type_ok(KEYS[8], 'zset') or
   not type_ok(KEYS[9], 'zset') or not type_ok(KEYS[10], 'hash') or
   not type_ok(KEYS[11], 'string') then
    return redis.error_reply('TickerQ occurrence quarantine key has an incompatible Redis type')
end
local idle = tonumber(ARGV[4])
local queued = tonumber(ARGV[5])
local skipped = tonumber(ARGV[6])
local retentionScore = tonumber(ARGV[8])
if not idle or not queued or not skipped or not retentionScore or ARGV[1] == '' or
   ARGV[2] == '' or ARGV[3] == '' or ARGV[7] == '' then
    return redis.error_reply('invalid occurrence quarantine argument')
end
local raw = redis.call('GET', KEYS[1])
if not raw then
    redis.call('SREM', KEYS[3], ARGV[1])
    redis.call('ZREM', KEYS[4], ARGV[1])
    redis.call('SREM', KEYS[5], ARGV[1])
    for i = 6, 9 do redis.call('ZREM', KEYS[i], ARGV[1]) end
    if redis.call('GET', KEYS[11]) == ARGV[1] then redis.call('DEL', KEYS[11]) end
    return 0
end
local ok, obj = pcall(cjson.decode, raw)
if not ok or type(obj) ~= 'table' then
    redis.call('HSET', KEYS[10], ARGV[1], raw)
    redis.call('SREM', KEYS[3], ARGV[1])
    redis.call('ZREM', KEYS[4], ARGV[1])
    redis.call('SREM', KEYS[5], ARGV[1])
    for i = 6, 9 do redis.call('ZREM', KEYS[i], ARGV[1]) end
    if redis.call('GET', KEYS[11]) == ARGV[1] then redis.call('DEL', KEYS[11]) end
    return -1
end
if string.lower(tostring(obj.CronTickerId or '')) ~= string.lower(ARGV[2]) then return 0 end
local status = tonumber(obj.Status)
if status ~= idle and status ~= queued then return 0 end
if obj.LockHolder ~= nil and obj.LockHolder ~= cjson.null and obj.LockHolder ~= '' then return 0 end
if obj.AcquisitionToken ~= nil and obj.AcquisitionToken ~= cjson.null and obj.AcquisitionToken ~= '' then return 0 end
local lease = obj.LeaseUntil
if lease ~= nil and lease ~= cjson.null and lease ~= '' and tostring(lease) > ARGV[3] then return 0 end
obj.Status = skipped
obj.SkippedReason = ARGV[7]
if obj.ExecutedAt == nil or obj.ExecutedAt == cjson.null or obj.ExecutedAt == '' then obj.ExecutedAt = ARGV[3] end
obj.UpdatedAt = ARGV[3]
obj.LockHolder = cjson.null
obj.LockedAt = cjson.null
obj.LeaseUntil = cjson.null
obj.AcquisitionToken = cjson.null
redis.call('SET', KEYS[1], cjson.encode(obj))
redis.call('ZREM', KEYS[4], ARGV[1])
for i = 6, 8 do redis.call('ZREM', KEYS[i], ARGV[1]) end
redis.call('ZADD', KEYS[9], retentionScore, ARGV[1])
redis.call('HDEL', KEYS[10], ARGV[1])
if redis.call('GET', KEYS[11]) == ARGV[1] then redis.call('DEL', KEYS[11]) end
return 1
