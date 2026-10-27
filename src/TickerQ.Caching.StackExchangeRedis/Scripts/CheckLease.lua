-- Returns 1 iff this process still owns the same InProgress generation.
-- A time-ticker root this process finished under the same chain generation is still held: its
-- chain may still be running, and nothing else can own that generation.
-- ARGV: holder, token, inProgressStatus, chainLease (1 = time ticker | 0), terminal statuses...
local json = redis.call('GET', KEYS[1])
if not json then return 0 end
local obj = cjson.decode(json)
local function present(value) return value ~= nil and value ~= cjson.null and value ~= '' end
local function sameId(value, expected)
  return present(value) and string.lower(tostring(value)) == string.lower(expected)
end
local holder = obj['LockHolder'] or obj['lockHolder']
local token = obj['AcquisitionToken'] or obj['acquisitionToken']
local status = tonumber(obj['Status'] or obj['status'])
if tostring(holder) ~= ARGV[1] then return 0 end
if sameId(token, ARGV[2]) and status == tonumber(ARGV[3]) then return 1 end
if ARGV[4] ~= '1' then return 0 end
local terminal = false
for i = 5, #ARGV do if status == tonumber(ARGV[i]) then terminal = true end end
if terminal and not present(obj['ParentId'] or obj['parentId']) and
   sameId(obj['ChainGeneration'] or obj['chainGeneration'], ARGV[2]) then return 1 end
return 0
