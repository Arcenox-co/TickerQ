// TypeScript shapes mirroring TickerQ.Utilities.DashboardDtos.
// Kept identical to the C# classes (camelCase keys via System.Text.Json default).

export type TickerStatus =
  | "Idle"
  | "Queued"
  | "InProgress"
  | "Done"
  | "DueDone"
  | "Failed"
  | "Cancelled"
  | "Skipped";

export type RunCondition =
  | "OnSuccess"
  | "OnFailure"
  | "OnCancelled"
  | "OnFailureOrCancelled"
  | "OnAnyCompletedStatus"
  | "InProgress"
  | "OnAnyTerminalStatus"
  | "GroupOnSuccess"
  | "GroupOnFailure"
  | "GroupOnCancelled"
  | "GroupOnFailureOrCancelled"
  | "GroupOnAnyCompletedStatus"
  | "GroupOnAnyTerminalStatus";

// Matches the C# enum member names exactly (JsonStringEnumConverter):
// TimeTicker | CronOccurrence.
export type ExecutionType = "TimeTicker" | "CronOccurrence";

export interface CancelTickerCommand {
  id: string;
  type: ExecutionType;
  running: boolean;
}

export interface CancellationResponse {
  outcome: "Cancelled" | "Requested";
  affected: number;
}

/** What the stale-job watchdog does if the executing node dies mid-run. */
export type StaleAction = "Restart" | "Cancel";
export type BlockedOccurrenceBehavior = "Reject" | "Skip" | "MoveForward";
export type MissedExecutionAction = "RunOnce" | "Skip";
export type CronCatchUpMode = "Skip" | "RunLatest";
export type ScheduleWeekday = "Sunday" | "Monday" | "Tuesday" | "Wednesday" | "Thursday" | "Friday" | "Saturday";

export type TickerTaskPriority =
  | "Low"
  | "Normal"
  | "High"
  | "Critical"
  | "LongRunning";

export type NodeHealthStatus = "Healthy" | "Degraded" | "Down";

/** Server-side scheduler options, served by /api/options. Includes the
 *  scheduler's configured timezone — used as the dashboard's default. */
export interface DashboardOptionsResponse {
  maxConcurrency: number;
  /** .NET TimeSpan string ("00:01:00"). */
  /** The host machine identifier (e.g. NodeIdentifier). */
  currentMachine: string;
  lastHostExceptionMessage: string | null;
  /** IANA timezone name (e.g. "Europe/Ljubljana") configured on the scheduler. */
  schedulerTimeZone: string;
  /** Minimum calculated scheduler wait, not a fixed polling cadence. Negative → zero. */
  minPollingInterval: string;
  /** Fallback delay after an empty/failed overdue-work check. .NET TimeSpan string. */
  fallbackIntervalChecker: string;
  /** Global per-attempt execution timeout (.NET TimeSpan string). Null → no timeout. */
  defaultExecutionTimeout: string | null;
  /** Configured recovery opt-in; effective recovery also requires provider lease support. */
  staleJobRecoveryEnabled: boolean;
  /** Additional scheduler decision and recovery logs emitted through ILogger. */
  detailedLoggingEnabled: boolean;
  /** Effective historical-record retention policy. */
  retention: RetentionConfig;
  executionLogs?: {
    enabled: boolean; persistToDatabase: boolean; minimumLevel: string;
    maxLinesPerExecution: number; maxBytesPerExecution: number; maxStoredExecutions: number;
    retention: string; memoryRetention: string; flushInterval: string;
  };
}

/** Non-secret view of the job-retention maintenance settings. A null window means
 *  "retain forever" for that terminal status. Windows are .NET TimeSpan strings. */
export interface RetentionConfig {
  enabled: boolean;
  deleteSucceededAfter: string | null;
  deleteFailedAfter: string | null;
  deleteCancelledAfter: string | null;
  deleteSkippedAfter: string | null;
  sweepInterval: string;
  batchSize: number;
  maxBatchesPerSweep: number;
  maxNodesPerChain: number;
}

/** Coarse license state, mirrors TickerQ.Utilities.Licensing.TickerQLicenseStatus. */
export type LicenseStatus =
  | "NotValidated"
  | "Missing"
  | "Invalid"
  | "Active"
  | "Expiring"
  | "Expired";

/** Safe, read-only license projection served by /api/license. Mirrors the C#
 *  LicenseResponse — never carries the raw certificate, payload, signature, or key. */
