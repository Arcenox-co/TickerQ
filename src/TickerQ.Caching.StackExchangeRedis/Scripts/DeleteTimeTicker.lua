-- Atomically delete one time-ticker aggregate root, result sidecars, indexes, and bounded terminal evidence.
-- KEYS: document,allIds,pending,terminalEvidence,result sidecars...
-- ARGV: root id,typed evidence fields...
if #KEYS < 5 or #ARGV < 2 or ARGV[1] == '' then
  return redis.error_reply('invalid time ticker deletion arguments')
end
local expected = {'string','set','zset','hash'}
for i = 1, 4 do
  local kind = redis.call('TYPE', KEYS[i])['ok']
  if kind ~= 'none' and kind ~= expected[i] then
    return redis.error_reply('time ticker deletion key has an incompatible Redis type')
  end
end
for i = 5, #KEYS do
  local kind = redis.call('TYPE', KEYS[i])['ok']
  if kind ~= 'none' and kind ~= 'string' then
    return redis.error_reply('time ticker result key has an incompatible Redis type')
  end
end
local raw = redis.call('GET', KEYS[1])
if not raw then
  redis.call('SREM', KEYS[2], ARGV[1])
  redis.call('ZREM', KEYS[3], ARGV[1])
  for i = 2, #ARGV do redis.call('HDEL', KEYS[4], ARGV[i]) end
  for i = 5, #KEYS do redis.call('DEL', KEYS[i]) end
  return 0
end
local ok, obj = pcall(cjson.decode, raw)
if not ok or type(obj) ~= 'table' or
   string.lower(tostring(obj['Id'] or obj['id'] or '')) ~= string.lower(ARGV[1]) then
  return redis.error_reply('cannot delete corrupt or mismatched time ticker document')
end
redis.call('DEL', KEYS[1])
redis.call('SREM', KEYS[2], ARGV[1])
redis.call('ZREM', KEYS[3], ARGV[1])
for i = 2, #ARGV do redis.call('HDEL', KEYS[4], ARGV[i]) end
for i = 5, #KEYS do redis.call('DEL', KEYS[i]) end
return 1
