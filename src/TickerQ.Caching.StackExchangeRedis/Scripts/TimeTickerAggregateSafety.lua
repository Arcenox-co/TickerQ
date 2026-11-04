-- Shared by the scripts that remove a time-ticker aggregate (deletion and chain replacement):
-- whether a stored aggregate may be removed. Defines functions only; the including script runs.
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

local function present(value) return value ~= nil and value ~= cjson.null and value ~= '' end

-- Missing policy is Cascade for existing documents. Unknown policies retain the whole chain.
local function descendantsFinished(node)
  if node.Children ~= nil and node.Children ~= cjson.null then
    for _, child in ipairs(node.Children) do
      -- Done, DueDone, Failed, Cancelled, Skipped. Idle/Queued/InProgress and unknown statuses wait.
      if child.Status ~= 3 and child.Status ~= 4 and child.Status ~= 5 and child.Status ~= 6 and child.Status ~= 7 then return false end
      if not descendantsFinished(child) then return false end
    end
  end
  return true
end

-- True when the root aggregate decoded from raw has not started and its deletion policy allows removal.
local function aggregateIsRemovable(raw, obj, nowSeconds, nowFraction, inProgress)
  -- A Queued (1) root's generation is only the scheduler's claim on unstarted work, which acquisition
  -- also stamps on the root's descendants. Moving the root to InProgress is fenced on that generation,
  -- so a removed document is never started. Any other generation belongs to work that has started.
  local claim = nil
  if obj.Status == 1 and present(obj.AcquisitionToken) then claim = obj.AcquisitionToken end
  local function unstarted(node)
    if type(node) ~= 'table' or type(node.Status) ~= 'number' or node.Status % 1 ~= 0 or
       node.Status == inProgress then return false end
    if present(node.AcquisitionToken) and node.AcquisitionToken ~= claim then return false end
    if present(node.LeaseUntil) then
      local leaseSeconds, leaseFraction = dateTimeParts(node.LeaseUntil)
      if not leaseSeconds or leaseSeconds > nowSeconds or
         (leaseSeconds == nowSeconds and leaseFraction > nowFraction) then return false end
    end
    if node.Children ~= nil and node.Children ~= cjson.null then
      if not isArray(node.Children) then return false end
      for _, child in ipairs(node.Children) do if not unstarted(child) then return false end end
    end
    return true
  end
  if not childrenContainersAreArrays(raw) or not unstarted(obj) or present(obj.ParentId) then return false end
  if obj.Children ~= nil and obj.Children ~= cjson.null and next(obj.Children) ~= nil and
     obj.DeleteBehavior ~= nil and obj.DeleteBehavior ~= 0 then
    if obj.DeleteBehavior ~= 1 or not descendantsFinished(obj) then return false end
  end
  return true
end