export interface LicenseResponse {
  status: LicenseStatus;
  executionAllowed: boolean;
  message: string;
  actionLabel: string | null;
  actionUrl: string | null;
  workspaceId: string | null;
  workspaceName: string | null;
  plan: string | null;
  kind: string | null;
  isEvaluation: boolean;
  licenseId: string | null;
  issuedAt: string | null;
  expiresAt: string | null;
  daysRemaining: number | null;
  schemaVersion: number | null;
  algorithm: string | null;
  keyId: string | null;
  anchoredMinorLine: string | null;
}

export interface PaginationResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
  firstItemIndex: number;
  lastItemIndex: number;
}

export type ChainDeleteBehavior = "Cascade" | "Restrict";

export interface ChainExecutionSummaryDto {
  status: "Waiting" | "Running" | "Completed" | "Failed" | "Cancelled" | "Unavailable";
  totalCount: number;
  finishedCount: number;
  failedCount: number;
  cancelledCount: number;
  skippedCount: number;
}

export interface TimeTickerFlatDto {
  chainSummary?: ChainExecutionSummaryDto | null;
  type?: ExecutionType;
  cronTickerId?: string | null;
  originCronTickerId?: string | null;
  deleteBehavior?: ChainDeleteBehavior;
  id: string;
  functionName: string;
  status: TickerStatus;
  scheduledFor: string | null;
  elapsedTime: number;
  progressPercent: number | null;
  progressMessage: string | null;
  progressUpdatedAt: string | null;
  retries: number;
  retryCount: number;
  priority: TickerTaskPriority;
  createdAt: string;
  childCount: number;
  exceptionMessage: string | null;
  skippedReason: string | null;
  parentId: string | null;
  runCondition: RunCondition | null;
  executedAt: string | null;
  description: string | null;
  lockHolder: string | null;
  lockedAt: string | null;
  retryIntervalsSeconds: number[] | null;
  onStale: StaleAction;
  timeoutSeconds: number | null;
  schedulePolicyName: string | null;
  schedulePolicyVersion: number | null;
  blockedOccurrenceBehavior: BlockedOccurrenceBehavior | null;
  missedExecutionAction: MissedExecutionAction;
  maximumLatenessSeconds: number | null;
  requestedExecutionTime: string | null;
  scheduleAdjustmentReason: string | null;
}

export interface CronChainNode {
  function: string;
  description?: string | null;
  request?: string | null;
  retries: number;
  retryIntervals?: number[] | null;
  runCondition: RunCondition;
  onStale: StaleAction;
  timeoutSeconds?: number | null;
  children: CronChainNode[];
}
export interface CronChainTemplate {
  deleteBehavior: ChainDeleteBehavior;
  children: CronChainNode[];
}

export interface CronTickerFlatDto {
  lastRunChainRootId?: string | null;
  lastRunChainSummary?: ChainExecutionSummaryDto | null;
  skipIfAlreadyRunning: boolean;
  chain?: CronChainTemplate | null;
  id: string;
  functionName: string;
  expression: string;
  description: string | null;
  retries: number;
  priority: TickerTaskPriority;
  createdAt: string;
  occurrenceCount: number;
  lastRunStatus: TickerStatus | null;
  lastRunAt: string | null;
  isEnabled: boolean;
  isSystemPaused: boolean;
  retryIntervalsSeconds: number[] | null;
  onStale: StaleAction;
  timeoutSeconds: number | null;
  timeZoneId: string | null;
  schedulePolicyName: string | null;
  schedulePolicyVersion: number | null;
  blockedOccurrenceBehavior: BlockedOccurrenceBehavior | null;
  missedExecutionAction: MissedExecutionAction;
  maximumLatenessSeconds: number | null;
  catchUpMode: CronCatchUpMode;
  /** True when the cron is declared in code with SeedCronTickers. */
  isSeeded?: boolean;
}

export interface CronOccurrenceFlatDto {
  chainSummary?: ChainExecutionSummaryDto | null;
  chainRootId?: string | null;
  chainLockHolder?: string | null;
  id: string;
  cronTickerId: string;
  functionName: string;
  status: TickerStatus;
  scheduledFor: string;
  elapsedTime: number;
  progressPercent: number | null;
  progressMessage: string | null;
  progressUpdatedAt: string | null;
  retryCount: number;
  exceptionMessage: string | null;
  skippedReason: string | null;
  executedAt: string | null;
  createdAt: string;
  lockHolder: string | null;
  lockedAt: string | null;
}

