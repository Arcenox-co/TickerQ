-- Fence one dead chain atomically: a terminal time-ticker root whose chain lease expired under the
-- given generation belongs to a node that died mid-chain. Clearing the root's lease and generation
-- fences any late write from that node; the embedded descendants it left InProgress under that
-- generation are then cancelled. Steps that never started keep their status.
-- KEYS: root entity, activation metadata
-- ARGV: now, generation, expectedUpdatedAt, nextUpdatedAt, inProgress, cancelled, staleReason,
--       exact supported epoch, scoped | legacy | invalid runtime admission mode, terminal statuses...
-- Returns 'cancelledCount|json', 'retry' when the aggregate changed since it was read, or nil.
local activationType = redis.call('TYPE', KEYS[#KEYS])['ok']
if ARGV[9] == 'invalid' then return nil end
if ARGV[9] ~= 'scoped' and ARGV[9] ~= 'legacy' then return redis.error_reply('invalid runtime admission mode') end
if activationType ~= 'none' and activationType ~= 'hash' then return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type') end
if ARGV[9] == 'legacy' and activationType == 'hash' and
   redis.call('HGET', KEYS[#KEYS], 'legacyAdoptionState') then return nil end
if ARGV[9] == 'scoped' and activationType == 'none' then return nil end
if ARGV[9] == 'scoped' and activationType == 'hash' then
  local metadata = redis.call('HGETALL', KEYS[#KEYS])
  if #metadata ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
  local fields = {}; for i = 1, #metadata, 2 do fields[metadata[i]] = metadata[i + 1] end
  local phase = fields['phase']
  if not fields['epoch'] or not phase or fields['checkpoint'] == nil or not string.match(fields['epoch'], '^[0-9]+$')
    or #fields['epoch'] > 19 or #fields['checkpoint'] > 200 or (phase ~= '0' and phase ~= '1' and phase ~= '2') then
    return redis.error_reply('reconciliation activation metadata is corrupt')
  end
  if phase ~= '2' or fields['epoch'] ~= ARGV[8] then return nil end
end
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local ok, obj = pcall(cjson.decode, json)
if not ok or type(obj) ~= 'table' then return nil end
local function present(value) return value ~= nil and value ~= cjson.null and value ~= '' end
local function sameId(value, expected)
  return present(value) and string.lower(tostring(value)) == string.lower(tostring(expected))
end
local status = tonumber(obj['Status'] or obj['status'])
local terminal = false
for i = 10, #ARGV do if status == tonumber(ARGV[i]) then terminal = true end end
if not terminal or present(obj['ParentId'] or obj['parentId']) then return nil end
if not sameId(obj['ChainGeneration'] or obj['chainGeneration'], ARGV[2]) then return nil end
local leaseUntil = obj['LeaseUntil'] or obj['leaseUntil']
if not present(leaseUntil) or tostring(leaseUntil) >= ARGV[1] then return nil end
if tostring(obj['UpdatedAt'] or obj['updatedAt']) ~= ARGV[3] then return 'retry' end
local rootId = obj['Id'] or obj['id']
obj['LeaseUntil'] = cjson.null; obj['leaseUntil'] = nil
obj['ChainGeneration'] = cjson.null; obj['chainGeneration'] = nil
obj['UpdatedAt'] = ARGV[4]; obj['updatedAt'] = nil
local cancelled = 0
local function cancel(children)
  if type(children) ~= 'table' then return end
  for _, child in pairs(children) do
    if type(child) == 'table' then
      if sameId(child['ChainRootId'] or child['chainRootId'], rootId) and
         sameId(child['ChainGeneration'] or child['chainGeneration'], ARGV[2]) and
         tonumber(child['Status'] or child['status']) == tonumber(ARGV[5]) then
        child['Status'] = tonumber(ARGV[6]); child['status'] = nil
        child['ExceptionMessage'] = ARGV[7]; child['exceptionMessage'] = nil
        child['ExecutedAt'] = ARGV[1]; child['executedAt'] = nil
        child['LeaseUntil'] = cjson.null; child['leaseUntil'] = nil
        child['AcquisitionToken'] = cjson.null; child['acquisitionToken'] = nil
        child['UpdatedAt'] = ARGV[1]; child['updatedAt'] = nil
        cancelled = cancelled + 1
      end
      cancel(child['Children'] or child['children'])
    end
  end
end
cancel(obj['Children'] or obj['children'])
local updated = cjson.encode(obj)
-- cjson encodes an empty Lua table as '{}', turning empty JSON arrays ('[]') into objects.
-- Restore the array-typed fields so C# deserialization does not fail on re-encode.
updated = updated:gsub('"Children":{}', '"Children":[]')
updated = updated:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
redis.call('SET', KEYS[1], updated)
return tostring(cancelled) .. '|' .. updated
