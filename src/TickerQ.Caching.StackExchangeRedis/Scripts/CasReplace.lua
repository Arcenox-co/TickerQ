-- Atomic JSON/result replacement guarded by the observed generation and version.
-- KEYS: entity, result side key
-- ARGV: expectedHolder, expectedToken, expectedStatus (-1 skips), expectedUpdatedAt,
--       replacementJson, resultAction (none/set/clear), resultEnvelope, embeddedTargetId,
--       expectedChainGeneration (required for embedded targets)
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local obj = cjson.decode(json)
local fence = obj
if ARGV[8] ~= '' then
  local rootId = obj['Id'] or obj['id']
  local chainRootId = obj['ChainRootId'] or obj['chainRootId']
  local generation = obj['ChainGeneration'] or obj['chainGeneration']
  if ARGV[9] == '' or not rootId or not chainRootId or not generation
    or string.lower(tostring(rootId)) ~= string.lower(tostring(chainRootId))
    or string.lower(tostring(generation)) ~= string.lower(ARGV[9]) then return nil end
  local function find(children)
    if type(children) ~= 'table' then return nil end
    for _, child in pairs(children) do
      local id = child['Id'] or child['id']
      if id and string.lower(tostring(id)) == string.lower(ARGV[8]) then return child end
      local nested = find(child['Children'] or child['children'])
      if nested then return nested end
    end
    return nil
  end
  fence = find(obj['Children'] or obj['children'])
  if not fence then return nil end
end
local holder = fence['LockHolder'] or fence['lockHolder']
local token = fence['AcquisitionToken'] or fence['acquisitionToken']
local status = fence['Status'] or fence['status']
local updatedAt = obj['UpdatedAt'] or obj['updatedAt']
if ARGV[1] ~= '' and tostring(holder) ~= ARGV[1] then return nil end
if ARGV[2] ~= '' and (not token or token == cjson.null or string.lower(tostring(token)) ~= string.lower(ARGV[2])) then return nil end
if ARGV[3] ~= '-1' and tonumber(status) ~= tonumber(ARGV[3]) then return nil end
if ARGV[4] ~= '' and tostring(updatedAt) ~= ARGV[4] then return nil end
if ARGV[6] == 'set' then
  -- Publish first: if Redis rejects the result write, the terminal entity mutation is never acknowledged.
  redis.call('SET', KEYS[2], ARGV[7])
elseif ARGV[6] == 'clear' then
  redis.call('DEL', KEYS[2])
elseif ARGV[6] ~= 'none' then
  return redis.error_reply('invalid result action')
end
redis.call('SET', KEYS[1], ARGV[5])
return ARGV[5]
