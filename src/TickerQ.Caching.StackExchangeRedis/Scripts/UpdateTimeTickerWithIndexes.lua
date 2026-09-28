-- Atomically replace one time-ticker document and maintain all of its derived indexes.
-- KEYS: document, activation metadata, ids set, pending zset, then succeeded/failed/cancelled/skipped retention zsets.
-- ARGV: expectedUpdatedAt, replacementJson, id, pending flag (0/1), pending score,
--       retention index (0 none, 1 succeeded, 2 failed, 3 cancelled, 4 skipped), retention score.
-- Returns 1 on success, 0 when fenced by legacy adoption, -1 when the document disappeared,
-- and -2 when the observed document version changed.
if #KEYS ~= 8 or #ARGV ~= 7 then
    return redis.error_reply('invalid unified time ticker mutation shape')
end

local activationType = redis.call('TYPE', KEYS[2])['ok']
if activationType ~= 'none' and activationType ~= 'hash' then
    return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
end
if activationType == 'hash' and redis.call('HGET', KEYS[2], 'legacyAdoptionState') then
    return 0
end

local function type_is(key, expected)
    local actual = redis.call('TYPE', key)['ok']
    return actual == 'none' or actual == expected
end
if not type_is(KEYS[1], 'string') then
    return redis.error_reply('time ticker document key has an incompatible Redis type')
end
if not type_is(KEYS[3], 'set') then
    return redis.error_reply('time ticker ids key has an incompatible Redis type')
end
for index = 4, 8 do
    if not type_is(KEYS[index], 'zset') then
        return redis.error_reply('time ticker derived index key has an incompatible Redis type')
    end
end

if ARGV[1] == '' or ARGV[3] == '' or (ARGV[4] ~= '0' and ARGV[4] ~= '1') then
    return redis.error_reply('invalid unified time ticker mutation arguments')
end
local retentionIndex = tonumber(ARGV[6])
if not retentionIndex or retentionIndex % 1 ~= 0 or retentionIndex < 0 or retentionIndex > 4 then
    return redis.error_reply('invalid time ticker retention index')
end
local function finite_score(value)
    local score = tonumber(value)
    return score and score == score and score ~= math.huge and score ~= -math.huge
end
if ARGV[4] == '1' and not finite_score(ARGV[5]) then
    return redis.error_reply('time ticker pending score is not finite')
end
if retentionIndex ~= 0 and not finite_score(ARGV[7]) then
    return redis.error_reply('time ticker retention score is not finite')
end

local replacementOk, replacement = pcall(cjson.decode, ARGV[2])
if not replacementOk or type(replacement) ~= 'table' then
    return redis.error_reply('invalid replacement time ticker json')
end
local replacementId = replacement['Id'] or replacement['id']
if not replacementId or replacementId == cjson.null or
   string.lower(tostring(replacementId)) ~= string.lower(ARGV[3]) then
    return redis.error_reply('replacement time ticker id mismatch')
end

local currentJson = redis.call('GET', KEYS[1])
if not currentJson then return -1 end
local currentOk, current = pcall(cjson.decode, currentJson)
if not currentOk or type(current) ~= 'table' then
    return redis.error_reply('invalid current time ticker json')
end
local currentId = current['Id'] or current['id']
if not currentId or currentId == cjson.null or
   string.lower(tostring(currentId)) ~= string.lower(ARGV[3]) then
    return redis.error_reply('current time ticker id mismatch')
end
local currentUpdatedAt = current['UpdatedAt'] or current['updatedAt']
if not currentUpdatedAt or tostring(currentUpdatedAt) ~= ARGV[1] then return -2 end

-- Every condition capable of producing a deterministic Redis/JSON error was checked above.
-- Redis serializes this document and index mutation as one Lua invocation.
redis.call('SET', KEYS[1], ARGV[2])
redis.call('SADD', KEYS[3], ARGV[3])
if ARGV[4] == '1' then
    redis.call('ZADD', KEYS[4], ARGV[5], ARGV[3])
else
    redis.call('ZREM', KEYS[4], ARGV[3])
end
for index = 5, 8 do redis.call('ZREM', KEYS[index], ARGV[3]) end
if retentionIndex ~= 0 then
    redis.call('ZADD', KEYS[4 + retentionIndex], ARGV[7], ARGV[3])
end
return 1
