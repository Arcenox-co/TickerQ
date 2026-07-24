import { getRuntimeConfig, normalizeBasePath } from "@/lib/runtime-config";
import { tokenStore } from "@/lib/auth/token-store";
import { refresh as refreshToken } from "@/lib/auth/auth-api";
import { fireUnauthenticated } from "@/lib/auth/auth-events";
import type {
  AddCronTickerRequest,
  DashboardOptionsResponse,
  AddTickerResponse,
  AddTimeTickerChainRequest,
  AddTimeTickerChainResponse,
  AddTimeTickerRequest,
  CronOccurrenceFlatDto,
  CronOccurrenceQueryFilter,
  CronTickerFlatDto,
  CronTickerQueryFilter,
  DuplicateTimeTickerRequest,
  ExecutionFlatDto,
  ExecutionQueryFilter,
  FunctionInfoDto,
  GraphBucketDto,
  HostStatusDto,
  NextTickerDto,
  NodeDto,
  NodeJobCount,
  PaginationResult,
  StatusCount,
  TickerLogTailResponse,
  TickerRequestPayloadResponse,
  TimeTickerFlatDto,
  TimeTickerQueryFilter,
  ToggleCronTickerBody,
  UpdateCronTickerRequest,
  UpdateTimeTickerRequest,
  BulkRetryItem,
  BulkActionResponse,
} from "./api-types";

const API_BASE = "/api/dashboard";

function withBase(path: string): string {
  const root = normalizeBasePath(getRuntimeConfig().basePath);
  const prefix = root === "/" ? "" : root;
  return `${prefix}${API_BASE}${path}`;
}

class ApiError extends Error {
  readonly status: number;
  readonly statusText: string;
  constructor(status: number, statusText: string, body: string) {
    super(`${status} ${statusText}${body ? ` — ${body}` : ""}`);
    this.name = "ApiError";
    this.status = status;
    this.statusText = statusText;
  }
}

function buildHeaders(init: RequestInit): HeadersInit {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "application/json",
    ...((init.headers as Record<string, string>) ?? {}),
  };
  const token = tokenStore.get();
  if (token && !headers.Authorization) headers.Authorization = `Bearer ${token}`;
  return headers;
}

async function request<T>(
  path: string,
  init: RequestInit = {}
): Promise<T> {
  let res = await fetch(withBase(path), {
    credentials: "include",
    ...init,
    headers: buildHeaders(init),
  });

  // 401 path: try a single refresh + retry. Covers both transports — the
  // /api/auth/refresh endpoint accepts the existing token from either the
  // Authorization header (bearer) or the auth cookie (browser session). A
  // second 401, or no token to refresh in the first place, fires the
  // unauthenticated event so the AuthProvider routes the user to /login.
  if (res.status === 401) {
    let recovered = false;
    try {
      const refreshed = await refreshToken();
      if (refreshed) {
        if (refreshed.accessToken) tokenStore.set(refreshed.accessToken);
        res = await fetch(withBase(path), {
          credentials: "include",
          ...init,
          headers: buildHeaders(init),
        });
        recovered = res.ok;
      }
    } catch {
      /* refresh failed — fall through to fire unauthenticated below */
    }

    if (!recovered) {
      tokenStore.set(null);
      fireUnauthenticated();
    }
  }

  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new ApiError(res.status, res.statusText, body);
  }

  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return text ? (JSON.parse(text) as T) : (undefined as T);
}

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  return request<T>(path, { method: "GET", signal });
}

async function post<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  return request<T>(path, {
    method: "POST",
    body: body == null ? undefined : JSON.stringify(body),
    signal,
  });
}

async function patch<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  return request<T>(path, {
    method: "PATCH",
    body: body == null ? undefined : JSON.stringify(body),
    signal,
  });
}

async function del<T>(path: string, signal?: AbortSignal): Promise<T> {
  return request<T>(path, { method: "DELETE", signal });
}