export interface ExecutionFlatDto {
  chainSummary?: ChainExecutionSummaryDto | null;
  chainRootId?: string | null;
  id: string;
  type: ExecutionType;
  /** Parent cron ticker id — set for cron occurrences only. Enables "retry" without an extra lookup. */
  cronTickerId: string | null;
  functionName: string;
  status: TickerStatus;
  scheduledFor: string;
  elapsedTime: number;
  progressPercent: number | null;
  progressMessage: string | null;
  progressUpdatedAt: string | null;
  retryCount: number;
  retries: number;
  exceptionMessage: string | null;
  skippedReason: string | null;
  executedAt: string | null;
  childCount: number;
  lockHolder: string | null;
  lockedAt: string | null;
}

export interface HostStatusDto {
  isRunning: boolean;
  /** Legacy field: logical async consumers, including idle consumers; not native threads. */
  activeThreads: number;
  maxConcurrency: number;
  activeExecutions?: number;
  queuedExecutions?: number;
  activeWorkflows?: number;
  waitingForFunction?: number;
  retryingExecutions?: number;
}

export interface NextTickerDto {
  id: string | null;
  functionName: string | null;
  scheduledFor: string | null;
  type: ExecutionType;
}

export interface NodeDto {
  nodeKey: string;
  nodeName: string;
  nodeIdentifier: string;
  dashboardUrl: string | null;
  version: string;
  startedAt: string | null;
  lastHeartbeat: string | null;
  healthStatus: NodeHealthStatus;
  isRunning: boolean;
  functionCount: number;
  activeJobs: number;
  maxConcurrency: number;
  queuedJobs: number;
  activeWorkflows: number;
  waitingForFunction: number;
  retryingJobs: number;
}

export interface FunctionRequestExampleDto {
  key: string;
  summary: string | null;
  valueJson: string;
}

export interface FunctionRequestContractDto {
  typeName: string;
  mediaType: string;
  required: boolean;
  schemaDialect: string;
  schemaJson: string | null;
  fingerprint: string | null;
  examples: FunctionRequestExampleDto[];
}

export interface FunctionInfoDto {
  functionName: string;
  contractVersion: number;
  requestContract: FunctionRequestContractDto | null;
  requestType: string | null;
  requestExample: string | null;
  priority: TickerTaskPriority;
  cronExpression: string | null;
  /** Per-function execution limit. Non-positive means no limit; null means unavailable. */
  maxConcurrency: number | null;
}

export interface GraphBucketCountDto {
  status: TickerStatus;
  count: number;
}

export interface GraphBucketDto {
  date: string;
  counts: GraphBucketCountDto[];
}

export interface StatusCount {
  status: TickerStatus;
  count: number;
}

export interface NodeJobCount {
  nodeName: string;
  jobCount: number;
}

// ===== Filter request bodies =====

export interface TimeTickerQueryFilter {
  functionName?: string | null;
  statuses?: TickerStatus[] | null;
  scheduledFrom?: string | null;
  scheduledTo?: string | null;
  executedFrom?: string | null;
  executedTo?: string | null;
  parentOnly?: boolean | null;
  hasChildren?: boolean | null;
  search?: string | null;
  sortBy?: string | null;
  sortDescending?: boolean;
  pageNumber?: number;
  pageSize?: number;
}

export interface CronTickerQueryFilter {
  functionName?: string | null;
  lastRunStatuses?: TickerStatus[] | null;
  expression?: string | null;
  search?: string | null;
  sortBy?: string | null;
  sortDescending?: boolean;
  pageNumber?: number;
  pageSize?: number;
}

export interface CronOccurrenceQueryFilter {
  statuses?: TickerStatus[] | null;
  scheduledFrom?: string | null;
  scheduledTo?: string | null;
  sortBy?: string | null;
  sortDescending?: boolean;
  pageNumber?: number;
  pageSize?: number;
}

export interface ExecutionQueryFilter {
  types?: ExecutionType[] | null;
  statuses?: TickerStatus[] | null;
  functionName?: string | null;
  executedFrom?: string | null;
  executedTo?: string | null;
  search?: string | null;
  sortBy?: string | null;
  sortDescending?: boolean;
  pageNumber?: number;
  pageSize?: number;
}

// ===== Write request bodies =====

export interface AddTimeTickerRequest {
  function: string;
  executionTime?: string | null;
  description?: string | null;
  retries?: number | null;
  request?: string | null; // raw JSON string
  retryIntervalsSeconds?: number[] | null;
  onStale?: StaleAction | null;
  timeoutSeconds?: number | null;
  schedulePolicyName?: string | null;
  blockedOccurrenceBehavior?: BlockedOccurrenceBehavior | null;
  missedExecutionAction?: MissedExecutionAction | null;
  maximumLatenessSeconds?: number | null;
}

