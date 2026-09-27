-- Recover resources owned by a dead node. Cron occurrences optionally include the authoritative
-- definition; an old revision is quarantined as Skipped rather than returned to executable Idle.
-- KEYS: entity,result side key,[authoritative Cron definition],activation metadata
-- ARGV: deadNodeId,now,idle,queued,inProgress,[skipped,stale revision reason],exact supported epoch,
--       scoped | legacy | invalid runtime admission mode
local mode = ARGV[#ARGV]
local activationType = mode == 'scoped' and redis.call('TYPE', KEYS[#KEYS])['ok'] or 'none'
if mode == 'invalid' then return nil end
if mode ~= 'scoped' and mode ~= 'legacy' then return redis.error_reply('invalid runtime admission mode') end
if activationType ~= 'none' and activationType ~= 'hash' then return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type') end
if mode == 'scoped' and activationType == 'none' then return nil end
if mode == 'scoped' and activationType == 'hash' then
  local metadata = redis.call('HGETALL', KEYS[#KEYS])
  if #metadata ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
  local fields = {}; for i = 1, #metadata, 2 do fields[metadata[i]] = metadata[i + 1] end
  local phase = fields['phase']
  if not fields['epoch'] or not phase or fields['checkpoint'] == nil or not string.match(fields['epoch'], '^[0-9]+$')
    or #fields['epoch'] > 19 or #fields['checkpoint'] > 200 or (phase ~= '0' and phase ~= '1' and phase ~= '2') then
    return redis.error_reply('reconciliation activation metadata is corrupt')
  end
  if phase ~= '2' or fields['epoch'] ~= ARGV[#ARGV - 1] then return nil end
end
local json = redis.call('GET', KEYS[1])
if not json then return nil end
local ok, obj = pcall(cjson.decode, json)
if not ok or type(obj) ~= 'table' then return nil end
local holder = obj['LockHolder'] or obj['lockHolder']
if holder ~= ARGV[1] then return nil end
local status = tonumber(obj['Status'] or obj['status'])
if status ~= tonumber(ARGV[3]) and status ~= tonumber(ARGV[4]) and status ~= tonumber(ARGV[5]) then return nil end
local staleRevision = false
if #KEYS == 4 then
  local definitionJson = redis.call('GET', KEYS[3])
  local definitionOk, definition = pcall(cjson.decode, definitionJson or '')
  local occurrenceRevision = tonumber(obj['DefinitionRevision'] or obj['definitionRevision'] or 0)
  local definitionRevision = definitionOk and type(definition) == 'table'
    and tonumber(definition['DefinitionRevision'] or definition['definitionRevision'] or 0) or nil
  staleRevision = not occurrenceRevision or not definitionRevision or occurrenceRevision ~= definitionRevision
end
obj['LockHolder'] = cjson.null; obj['lockHolder'] = nil
obj['LockedAt'] = cjson.null; obj['lockedAt'] = nil
obj['LeaseUntil'] = cjson.null; obj['leaseUntil'] = nil
obj['AcquisitionToken'] = cjson.null; obj['acquisitionToken'] = nil
local resultKey = tostring(KEYS[2])
local resultPrefix = string.match(resultKey, '^(.*:tt:)[^:]+:result$')
local function clearChildren(children)
  if type(children) ~= 'table' then return end
  for _, child in pairs(children) do
    child['LockHolder'] = cjson.null; child['lockHolder'] = nil
    child['LockedAt'] = cjson.null; child['lockedAt'] = nil
    child['LeaseUntil'] = cjson.null; child['leaseUntil'] = nil
    child['AcquisitionToken'] = cjson.null; child['acquisitionToken'] = nil
    local id = child['Id'] or child['id']
    if resultPrefix and id and id ~= cjson.null then redis.call('DEL', resultPrefix .. tostring(id) .. ':result') end
    clearChildren(child['Children'] or child['children'])
  end
end
clearChildren(obj['Children'] or obj['children'])
if staleRevision then
  obj['Status'] = tonumber(ARGV[6]); obj['status'] = nil
  obj['SkippedReason'] = ARGV[7]; obj['skippedReason'] = nil
  if obj['ExecutedAt'] == nil or obj['ExecutedAt'] == cjson.null or obj['ExecutedAt'] == '' then obj['ExecutedAt'] = ARGV[2] end
  obj['executedAt'] = nil
else
  obj['Status'] = tonumber(ARGV[3]); obj['status'] = nil
end
obj['UpdatedAt'] = ARGV[2]; obj['updatedAt'] = nil
local updated = cjson.encode(obj)
updated = updated:gsub('"Children":{}', '"Children":[]')
updated = updated:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
redis.call('SET', KEYS[1], updated)
-- Quarantine preserves result evidence; ordinary dead-node retry starts a fresh attempt.
if not staleRevision then redis.call('DEL', KEYS[2]) end
return updated
