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
  | "InProgress";

// Matches the C# enum member names exactly (JsonStringEnumConverter):
// TimeTicker | CronOccurrence.
export type ExecutionType = "TimeTicker" | "CronOccurrence";

/** What the stale-job watchdog does if the executing node dies mid-run. */
export type StaleAction = "Restart" | "Cancel";

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
  /** TimeSpan as ISO 8601 duration ("00:01:00"). */
  idleWorkerTimeOut: string;
  /** The host machine identifier (e.g. NodeIdentifier). */
  currentMachine: string;
  lastHostExceptionMessage: string | null;
  /** IANA timezone name (e.g. "Europe/Ljubljana") configured on the scheduler. */
  schedulerTimeZone: string;
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

export interface TimeTickerFlatDto {
  id: string;
  functionName: string;
  status: TickerStatus;
  scheduledFor: string | null;
  elapsedTime: number;
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
}

export interface CronTickerFlatDto {
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
}

export interface CronOccurrenceFlatDto {
  id: string;
  cronTickerId: string;
  functionName: string;
  status: TickerStatus;
  scheduledFor: string;
  elapsedTime: number;
  retryCount: number;
  exceptionMessage: string | null;
  skippedReason: string | null;
  executedAt: string | null;
  createdAt: string;
  lockHolder: string | null;
  lockedAt: string | null;
}

export interface ExecutionFlatDto {
  id: string;
  type: ExecutionType;
  /** Parent cron ticker id — set for cron occurrences only. Enables "retry" without an extra lookup. */
  cronTickerId: string | null;
  functionName: string;
  status: TickerStatus;
  scheduledFor: string;
  elapsedTime: number;
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
  activeThreads: number;
  maxConcurrency: number;
}

export interface NextTickerDto {
  id: string | null;
  functionName: string | null;
  scheduledFor: string | null;
  type: ExecutionType;
}

export interface NodeDto {
  nodeName: string;
  lastHeartbeat: string | null;
  healthStatus: NodeHealthStatus;
  functionCount: number;
  activeJobs: number;
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
}

export interface DuplicateTimeTickerRequest {
  executionTime?: string | null;
}

export interface AddCronTickerRequest {
  function: string;
  expression: string;
  description?: string | null;
  retries?: number | null;
  request?: string | null; // raw JSON string
  retryIntervalsSeconds?: number[] | null;
  isEnabled?: boolean | null;
  onStale?: StaleAction | null;
  timeoutSeconds?: number | null;
}

export interface UpdateCronTickerRequest {
  function?: string | null;
  expression?: string | null;
  description?: string | null;
  retries?: number | null;
  retryIntervalsSeconds?: number[] | null;
  request?: string | null;
  isEnabled?: boolean | null;
  onStale?: StaleAction | null;
  timeoutSeconds?: number | null;
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
}

// ===== Write response bodies =====

export interface AddTickerResponse {
  id: string;
}

export interface AddTimeTickerChainResponse {
  rootId: string;
  createdCount: number;
}

export interface TickerRequestPayloadResponse {
  payload: string | null; // base64
}

export interface TickerLogLine {
  unixMs: number;
  level: number;
  source: string;
  message: string;
  category: string;
  functionName: string;
}

export interface TickerLogTailResponse {
  lines: TickerLogLine[];
}
