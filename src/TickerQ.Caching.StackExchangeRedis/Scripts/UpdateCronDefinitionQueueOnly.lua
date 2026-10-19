-- Queue-only Redis Cluster definition CAS. Exactly one key keeps the script in one hash slot.
-- KEYS[1] definition document
-- ARGV[1] expected serialized document, ARGV[2] replacement serialized document
if #KEYS ~= 1 or #ARGV ~= 2 then return redis.error_reply('invalid queue-only cron CAS arguments') end
local current = redis.call('GET', KEYS[1])
if not current or current ~= ARGV[1] then return 0 end
redis.call('SET', KEYS[1], ARGV[2])
return 1
