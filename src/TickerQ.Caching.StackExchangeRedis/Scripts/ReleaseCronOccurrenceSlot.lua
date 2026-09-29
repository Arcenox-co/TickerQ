-- ABA-safe recurring-slot release. A stale owner may release only its own reservation.
-- KEYS[1] slot key; ARGV[1] expected occurrence id.
if #KEYS ~= 1 or #ARGV ~= 1 or ARGV[1] == '' then return redis.error_reply('invalid slot release arguments') end
local t = redis.call('TYPE', KEYS[1])['ok']
if t ~= 'none' and t ~= 'string' then return redis.error_reply('recurring-slot key has an incompatible Redis type') end
if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end
return redis.call('DEL', KEYS[1])