export interface UpdateTimeTickerRequest {
  executionTime?: string | null;
  description?: string | null;
  retries?: number | null;
  retryIntervalsSeconds?: number[] | null;
  request?: string | null; // raw JSON string
  function?: string | null;
  runCondition?: RunCondition | null;
  onStale?: StaleAction | null;
  timeoutSeconds?: number | null;
  schedulePolicyName?: string | null;
  replaceSchedulePolicy?: boolean;
  blockedOccurrenceBehavior?: BlockedOccurrenceBehavior | null;
  missedExecutionAction?: MissedExecutionAction | null;
  maximumLatenessSeconds?: number | null;
}

export interface DuplicateTimeTickerRequest {
  executionTime?: string | null;
}

export interface AddCronTickerRequest {
  skipIfAlreadyRunning?: boolean;
  chain?: CronChainTemplate | null;
  function: string;
  expression: string;
  description?: string | null;
  retries?: number | null;
  request?: string | null; // raw JSON string
  retryIntervalsSeconds?: number[] | null;
  isEnabled?: boolean | null;
  onStale?: StaleAction | null;
  timeoutSeconds?: number | null;
  timeZoneId?: string | null;
  schedulePolicyName?: string | null;
  missedExecutionAction?: MissedExecutionAction | null;
  maximumLatenessSeconds?: number | null;
  catchUpMode?: CronCatchUpMode | null;
}

export interface UpdateCronTickerRequest {
  skipIfAlreadyRunning?: boolean;
  chain?: CronChainTemplate | null;
  replaceChain?: boolean;
  function?: string | null;
  expression?: string | null;
  description?: string | null;
  retries?: number | null;
  retryIntervalsSeconds?: number[] | null;
  request?: string | null;
  isEnabled?: boolean | null;
  onStale?: StaleAction | null;
  timeoutSeconds?: number | null;
  timeZoneId?: string | null;
  schedulePolicyName?: string | null;
  replaceSchedulePolicy?: boolean;
  missedExecutionAction?: MissedExecutionAction | null;
  maximumLatenessSeconds?: number | null;
  catchUpMode?: CronCatchUpMode | null;
}

export interface BulkRetryItem {
  id: string;
  type: ExecutionType;
  cronTickerId?: string | null;
}

export interface BulkActionResponse {
  affected: number;
}

export interface ToggleCronTickerBody {
  isEnabled: boolean;
}

export interface TimeTickerNode {
  deleteBehavior?: ChainDeleteBehavior;
  function: string;
  description?: string | null;
  retries?: number | null;
  request?: string | null;
  retryIntervalsSeconds?: number[] | null;
  runCondition?: RunCondition | null;
  onStale?: StaleAction | null;
  timeoutSeconds?: number | null;
  children?: TimeTickerNode[];
}

export interface AddTimeTickerChainRequest {
  executionTime: string;
  root: TimeTickerNode;
  schedulePolicyName?: string | null;
  blockedOccurrenceBehavior?: BlockedOccurrenceBehavior | null;
  missedExecutionAction?: MissedExecutionAction | null;
  maximumLatenessSeconds?: number | null;
}

export type SavedTickerDefinitionKind = "Template" | "Draft";
export type SavedTickerDefinitionTarget = "TimeTicker" | "CronTicker";

export interface SavedDefinitionPayload {
  executionTime?: string | null;
  expression?: string | null;
  skipIfAlreadyRunning?: boolean | null;
  timeZoneId?: string | null;
  schedulePolicyName?: string | null;
  blockedOccurrenceBehavior?: BlockedOccurrenceBehavior | null;
  missedExecutionAction?: MissedExecutionAction | null;
  maximumLatenessSeconds?: number | null;
  catchUpMode?: CronCatchUpMode | null;
  root: TimeTickerNode;
}

export interface ScheduleTimeWindow {
  start: string;
  end: string;
}

export interface ScheduleAnnualDate {
  month: number;
  day: number;
}

export interface SchedulePolicy {
  id: string;
  name: string;
  description: string | null;
  timeZoneId: string;
  /** Flags enum serialized by System.Text.Json, e.g. "Monday, Tuesday, Wednesday". */
  allowedWeekdays: string;
  allowedTimeWindows: ScheduleTimeWindow[];
  excludedDates: string[];
  excludedAnnualDates: ScheduleAnnualDate[];
  dateProviderNames: string[];
  requiredPolicyNames: string[];
  excludedWhenPolicyAllowsNames: string[];
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
}

