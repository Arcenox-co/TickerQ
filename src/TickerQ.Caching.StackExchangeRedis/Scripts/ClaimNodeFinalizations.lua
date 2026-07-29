-- Claim at most maxCount due Node finalization records, cleaning only that bounded due page.
-- KEYS: records hash, due sorted set
-- ARGV: nowScore, maxCount, workerId, leaseScore, nowUtc, leaseUntilUtc, token1..tokenN
local maxCount = tonumber(ARGV[2])
if not maxCount or maxCount < 1 then return {} end
local members = redis.call('ZRANGEBYSCORE', KEYS[2], '-inf', ARGV[1], 'LIMIT', 0, maxCount)
local claimed = {}
for i, member in ipairs(members) do
  local json = redis.call('HGET', KEYS[1], member)
  if not json then
    redis.call('ZREM', KEYS[2], member)
  else
    local ok, record = pcall(cjson.decode, json)
    if not ok or type(record) ~= 'table' or record['OutboxId'] ~= member
      or not record['ImmutableDigest'] or not record['ExactBodyBase64']
      or not record['TickerId'] or not record['AcquisitionToken'] then
      redis.call('ZREM', KEYS[2], member)
    else
      record['ClaimToken'] = ARGV[6 + i]
      record['ClaimedBy'] = ARGV[3]
      record['LeaseUntilUtc'] = ARGV[6]
      record['AttemptCount'] = (tonumber(record['AttemptCount']) or 0) + 1
      record['LastAttemptAtUtc'] = ARGV[5]
      local updated = cjson.encode(record)
      redis.call('HSET', KEYS[1], member, updated)
      redis.call('ZADD', KEYS[2], ARGV[4], member)
      -- Return claim identity outside the JSON payload so the caller can exactly discard a
      -- semantically invalid record even when its JSON cannot be reconstructed as a C# record.
      table.insert(claimed, { member, ARGV[6 + i], ARGV[3], updated })
    end
  end
end
return claimed
