-- Atomic JSON/result replacement guarded by the observed generation and version.
-- KEYS: entity, result side key, optional finalization records hash, optional due sorted set,
--       optional authoritative Cron definition, activation metadata (always last)
-- ARGV: expectedHolder, expectedToken, expectedStatus (-1 skips), expectedUpdatedAt,
--       replacementJson, resultAction (none/set/clear), resultEnvelope, embeddedTargetId,
--       expectedChainGeneration (required for embedded targets), optional outboxRecordJson,
--       outboxId, immutableDigest, dueScore, revisionFence (0/1), caller epoch,
--       typed terminal evidence field, terminal mutation digest, cron-slot flag (0/1),
--       mutation mode (terminal | scoped | legacy | invalid)
local mutationMode = ARGV[19]
if mutationMode ~= 'terminal' and mutationMode ~= 'scoped' and mutationMode ~= 'legacy' and mutationMode ~= 'invalid' then
  return redis.error_reply('invalid CAS mutation mode')
end
if mutationMode == 'invalid' then return nil end
local activationType = mutationMode == 'scoped' and redis.call('TYPE', KEYS[#KEYS])['ok'] or 'none'
if activationType ~= 'none' and activationType ~= 'hash' then return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type') end
if mutationMode == 'scoped' and activationType == 'none' then return nil end
if mutationMode == 'scoped' and activationType == 'hash' then
  local metadata = redis.call('HGETALL', KEYS[#KEYS])
  if #metadata ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
  local fields = {}; for i = 1, #metadata, 2 do fields[metadata[i]] = metadata[i + 1] end
  local phase = fields['phase']
  if not fields['epoch'] or not phase or fields['checkpoint'] == nil or not string.match(fields['epoch'], '^[0-9]+$')
    or #fields['epoch'] > 19 or #fields['checkpoint'] > 200 or (phase ~= '0' and phase ~= '1' and phase ~= '2') then
    return redis.error_reply('reconciliation activation metadata is corrupt')
  end
  if phase ~= '2' or fields['epoch'] ~= ARGV[15] then return nil end
end
local enqueue = ARGV[10] and ARGV[10] ~= ''
local terminal = mutationMode == 'terminal'
local hasSlot = ARGV[18] == '1'
local function same_immutable(a, b)
  return a['SchemaVersion'] == b['SchemaVersion'] and a['OutboxId'] == b['OutboxId']
    and a['TickerType'] == b['TickerType'] and a['TickerId'] == b['TickerId']
    and a['AcquisitionToken'] == b['AcquisitionToken'] and a['DispatchId'] == b['DispatchId']
    and a['NodeEpoch'] == b['NodeEpoch'] and a['FinalizeUri'] == b['FinalizeUri']
    and a['FinalizePathAndQuery'] == b['FinalizePathAndQuery']
    and a['AllowPrivateCallbackAddressesForLocalDevelopment'] == b['AllowPrivateCallbackAddressesForLocalDevelopment']
    and a['RequestNonce'] == b['RequestNonce'] and a['ControlNonce'] == b['ControlNonce']
    and a['ExactBodyBase64'] == b['ExactBodyBase64'] and a['BodyDigest'] == b['BodyDigest']
    and a['TerminalMutationDigest'] == b['TerminalMutationDigest'] and a['CreatedAtUtc'] == b['CreatedAtUtc']
    and a['ImmutableDigest'] == b['ImmutableDigest']
end
local function type_is(key, allowed)
  local kind = redis.call('TYPE', key)['ok']
  return kind == 'none' or kind == allowed
end
if not type_is(KEYS[1], 'string') or not type_is(KEYS[2], 'string') then
  return redis.error_reply('invalid terminal key type')
end
if terminal and (not type_is(KEYS[#KEYS - 1], 'hash') or ARGV[17] == '') then
  return redis.error_reply('invalid terminal evidence key or digest')
end
if hasSlot and not type_is(KEYS[#KEYS - 2], 'string') then
  return redis.error_reply('invalid cron slot key type')
end
local authoritativeIndex = #KEYS - (hasSlot and 3 or 2)
if ARGV[14] == '1' and (not KEYS[authoritativeIndex] or not type_is(KEYS[authoritativeIndex], 'string')) then
  return redis.error_reply('invalid authoritative cron key type')
end
if enqueue and (not KEYS[3] or not KEYS[4] or not type_is(KEYS[3], 'hash') or not type_is(KEYS[4], 'zset')) then
  return redis.error_reply('invalid outbox key type')
end
if ARGV[6] ~= 'none' and ARGV[6] ~= 'set' and ARGV[6] ~= 'clear' then
  return redis.error_reply('invalid result action')
end
local replacementOk = pcall(cjson.decode, ARGV[5])
if not replacementOk then return redis.error_reply('invalid replacement json') end
local function superseded_by_active_generation()
  local expectedGeneration = ARGV[9] ~= '' and ARGV[9] or ARGV[2]
  if expectedGeneration == '' then return false end
  local currentJson = redis.call('GET', KEYS[1])
  local currentOk, current = pcall(cjson.decode, currentJson or '')
  if not currentOk or type(current) ~= 'table' then return false end
  local currentGeneration
  if ARGV[9] ~= '' then
    currentGeneration = current['ChainGeneration'] or current['chainGeneration']
  else
    currentGeneration = current['AcquisitionToken'] or current['acquisitionToken']
  end
  return currentGeneration and currentGeneration ~= cjson.null and
    string.lower(tostring(currentGeneration)) ~= string.lower(expectedGeneration)
end
if enqueue then
  if ARGV[11] == '' or ARGV[12] == '' or not tonumber(ARGV[13]) then
    return redis.error_reply('invalid outbox arguments')
  end
  local recordOk, record = pcall(cjson.decode, ARGV[10])
  if not recordOk or type(record) ~= 'table' or record['OutboxId'] ~= ARGV[11]
    or record['ImmutableDigest'] ~= ARGV[12] or type(record['TerminalMutationDigest']) ~= 'string'
    or string.len(record['TerminalMutationDigest']) ~= 64 then
    return redis.error_reply('invalid outbox record')
  end
  local existingJson = redis.call('HGET', KEYS[3], ARGV[11])
  if existingJson then
    local existingOk, existing = pcall(cjson.decode, existingJson)
    if not existingOk or type(existing) ~= 'table' or not same_immutable(existing, record) then
      return redis.error_reply('outbox id immutable mismatch')
    end
    -- Ambiguous prior invocation already committed all effects. Acknowledge idempotently even
    -- though the terminal write cleared the acquisition token and no longer passes its fence.
    if superseded_by_active_generation() then return nil end
    if terminal then redis.call('HSET', KEYS[#KEYS - 1], ARGV[16], ARGV[17]) end
    return ARGV[5]
  end
end
-- Validate and compare an extant outbox record before consulting generation evidence. This lets an
-- exact ambiguous replay acknowledge after the first terminal write changed the mutable replacement,
-- while same-ID intent or terminal-mutation mismatches still fail closed above.
local prior = terminal and redis.call('HGET', KEYS[#KEYS - 1], ARGV[16]) or nil
local function reject_or_replay()
  if prior then
    if superseded_by_active_generation() then return nil end
    if prior ~= ARGV[17] then
      local priorGeneration = string.match(prior, '^([^:]+):')
      local currentGeneration = string.match(ARGV[17], '^([^:]+):')
      -- A different generation is an expected stale write. The same generation with another
      -- immutable terminal intent is a protocol conflict and must fail closed.
      if priorGeneration and currentGeneration and
         string.lower(priorGeneration) ~= string.lower(currentGeneration) then
        return nil
      end
      return redis.error_reply('terminal generation immutable mismatch')
    end
    return ARGV[5]
  end
  return nil
end
local json = redis.call('GET', KEYS[1])
if not json then return reject_or_replay() end
local objectOk, obj = pcall(cjson.decode, json)
if not objectOk or type(obj) ~= 'table' then return redis.error_reply('invalid entity json') end
if ARGV[14] == '1' and not terminal then
  local definitionJson = redis.call('GET', KEYS[authoritativeIndex])
  local definitionOk, definition = pcall(cjson.decode, definitionJson or '')
  local occurrenceRevision = tonumber(obj['DefinitionRevision'] or obj['definitionRevision'] or 0)
  local definitionRevision = definitionOk and type(definition) == 'table'
    and tonumber(definition['DefinitionRevision'] or definition['definitionRevision'] or 0) or nil
  if not occurrenceRevision or not definitionRevision or occurrenceRevision ~= definitionRevision then return reject_or_replay() end
end
local fence = obj
if ARGV[8] ~= '' then
  local rootId = obj['Id'] or obj['id']
  local chainRootId = obj['ChainRootId'] or obj['chainRootId']
  local generation = obj['ChainGeneration'] or obj['chainGeneration']
  if ARGV[9] == '' or not rootId or not chainRootId or not generation
    or string.lower(tostring(rootId)) ~= string.lower(tostring(chainRootId))
    or string.lower(tostring(generation)) ~= string.lower(ARGV[9]) then return reject_or_replay() end
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
  if not fence then return reject_or_replay() end
end
local holder = fence['LockHolder'] or fence['lockHolder']
local token = fence['AcquisitionToken'] or fence['acquisitionToken']
local status = fence['Status'] or fence['status']
local updatedAt = obj['UpdatedAt'] or obj['updatedAt']
if ARGV[1] ~= '' and tostring(holder) ~= ARGV[1] then return reject_or_replay() end
if ARGV[2] ~= '' and (not token or token == cjson.null or string.lower(tostring(token)) ~= string.lower(ARGV[2])) then return reject_or_replay() end
if ARGV[3] ~= '-1' and tonumber(status) ~= tonumber(ARGV[3]) then return reject_or_replay() end
if ARGV[4] ~= '' and tostring(updatedAt) ~= ARGV[4] then return reject_or_replay() end
-- All types and payloads were validated before the first write. Redis Lua serializes these writes
-- as one invocation; deterministic WRONGTYPE/JSON failures therefore cannot leave partial effects.
if ARGV[6] == 'set' then redis.call('SET', KEYS[2], ARGV[7])
elseif ARGV[6] == 'clear' then redis.call('DEL', KEYS[2]) end
redis.call('SET', KEYS[1], ARGV[5])
if terminal then redis.call('HSET', KEYS[#KEYS - 1], ARGV[16], ARGV[17]) end
if hasSlot then
  local entityId = obj['Id'] or obj['id']
  if entityId and redis.call('GET', KEYS[#KEYS - 2]) == tostring(entityId) then
    redis.call('DEL', KEYS[#KEYS - 2])
  end
end
if enqueue then
  redis.call('HSET', KEYS[3], ARGV[11], ARGV[10])
  redis.call('ZADD', KEYS[4], ARGV[13], ARGV[11])
end
return ARGV[5]
