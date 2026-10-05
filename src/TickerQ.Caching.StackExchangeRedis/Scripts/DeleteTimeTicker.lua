-- Atomically delete one time-ticker aggregate root, result sidecars, indexes, and bounded terminal evidence.
-- KEYS: document,allIds,pending,terminalEvidence,result sidecars...
-- ARGV: root id,expected document,now,in-progress status,typed evidence fields...
if #KEYS < 5 or #ARGV < 5 or ARGV[1] == '' or ARGV[2] == '' then
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
if not raw then return 0 end
if raw ~= ARGV[2] then return 0 end
local ok, obj = pcall(cjson.decode, raw)
if not ok or type(obj) ~= 'table' or
   string.lower(tostring(obj['Id'] or obj['id'] or '')) ~= string.lower(ARGV[1]) then
  return redis.error_reply('cannot delete corrupt or mismatched time ticker document')
end
local function dateTimeParts(value)
  if type(value) ~= 'string' then return nil end
  local year, month, day, hour, minute, second, rest = string.match(value,
    '^(%d%d%d%d)%-(%d%d)%-(%d%d)T(%d%d):(%d%d):(%d%d)(.*)$')
  if not year then return nil end
  year, month, day = tonumber(year), tonumber(month), tonumber(day)
  hour, minute, second = tonumber(hour), tonumber(minute), tonumber(second)
  if year < 1 or year > 9999 or month < 1 or month > 12 or
     hour > 23 or minute > 59 or second > 59 then return nil end
  local leap = year % 400 == 0 or (year % 4 == 0 and year % 100 ~= 0)
  local monthDays = {31, leap and 29 or 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31}
  if day < 1 or day > monthDays[month] then return nil end
  local fraction, suffix = '', rest
  if string.sub(rest, 1, 1) == '.' then
    fraction, suffix = string.match(rest, '^%.(%d+)(.*)$')
    if not fraction or string.len(fraction) > 7 then return nil end
  end
  local offsetMinutes = 0
  if suffix ~= 'Z' then
    local sign, offsetHour, offsetMinute = string.match(suffix, '^([%+%-])(%d%d):(%d%d)$')
    if not sign then return nil end
    offsetHour, offsetMinute = tonumber(offsetHour), tonumber(offsetMinute)
    if offsetHour > 14 or offsetMinute > 59 or (offsetHour == 14 and offsetMinute ~= 0) then return nil end
    offsetMinutes = offsetHour * 60 + offsetMinute
    if sign == '-' then offsetMinutes = -offsetMinutes end
  end
  local priorYear = year - 1
  local days = priorYear * 365 + math.floor(priorYear / 4)
    - math.floor(priorYear / 100) + math.floor(priorYear / 400)
  local beforeMonth = {0,31,59,90,120,151,181,212,243,273,304,334}
  days = days + beforeMonth[month] + day - 1
  if month > 2 and leap then days = days + 1 end
  local utcSeconds = days * 86400 + hour * 3600 + minute * 60 + second - offsetMinutes * 60
  if utcSeconds < 0 or utcSeconds > 315537897599 then return nil end
  return utcSeconds, tonumber(string.sub(fraction .. '0000000', 1, 7))
end
local nowSeconds, nowFraction = dateTimeParts(ARGV[3])
local inProgress = tonumber(ARGV[4])
if not nowSeconds or not inProgress or inProgress % 1 ~= 0 then
  return redis.error_reply('invalid time ticker deletion fence')
end
local function isArray(value)
  if type(value) ~= 'table' then return false end
  local count = 0
  for key, _ in pairs(value) do
    if type(key) ~= 'number' or key < 1 or key % 1 ~= 0 then return false end
    count = count + 1
  end
  for index = 1, count do if value[index] == nil then return false end end
  return true
end
local function childrenContainersAreArrays(json)
  local index, length = 1, string.len(json)
  while index <= length do
    if string.sub(json, index, index) == '"' then
      local start = index
      index = index + 1
      while index <= length do
        local char = string.sub(json, index, index)
        if char == '\\' then index = index + 2
        elseif char == '"' then break
        else index = index + 1 end
      end
      if index > length then return false end
      local token = string.sub(json, start, index)
      local nextIndex = index + 1
      while string.match(string.sub(json, nextIndex, nextIndex), '%s') do nextIndex = nextIndex + 1 end
      if string.sub(json, nextIndex, nextIndex) == ':' then
        local decoded, key = pcall(cjson.decode, token)
        if not decoded then return false end
        if key == 'Children' then
          nextIndex = nextIndex + 1
          while string.match(string.sub(json, nextIndex, nextIndex), '%s') do nextIndex = nextIndex + 1 end
          if string.sub(json, nextIndex, nextIndex) ~= '[' and
             string.sub(json, nextIndex, nextIndex + 3) ~= 'null' then return false end
        end
      end
    end
    index = index + 1
  end
  return true
end
local function isSafe(node)
  if type(node) ~= 'table' or type(node.Status) ~= 'number' or node.Status % 1 ~= 0 or
     node.Status == inProgress then return false end
  if node.AcquisitionToken ~= nil and node.AcquisitionToken ~= cjson.null and node.AcquisitionToken ~= '' then
    return false
  end
  if node.LeaseUntil ~= nil and node.LeaseUntil ~= cjson.null and node.LeaseUntil ~= '' then
    local leaseSeconds, leaseFraction = dateTimeParts(node.LeaseUntil)
    if not leaseSeconds or leaseSeconds > nowSeconds or
       (leaseSeconds == nowSeconds and leaseFraction > nowFraction) then return false end
  end
  if node.Children ~= nil and node.Children ~= cjson.null then
    if not isArray(node.Children) then return false end
    for _, child in ipairs(node.Children) do if not isSafe(child) then return false end end
  end
  return true
end
if not childrenContainersAreArrays(raw) or not isSafe(obj) or
   (obj.ParentId ~= nil and obj.ParentId ~= cjson.null and obj.ParentId ~= '') then return 0 end
redis.call('DEL', KEYS[1])
redis.call('SREM', KEYS[2], ARGV[1])
redis.call('ZREM', KEYS[3], ARGV[1])
for i = 5, #ARGV do redis.call('HDEL', KEYS[4], ARGV[i]) end
for i = 5, #KEYS do redis.call('DEL', KEYS[i]) end
return 1
