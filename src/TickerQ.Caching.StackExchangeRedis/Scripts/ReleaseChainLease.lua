-- Release the chain lease a terminal time-ticker root kept for its descendants.
-- KEYS: root entity
-- ARGV: holder, chain generation, expectedUpdatedAt, nextUpdatedAt, terminal statuses...
-- Returns the updated JSON, 'retry' when the aggregate changed since it was read, or nil when
-- this process does not hold that chain lease.
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local ok, obj = pcall(cjson.decode, json)
if not ok or type(obj) ~= 'table' then return nil end
local function present(value) return value ~= nil and value ~= cjson.null and value ~= '' end
local function sameId(value, expected)
  return present(value) and string.lower(tostring(value)) == string.lower(expected)
end
local status = tonumber(obj['Status'] or obj['status'])
local terminal = false
for i = 5, #ARGV do if status == tonumber(ARGV[i]) then terminal = true end end
if not terminal or present(obj['ParentId'] or obj['parentId']) then return nil end
if tostring(obj['LockHolder'] or obj['lockHolder']) ~= ARGV[1] then return nil end
if not present(obj['LeaseUntil'] or obj['leaseUntil']) then return nil end
if not sameId(obj['ChainGeneration'] or obj['chainGeneration'], ARGV[2]) then return nil end
-- A strictly newer UpdatedAt fences every concurrent read-modify-CAS writer of the aggregate.
if tostring(obj['UpdatedAt'] or obj['updatedAt']) ~= ARGV[3] then return 'retry' end
obj['LeaseUntil'] = cjson.null; obj['leaseUntil'] = nil
obj['UpdatedAt'] = ARGV[4]; obj['updatedAt'] = nil
local updated = cjson.encode(obj)
-- cjson encodes an empty Lua table as '{}', turning empty JSON arrays ('[]') into objects.
-- Restore the array-typed fields so C# deserialization does not fail on re-encode.
updated = updated:gsub('"Children":{}', '"Children":[]')
updated = updated:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
redis.call('SET', KEYS[1], updated)
return updated
