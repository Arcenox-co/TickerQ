-- Exactly discard a semantically corrupt record claimed by this caller. The exact raw-value
-- fence prevents deleting a concurrently repaired record or a newer lease.
-- KEYS: records hash, due sorted set
-- ARGV: outboxId, claimToken, claimedBy, exactRawRecord
local current = redis.call('HGET', KEYS[1], ARGV[1])
if not current or current ~= ARGV[4] then return 0 end
local ok, record = pcall(cjson.decode, current)
if not ok or type(record) ~= 'table' or record['OutboxId'] ~= ARGV[1]
  or record['ClaimToken'] ~= ARGV[2] or record['ClaimedBy'] ~= ARGV[3] then
  return 0
end
redis.call('HDEL', KEYS[1], ARGV[1])
redis.call('ZREM', KEYS[2], ARGV[1])
return 1