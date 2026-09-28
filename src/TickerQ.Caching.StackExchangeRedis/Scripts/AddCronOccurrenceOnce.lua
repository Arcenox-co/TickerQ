-- Atomically reserve one logical recurring slot and publish or repair the committed owner.
-- Redis Lua has no rollback, so all keys, arguments and JSON are validated before mutation.
-- KEYS: definition,candidate occurrence,slot,all ids,reverse ids,pending,activation metadata,selected retention index
-- ARGV: candidate id,json,score,expected revision,occurrence key prefix,cron id,idle,queued,first terminal,
--       exact supported epoch,pending eligible,retention eligible,retention score
if #KEYS ~= 8 or #ARGV ~= 13 then return redis.error_reply('invalid recurring-slot argument count') end
local function type_ok(key, expected)
  local t = redis.call('TYPE', key)['ok']
  return t == 'none' or t == expected
end
if not type_ok(KEYS[1], 'string') or not type_ok(KEYS[2], 'string') or
   not type_ok(KEYS[3], 'string') or not type_ok(KEYS[4], 'set') or
   not type_ok(KEYS[5], 'set') or not type_ok(KEYS[6], 'zset') or
   not type_ok(KEYS[7], 'hash') or not type_ok(KEYS[8], 'zset') then
  return redis.error_reply('TickerQ recurring-slot key has an incompatible Redis type')
end
if redis.call('EXISTS', KEYS[7]) == 0 then
  -- Scoped scheduler keys fail closed before Begin. The legacy global key remains
  -- absent-compatible for queue-only producers that do not participate in activation.
  if string.find(KEYS[7], ':scope:', 1, true) then return {-3, ''} end
else
  if redis.call('HGET', KEYS[7], 'legacyAdoptionState') then return {-3, ''} end
  local metadata = redis.call('HGETALL', KEYS[7])
  if #metadata ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
  local fields = {}
  for i = 1, #metadata, 2 do fields[metadata[i]] = metadata[i + 1] end
  local phase = fields['phase']
  if not fields['epoch'] or not phase or fields['checkpoint'] == nil or
     not string.match(fields['epoch'], '^[0-9]+$') or #fields['epoch'] > 19 or #fields['checkpoint'] > 200 then
    return redis.error_reply('reconciliation activation metadata is corrupt')
  end
  if phase ~= '0' and phase ~= '1' and phase ~= '2' then return redis.error_reply('reconciliation activation metadata is corrupt') end
  if phase ~= '2' or fields['epoch'] ~= ARGV[10] then return {-3, ''} end
end
local occurrenceId = ARGV[1]
local score = tonumber(ARGV[3])
local expectedRevision = tonumber(ARGV[4])
local idle = tonumber(ARGV[7])
local queued = tonumber(ARGV[8])
local firstTerminal = tonumber(ARGV[9])
local pendingEligible = tonumber(ARGV[11])
local retentionEligible = tonumber(ARGV[12])
local retentionScore = tonumber(ARGV[13])
if occurrenceId == '' or ARGV[2] == '' or ARGV[5] == '' or ARGV[6] == '' or
   not score or score ~= score or not expectedRevision or expectedRevision <= 0 or
   not idle or not queued or not firstTerminal or
   (pendingEligible ~= 0 and pendingEligible ~= 1) or
   (retentionEligible ~= 0 and retentionEligible ~= 1) or not retentionScore then
  return redis.error_reply('invalid recurring-slot argument')
end
local okOccurrence, occurrence = pcall(cjson.decode, ARGV[2])
if not okOccurrence or type(occurrence) ~= 'table' then return redis.error_reply('invalid occurrence json') end
if string.lower(tostring(occurrence['Id'] or occurrence['id'] or '')) ~= string.lower(occurrenceId) or
   string.lower(tostring(occurrence['CronTickerId'] or occurrence['cronTickerId'] or '')) ~= string.lower(ARGV[6]) or
   tonumber(occurrence['DefinitionRevision'] or occurrence['definitionRevision'] or 0) ~= expectedRevision then
  return redis.error_reply('occurrence identity or revision does not match arguments')
end
local definitionJson = redis.call('GET', KEYS[1])
if not definitionJson then return {-2, ''} end
local okDefinition, definition = pcall(cjson.decode, definitionJson)
if not okDefinition or type(definition) ~= 'table' then return redis.error_reply('invalid cron definition json') end
if tostring(definition['TickerQLifecycle'] or '') == 'Deleting' then return {-2, ''} end
if tonumber(definition['DefinitionRevision'] or definition['definitionRevision'] or 0) ~= expectedRevision then return {-2, ''} end
-- A raw retry may not replace an existing generation. In particular, never overwrite a terminal
-- document whose result sidecar is durable evidence. Slot-owner retries use their existing owner ID
-- through the slot branch below and never need to replace the candidate key.
if redis.call('EXISTS', KEYS[2]) == 1 then return {-2, ''} end

local existing = redis.call('GET', KEYS[3])
if existing then
  local ownerKey = ARGV[5] .. existing
  if not type_ok(ownerKey, 'string') then return redis.error_reply('recurring-slot owner key has an incompatible Redis type') end
  local ownerJson = redis.call('GET', ownerKey)
  local ownerOk, owner = pcall(cjson.decode, ownerJson or '')
  local ownerStatus = ownerOk and type(owner) == 'table' and tonumber(owner['Status'] or owner['status']) or nil
  local ownerCron = ownerOk and type(owner) == 'table' and tostring(owner['CronTickerId'] or owner['cronTickerId'] or '') or ''
  local validOwner = ownerOk and type(owner) == 'table' and ownerStatus and ownerStatus < firstTerminal and
    string.lower(ownerCron) == string.lower(ARGV[6])
  if validOwner then
    redis.call('SADD', KEYS[4], existing)
    redis.call('SADD', KEYS[5], existing)
    if ownerStatus == idle or ownerStatus == queued then redis.call('ZADD', KEYS[6], score, existing) end
    return {0, existing}
  end
  -- Missing, corrupt, wrong-parent, or terminal owners are tombstones. Release only the
  -- exact owner observed so a stale cleanup can never delete a newer reservation.
  if redis.call('GET', KEYS[3]) == existing then redis.call('DEL', KEYS[3]) end
end

redis.call('SET', KEYS[2], ARGV[2])
redis.call('SET', KEYS[3], occurrenceId)
redis.call('SADD', KEYS[4], occurrenceId)
redis.call('SADD', KEYS[5], occurrenceId)
if pendingEligible == 1 then redis.call('ZADD', KEYS[6], score, occurrenceId) end
if retentionEligible == 1 then redis.call('ZADD', KEYS[8], retentionScore, occurrenceId) end
return {1, occurrenceId}
