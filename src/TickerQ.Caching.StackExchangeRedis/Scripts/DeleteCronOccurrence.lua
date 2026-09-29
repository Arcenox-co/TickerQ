-- Atomically delete one occurrence, its result and every index, and ABA-safely release its slot.
-- Explicit mode carries the observed execution generation. Retention mode carries the two accepted
-- statuses, strict cutoff, and current time; each eligibility predicate and deletion remain one boundary.
-- KEYS: document,result,allIds,pending,byCron,retention x4,slot,terminalEvidence
-- ARGV retention: occurrence id,expected cron id,first status,second status,cutoff,now
-- ARGV explicit: occurrence id,expected cron id,status,token,lease,updatedAt,observedAt
if #KEYS ~= 11 or (#ARGV ~= 6 and #ARGV ~= 7) or ARGV[1] == '' or ARGV[2] == '' then
  return redis.error_reply('invalid occurrence deletion arguments')
end
local expected = {'string','string','set','zset','set','zset','zset','zset','zset','string','hash'}
for i = 1, #KEYS do
  local t = redis.call('TYPE', KEYS[i])['ok']
  if t ~= 'none' and t ~= expected[i] then return redis.error_reply('occurrence deletion key has an incompatible Redis type') end
end
local raw = redis.call('GET', KEYS[1])
if raw then
  local ok, obj = pcall(cjson.decode, raw)
  if not ok or type(obj) ~= 'table' then return redis.error_reply('cannot delete corrupt occurrence document') end
  if string.lower(tostring(obj['Id'] or obj['id'] or '')) ~= string.lower(ARGV[1]) or
     string.lower(tostring(obj['CronTickerId'] or obj['cronTickerId'] or '')) ~= string.lower(ARGV[2]) then return 0 end
  local function normalizedDateTime(value)
    local year, month, day, hour, minute, second, fraction = string.match(
      value or '', '^(%d%d%d%d)%-(%d%d)%-(%d%d)T(%d%d):(%d%d):(%d%d)%.?(%d*)')
    if not year then return nil end
    return year .. month .. day .. hour .. minute .. second
      .. string.sub((fraction or '') .. '0000000', 1, 7)
  end
  if #ARGV == 6 then
    local status = tonumber(obj.Status or obj.status)
    if status ~= tonumber(ARGV[3]) and status ~= tonumber(ARGV[4]) then return 0 end
    local executedAt = obj.ExecutedAt ~= nil and obj.ExecutedAt ~= cjson.null
      and normalizedDateTime(obj.ExecutedAt) or nil
    if executedAt == nil or executedAt >= normalizedDateTime(ARGV[5]) then return 0 end
    if obj.AcquisitionToken ~= nil and obj.AcquisitionToken ~= cjson.null and obj.AcquisitionToken ~= '' then return 0 end
    local leaseUntil = obj.LeaseUntil ~= nil and obj.LeaseUntil ~= cjson.null
      and obj.LeaseUntil ~= '' and normalizedDateTime(obj.LeaseUntil) or nil
    if leaseUntil ~= nil and leaseUntil > normalizedDateTime(ARGV[6]) then return 0 end
  else
    local status = tonumber(obj.Status or obj.status)
    local token = obj.AcquisitionToken or obj.acquisitionToken
    if token == nil or token == cjson.null then token = '' else token = string.lower(tostring(token)) end
    local lease = obj.LeaseUntil or obj.leaseUntil
    if lease == nil or lease == cjson.null or lease == '' then lease = '' else lease = normalizedDateTime(lease) end
    local updatedAt = normalizedDateTime(obj.UpdatedAt or obj.updatedAt)
    local expectedLease = ARGV[5] == '' and '' or normalizedDateTime(ARGV[5])
    if status ~= tonumber(ARGV[3]) or token ~= string.lower(ARGV[4]) or
       lease ~= expectedLease or updatedAt ~= normalizedDateTime(ARGV[6]) then return 0 end
  end
end
redis.call('DEL', KEYS[1], KEYS[2])
redis.call('SREM', KEYS[3], ARGV[1])
redis.call('ZREM', KEYS[4], ARGV[1])
redis.call('SREM', KEYS[5], ARGV[1])
for i = 6, 9 do redis.call('ZREM', KEYS[i], ARGV[1]) end
if redis.call('GET', KEYS[10]) == ARGV[1] then redis.call('DEL', KEYS[10]) end
redis.call('HDEL', KEYS[11], '0:' .. string.lower(ARGV[1]))
return raw and 1 or 0
