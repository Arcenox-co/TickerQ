-- Atomically revive one standalone cron occurrence without inserting another occurrence.
-- KEYS: occurrence, result, authoritative cron, terminal evidence, activation metadata
-- ARGV: holder, now, leaseUntil, token, inProgress, executionTime, idle, queued,
--       done, dueDone, failed, cancelled, skipped, exact supported epoch, admission mode
local activationType = redis.call('TYPE', KEYS[5])['ok']
if ARGV[15] == 'invalid' then return nil end
if ARGV[15] ~= 'scoped' and ARGV[15] ~= 'legacy' then
  return redis.error_reply('invalid runtime admission mode')
end
if activationType ~= 'none' and activationType ~= 'hash' then
  return redis.error_reply('reconciliation activation metadata key has an incompatible Redis type')
end
if ARGV[15] == 'legacy' and activationType == 'hash' and
   redis.call('HGET', KEYS[5], 'legacyAdoptionState') then return nil end
if ARGV[15] == 'scoped' and activationType == 'none' then return nil end
if ARGV[15] == 'scoped' and activationType == 'hash' then
  local metadata = redis.call('HGETALL', KEYS[5])
  if #metadata ~= 6 then return redis.error_reply('reconciliation activation metadata is corrupt') end
  local fields = {}
  for i = 1, #metadata, 2 do fields[metadata[i]] = metadata[i + 1] end
  local phase = fields['phase']
  if not fields['epoch'] or not phase or fields['checkpoint'] == nil or
     not string.match(fields['epoch'], '^[0-9]+$') or #fields['epoch'] > 19 or
     #fields['checkpoint'] > 200 then
    return redis.error_reply('reconciliation activation metadata is corrupt')
  end
  if phase ~= '0' and phase ~= '1' and phase ~= '2' then
    return redis.error_reply('reconciliation activation metadata is corrupt')
  end
  if phase ~= '2' or fields['epoch'] ~= ARGV[14] then return nil end
end

local occurrenceJson = redis.call('GET', KEYS[1])
local cronJson = redis.call('GET', KEYS[3])
if not occurrenceJson or not cronJson then return nil end
local obj = cjson.decode(occurrenceJson)
local cron = cjson.decode(cronJson)
local occurrenceRevision = tonumber(obj['DefinitionRevision'] or obj['definitionRevision'])
local cronRevision = tonumber(cron['DefinitionRevision'] or cron['definitionRevision'])
if not occurrenceRevision or not cronRevision or occurrenceRevision ~= cronRevision then return nil end

local chainRoot = obj['ChainRootId'] or obj['chainRootId']
if chainRoot and chainRoot ~= cjson.null and tostring(chainRoot) ~= '' then return nil end
local status = tonumber(obj['Status'] or obj['status'])
local holder = obj['LockHolder'] or obj['lockHolder']
local token = obj['AcquisitionToken'] or obj['acquisitionToken']
local lease = obj['LeaseUntil'] or obj['leaseUntil']
local isTerminal = status == tonumber(ARGV[9]) or status == tonumber(ARGV[10]) or
                   status == tonumber(ARGV[11]) or status == tonumber(ARGV[12]) or
                   status == tonumber(ARGV[13])
local eligible = status == tonumber(ARGV[7]) or
  (status == tonumber(ARGV[8]) and
   (not holder or holder == cjson.null or holder == '' or holder == ARGV[1])) or
  (isTerminal and (not token or token == cjson.null or token == '') and
   (not lease or lease == cjson.null or lease == '' or tostring(lease) <= ARGV[2]))
if not eligible then return nil end

obj['ExecutionTime'] = ARGV[6]; obj['executionTime'] = nil
obj['Status'] = tonumber(ARGV[5]); obj['status'] = nil
obj['LockHolder'] = ARGV[1]; obj['lockHolder'] = nil
obj['LockedAt'] = ARGV[2]; obj['lockedAt'] = nil
obj['LeaseUntil'] = ARGV[3]; obj['leaseUntil'] = nil
obj['AcquisitionToken'] = ARGV[4]; obj['acquisitionToken'] = nil
obj['RetryCount'] = 0; obj['retryCount'] = nil
obj['ExceptionMessage'] = cjson.null; obj['exceptionMessage'] = nil
obj['SkippedReason'] = cjson.null; obj['skippedReason'] = nil
obj['ExecutedAt'] = cjson.null; obj['executedAt'] = nil
obj['ElapsedTime'] = 0; obj['elapsedTime'] = nil
obj['StaleRestartCount'] = 0; obj['staleRestartCount'] = nil
obj['ProgressPercent'] = cjson.null; obj['progressPercent'] = nil
obj['ProgressMessage'] = cjson.null; obj['progressMessage'] = nil
obj['ProgressUpdatedAt'] = cjson.null; obj['progressUpdatedAt'] = nil
obj['UpdatedAt'] = ARGV[2]; obj['updatedAt'] = nil
obj['LastNodeFinalizationIntent'] = nil; obj['lastNodeFinalizationIntent'] = nil

local id = obj['Id'] or obj['id']
if not id or id == cjson.null then return nil end
redis.call('HDEL', KEYS[4], '0:' .. string.lower(tostring(id)))
local updated = cjson.encode(obj)
updated = updated:gsub('"RetryIntervals":{}', '"RetryIntervals":[]')
redis.call('SET', KEYS[1], updated)
redis.call('DEL', KEYS[2])
return updated
