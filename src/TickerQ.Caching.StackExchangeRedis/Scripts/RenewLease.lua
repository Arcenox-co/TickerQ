-- Renew one lease iff this process still owns the same InProgress generation.
-- A time-ticker root that finished its own function but still holds its chain lease is renewed
-- under its chain generation instead (its descendants run under that lease).
-- ARGV: holder, token, inProgressStatus, leaseUntil, chainLease (1 = time ticker | 0),
--       terminal statuses...
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local obj = cjson.decode(json)
local function present(value) return value ~= nil and value ~= cjson.null and value ~= '' end
local function sameId(value, expected)
  return present(value) and string.lower(tostring(value)) == string.lower(expected)
end
local holder = obj['LockHolder'] or obj['lockHolder']
local token = obj['AcquisitionToken'] or obj['acquisitionToken']
local status = tonumber(obj['Status'] or obj['status'])
if tostring(holder) ~= ARGV[1] then return nil end
local held = sameId(token, ARGV[2]) and status == tonumber(ARGV[3])
if not held and ARGV[5] == '1' then
  local terminal = false
  for i = 6, #ARGV do if status == tonumber(ARGV[i]) then terminal = true end end
  held = terminal and not present(obj['ParentId'] or obj['parentId']) and
    present(obj['LeaseUntil'] or obj['leaseUntil']) and
    sameId(obj['ChainGeneration'] or obj['chainGeneration'], ARGV[2])
end
if not held then return nil end
obj['LeaseUntil'] = ARGV[4]
obj['leaseUntil'] = nil
local updated = cjson.encode(obj)
-- cjson encodes an empty Lua table as '{}', turning empty JSON arrays ('[]') into objects.
-- Restore the array-typed fields so C# deserialization does not fail on re-encode.
updated = updated:gsub('"Children":{}', '"Children":[]')
updated = updated:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
redis.call('SET', KEYS[1], updated)
return 1
