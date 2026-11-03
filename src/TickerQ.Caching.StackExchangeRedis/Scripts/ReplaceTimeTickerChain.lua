-- Atomically replaces a time-ticker chain: removes the old root aggregate, its result sidecars, indexes,
-- and terminal evidence, and publishes the new aggregate behind runtime admission. Either both happen or
-- neither does, so a failed replacement never loses the original.
-- Loaded after TimeTickerAggregateSafety.lua and RuntimeAdmission.lua.
-- KEYS[1] old document, KEYS[2] new document, KEYS[3] all-id set, KEYS[4] pending sorted set,
-- KEYS[5] reconciliation activation metadata, KEYS[6] terminal evidence hash, KEYS[7..n] old result keys
-- ARGV[1] old root id, ARGV[2] expected old document, ARGV[3] now, ARGV[4] in-progress status,
-- ARGV[5] new root id, ARGV[6] new JSON, ARGV[7] new pending score or empty,
-- ARGV[8] exact supported epoch, ARGV[9] runtime admission mode, ARGV[10..n] old typed evidence fields
-- Returns 1 when replaced, 0 when the original must be kept or admission is closed, and -2 when the
-- original changed since it was read.
if #KEYS < 6 or #ARGV < 9 or ARGV[1] == '' or ARGV[2] == '' or ARGV[5] == '' or
   string.lower(ARGV[1]) == string.lower(ARGV[5]) then
  return redis.error_reply('invalid time ticker chain replacement arguments')
end
if not isAdmissionMode(ARGV[9]) then
  return redis.error_reply('invalid runtime admission mode')
end
local expectedTypes = {'string','string','set','zset','hash','hash'}
for i = 1, #KEYS do
  local expected = expectedTypes[i] or 'string'
  local actual = redis.call('TYPE', KEYS[i])['ok']
  if actual ~= 'none' and actual ~= expected then
    return redis.error_reply('time ticker chain replacement key has an incompatible Redis type')
  end
end
if ARGV[7] ~= '' and tonumber(ARGV[7]) == nil then
  return redis.error_reply('invalid time ticker pending score')
end
for i = 10, #ARGV do
  if ARGV[i] == '' then return redis.error_reply('invalid terminal evidence field') end
end
local newOk, replacement = pcall(cjson.decode, ARGV[6])
if not newOk or type(replacement) ~= 'table' or
   string.lower(tostring(replacement.Id or replacement.id or '')) ~= string.lower(ARGV[5]) then
  return redis.error_reply('invalid or mismatched replacement time ticker document')
end

local raw = redis.call('GET', KEYS[1])
if not raw then return 0 end
if raw ~= ARGV[2] then return -2 end
local oldOk, original = pcall(cjson.decode, raw)
if not oldOk or type(original) ~= 'table' or
   string.lower(tostring(original.Id or original.id or '')) ~= string.lower(ARGV[1]) then
  return redis.error_reply('cannot replace corrupt or mismatched time ticker document')
end
local nowSeconds, nowFraction = dateTimeParts(ARGV[3])
local inProgress = tonumber(ARGV[4])
if not nowSeconds or not inProgress or inProgress % 1 ~= 0 then
  return redis.error_reply('invalid time ticker chain replacement fence')
end
if not aggregateIsRemovable(raw, original, nowSeconds, nowFraction, inProgress) then return 0 end
local admitted = runtimeAdmits(KEYS[5], ARGV[8], ARGV[9])
if type(admitted) == 'table' then return admitted end
if not admitted then return 0 end
if redis.call('EXISTS', KEYS[2]) == 1 then return 0 end

redis.call('DEL', KEYS[1])
redis.call('SREM', KEYS[3], ARGV[1])
redis.call('ZREM', KEYS[4], ARGV[1])
for i = 10, #ARGV do redis.call('HDEL', KEYS[6], ARGV[i]) end
for i = 7, #KEYS do redis.call('DEL', KEYS[i]) end
redis.call('SET', KEYS[2], ARGV[6])
redis.call('SADD', KEYS[3], ARGV[5])
if ARGV[7] ~= '' then
  redis.call('ZADD', KEYS[4], ARGV[7], ARGV[5])
end
return 1