export type SaveSchedulePolicyRequest = Omit<SchedulePolicy,
  "id" | "createdAtUtc" | "updatedAtUtc" | "version">;

export interface SchedulePolicyPreviewItem {
  requestedAtUtc: string;
  effectiveAtUtc: string | null;
  isAllowed: boolean;
  rule: string;
  reason: string;
}

export interface SchedulePolicyUsage {
  timeTickerCount: number;
  cronTickerCount: number;
}

export interface ScheduleDateProviders {
  names: string[];
}

export interface SaveTickerDefinitionRequest {
  kind: SavedTickerDefinitionKind;
  target: SavedTickerDefinitionTarget;
  name: string;
  description?: string | null;
  includeRequestData: boolean;
  payload: SavedDefinitionPayload;
}

export interface UpdateTickerDefinitionRequest extends SaveTickerDefinitionRequest {
  version: number;
}

export interface SaveExistingTickerDefinitionRequest {
  name: string;
  description?: string | null;
  includeRequestData: boolean;
}

export interface SavedTickerDefinition {
  id: string;
  kind: SavedTickerDefinitionKind;
  target: SavedTickerDefinitionTarget;
  name: string;
  description: string | null;
  includesRequestData: boolean;
  schemaVersion: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
  activatedRootTickerId: string | null;
  activatedAtUtc: string | null;
  activationInProgress: boolean;
  payload: SavedDefinitionPayload;
}

// ===== Write response bodies =====

export interface AddTickerResponse {
  id: string;
}

export interface TestRunRequest {
  function: string;
  request: string | null;
  timeoutSeconds?: number;
}

export interface TestRunResponse {
  executionId: string;
  status: TickerStatus;
  success: boolean;
  error: string | null;
  elapsedMilliseconds: number;
  result: string | null;
  resultMediaType: string | null;
  resultContractId: string | null;
}

export interface TestRunChainStepResponse {
  executionId: string;
  parentExecutionId: string | null;
  function: string;
  description: string | null;
  status: TickerStatus;
  runCondition: RunCondition | null;
  priority: TickerTaskPriority;
  retries: number;
  retryCount: number;
  createdAtUtc: string;
  executedAtUtc: string | null;
  progressUpdatedAtUtc: string | null;
  childCount: number;
  error: string | null;
  elapsedMilliseconds: number;
  progressPercent: number | null;
  progressMessage: string | null;
  result: string | null;
  resultMediaType: string | null;
  resultContractId: string | null;
  isGroup: boolean;
}

export interface TestRunChainResponse {
  rootExecutionId: string;
  success: boolean;
  completed: boolean;
  startedAtUtc: string;
  finishedAtUtc: string | null;
  error: string | null;
  steps: TestRunChainStepResponse[];
}

export interface AddTimeTickerChainResponse {
  rootId: string;
  createdCount: number;
}

export interface TickerRequestPayloadResponse {
  payload: string | null; // base64
}

export interface TickerResultPayloadResponse {
  payload: string | null; // base64
  version: number | null;
  mediaType: string | null;
  contractId: string | null;
  contractType: string | null;
}

export interface TickerLogLine {
  lifecycleEvent?: "Created" | "Queued" | "Started" | "RetryScheduled" | "Completed" | "ChainLaunched" | null;
  status?: TickerStatus | null;
  attempt?: number;
  id?: string;
  unixMs: number;
  level: number;
  source: string;
  message: string;
  category: string;
  functionName: string;
}

export interface TickerLogTailResponse {
  lines: TickerLogLine[];
  storage?: "memory" | "database" | "disabled";
  truncated?: boolean;
  persistenceAvailable?: boolean;
  writeHealth?: ExecutionLogHealth;
}

export interface ExecutionLogHealth {
  writesAvailable: boolean | null;
  pendingWrites: number;
  droppedUnflushedExecutions: number;
  writeFailures: number;
  lastSuccessfulFlushUtc: string | null;
  lastFailureUtc: string | null;
}
export interface RuntimeDiagnostics {
  node: string;
  lastSuccessfulFallbackScanUtc: string | null;
  lastFallbackFailureUtc: string | null;
  fallbackScanFailures: number;
  executionLogs: ExecutionLogHealth;
}
export interface TickerLogBatchItem {
  tickerId: string;
  cursor: string;
  reset: boolean;
  tail: TickerLogTailResponse;
}