// Some routes live directly under `/api/` (not `/api/dashboard/`) — e.g. /api/options.
async function rootRequest<T>(path: string, signal?: AbortSignal): Promise<T> {
  const root = normalizeBasePath(getRuntimeConfig().basePath);
  const prefix = root === "/" ? "" : root;
  const token = tokenStore.get();
  const headers: Record<string, string> = { Accept: "application/json" };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(`${prefix}${path}`, { credentials: "include", signal, headers });
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new ApiError(res.status, res.statusText, body);
  }
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return text ? (JSON.parse(text) as T) : (undefined as T);
}

// ===== Reads =====

export const dashboardApi = {
  // Scheduler options (root-level under /api/, not /api/dashboard/)
  getOptions: (signal?: AbortSignal) =>
    rootRequest<DashboardOptionsResponse>("/api/options", signal),

  // Time tickers
  queryTimeTickers: (f: TimeTickerQueryFilter, signal?: AbortSignal) =>
    post<PaginationResult<TimeTickerFlatDto>>("/time-tickers/query", f, signal),
  getTimeTicker: (id: string, signal?: AbortSignal) =>
    get<TimeTickerFlatDto>(`/time-tickers/${id}`, signal),
  getTimeTickerChildren: (id: string, signal?: AbortSignal) =>
    get<TimeTickerFlatDto[]>(`/time-tickers/${id}/children`, signal),
  getTimeTickerRequest: (id: string, signal?: AbortSignal) =>
    get<TickerRequestPayloadResponse>(`/time-tickers/${id}/request`, signal),

  // Cron tickers
  queryCronTickers: (f: CronTickerQueryFilter, signal?: AbortSignal) =>
    post<PaginationResult<CronTickerFlatDto>>("/cron-tickers/query", f, signal),
  getCronTicker: (id: string, signal?: AbortSignal) =>
    get<CronTickerFlatDto>(`/cron-tickers/${id}`, signal),

  // Cron occurrences
  queryCronOccurrences: (
    cronTickerId: string,
    f: CronOccurrenceQueryFilter,
    signal?: AbortSignal
  ) =>
    post<PaginationResult<CronOccurrenceFlatDto>>(
      `/cron-occurrences/${cronTickerId}/query`,
      f,
      signal
    ),
  getCronOccurrence: (id: string, signal?: AbortSignal) =>
    get<CronOccurrenceFlatDto>(`/cron-occurrences/${id}`, signal),
  getCronOccurrenceRequest: (id: string, signal?: AbortSignal) =>
    get<TickerRequestPayloadResponse>(`/cron-occurrences/${id}/request`, signal),

  // Executions
  queryExecutions: (f: ExecutionQueryFilter, signal?: AbortSignal) =>
    post<PaginationResult<ExecutionFlatDto>>("/executions/query", f, signal),

  // Stats
  getOverallStatuses: (signal?: AbortSignal) =>
    get<StatusCount[]>("/stats/overall-statuses", signal),
  getNodeJobs: (signal?: AbortSignal) =>
    get<NodeJobCount[]>("/stats/node-jobs", signal),

  // Overview
  getUpcoming: (count = 10, signal?: AbortSignal) =>
    get<TimeTickerFlatDto[]>(`/overview/upcoming?count=${count}`, signal),
  getRecentActivity: (count = 10, signal?: AbortSignal) =>
    get<ExecutionFlatDto[]>(`/overview/recent-activity?count=${count}`, signal),

  // Nodes / Functions
  getNodes: (signal?: AbortSignal) => get<NodeDto[]>("/nodes", signal),
  getNodeFunctions: (nodeName: string, signal?: AbortSignal) =>
    get<FunctionInfoDto[]>(
      `/nodes/${encodeURIComponent(nodeName)}/functions`,
      signal
    ),
  getAllFunctions: (signal?: AbortSignal) =>
    get<FunctionInfoDto[]>("/functions", signal),

  // Host
  getHostStatus: (signal?: AbortSignal) =>
    get<HostStatusDto>("/host/status", signal),
  getNextTicker: (signal?: AbortSignal) =>
    get<NextTickerDto>("/host/next-ticker", signal),

  // Graphs
  getTimeTickersGraph: (
    pastDays = 7,
    futureDays = 0,
    signal?: AbortSignal
  ) =>
    get<GraphBucketDto[]>(
      `/graphs/time-tickers?pastDays=${pastDays}&futureDays=${futureDays}`,
      signal
    ),
  getCronTickersGraph: (
    pastDays = 7,
    futureDays = 0,
    signal?: AbortSignal
  ) =>
    get<GraphBucketDto[]>(
      `/graphs/cron-tickers?pastDays=${pastDays}&futureDays=${futureDays}`,
      signal
    ),
  getCronTickerGraphById: (
    cronTickerId: string,
    pastDays = 7,
    futureDays = 0,
    signal?: AbortSignal
  ) =>
    get<GraphBucketDto[]>(
      `/graphs/cron-tickers/${cronTickerId}?pastDays=${pastDays}&futureDays=${futureDays}`,
      signal
    ),
  getCronOccurrencesGraph: (cronTickerId: string, signal?: AbortSignal) =>
    get<GraphBucketDto[]>(`/graphs/cron-occurrences/${cronTickerId}`, signal),

  // Logs
  getLogTail: (tickerId: string, signal?: AbortSignal) =>
    get<TickerLogTailResponse>(`/log-tail/${tickerId}`, signal),

  // ===== Writes =====

  startHost: () => post<void>("/host/start"),
  stopHost: () => post<void>("/host/stop"),
  restartHost: () => post<void>("/host/restart"),

  cancelTicker: (id: string) => post<void>(`/tickers/${id}/cancel`),

  addTimeTicker: (body: AddTimeTickerRequest) =>
    post<AddTickerResponse>("/time-tickers", body),
  deleteTimeTicker: (id: string) => del<void>(`/time-tickers/${id}`),
  bulkDeleteTimeTickers: (ids: string[]) =>
    post<BulkActionResponse>("/time-tickers/bulk-delete", { ids }),
  bulkDeleteCronTickers: (ids: string[]) =>
    post<BulkActionResponse>("/cron-tickers/bulk-delete", { ids }),
  bulkCancelTickers: (ids: string[]) =>
    post<BulkActionResponse>("/tickers/bulk-cancel", { ids }),
  bulkRetryExecutions: (items: BulkRetryItem[]) =>
    post<BulkActionResponse>("/executions/bulk-retry", { items }),
  runTimeTicker: (id: string) => post<void>(`/time-tickers/${id}/run`),
  duplicateTimeTicker: (id: string, body?: DuplicateTimeTickerRequest) =>
    post<AddTickerResponse>(`/time-tickers/${id}/duplicate`, body),
  updateTimeTicker: (id: string, body: UpdateTimeTickerRequest) =>
    patch<void>(`/time-tickers/${id}`, body),
  addTimeTickerChain: (body: AddTimeTickerChainRequest) =>
    post<AddTimeTickerChainResponse>("/time-tickers/chain", body),

  addCronTicker: (body: AddCronTickerRequest) =>
    post<AddTickerResponse>("/cron-tickers", body),
  updateCronTicker: (id: string, body: UpdateCronTickerRequest) =>
    patch<void>(`/cron-tickers/${id}`, body),
  toggleCronTicker: (id: string, body: ToggleCronTickerBody) =>
    post<void>(`/cron-tickers/${id}/toggle`, body),
  runCronTicker: (id: string) => post<void>(`/cron-tickers/${id}/run`),
  deleteCronTicker: (id: string) => del<void>(`/cron-tickers/${id}`),

  deleteCronOccurrence: (id: string) => del<void>(`/cron-occurrences/${id}`),
};

export { ApiError };
