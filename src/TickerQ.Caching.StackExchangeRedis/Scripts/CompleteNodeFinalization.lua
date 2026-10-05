-- Complete only the exact immutable identity and active claim.
-- KEYS: records hash, due sorted set
-- ARGV: outboxId, immutableDigest, tickerType, tickerId, acquisitionToken, dispatchId,
--       nodeEpoch, claimToken, claimedBy
local json = redis.call('HGET', KEYS[1], ARGV[1])
if not json then return 0 end
local ok, record = pcall(cjson.decode, json)
if not ok or type(record) ~= 'table' then return 0 end
if record['OutboxId'] ~= ARGV[1] or record['ImmutableDigest'] ~= ARGV[2]
  or tostring(record['TickerType']) ~= ARGV[3] or record['TickerId'] ~= ARGV[4]
  or record['AcquisitionToken'] ~= ARGV[5] or record['DispatchId'] ~= ARGV[6]
  or record['NodeEpoch'] ~= ARGV[7] or record['ClaimToken'] ~= ARGV[8]
  or record['ClaimedBy'] ~= ARGV[9] then return 0 end
redis.call('HDEL', KEYS[1], ARGV[1])
redis.call('ZREM', KEYS[2], ARGV[1])
return 1
