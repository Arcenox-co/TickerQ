import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseQueryOptions,
} from "@tanstack/react-query";
import { dashboardApi } from "./dashboard-api";
import type {
  AddCronTickerRequest,
  AddTimeTickerChainRequest,
  AddTimeTickerRequest,
  BulkRetryItem,
  CronOccurrenceQueryFilter,
  CronTickerFlatDto,
  CronTickerQueryFilter,
  DuplicateTimeTickerRequest,
  ExecutionFlatDto,
  ExecutionQueryFilter,
  PaginationResult,
  TimeTickerFlatDto,
  TimeTickerQueryFilter,
  ToggleCronTickerBody,
  UpdateCronTickerRequest,
  UpdateTimeTickerRequest,
} from "./api-types";

// ===== Query keys =====
export const qk = {
  options: ["options"] as const,
  hostStatus: ["host", "status"] as const,
  nextTicker: ["host", "next-ticker"] as const,
  overallStatuses: ["stats", "overall-statuses"] as const,
  nodeJobs: ["stats", "node-jobs"] as const,
  upcoming: (count: number) => ["overview", "upcoming", count] as const,
  recent: (count: number) => ["overview", "recent", count] as const,
  nodes: ["nodes"] as const,
  functions: ["functions"] as const,
  nodeFunctions: (name: string) => ["nodes", name, "functions"] as const,
  timeTickers: (f: TimeTickerQueryFilter) => ["time-tickers", "list", f] as const,
  timeTicker: (id: string) => ["time-tickers", id] as const,
  timeTickerChildren: (id: string) => ["time-tickers", id, "children"] as const,
  cronTickers: (f: CronTickerQueryFilter) => ["cron-tickers", "list", f] as const,
  cronTicker: (id: string) => ["cron-tickers", id] as const,
  cronOccurrences: (cronId: string, f: CronOccurrenceQueryFilter) =>
    ["cron-occurrences", cronId, f] as const,
  executions: (f: ExecutionQueryFilter) => ["executions", "list", f] as const,
  graphTime: (past: number, future: number) =>
    ["graphs", "time", past, future] as const,
  graphCron: (past: number, future: number) =>
    ["graphs", "cron", past, future] as const,
  graphCronById: (id: string, past: number, future: number) =>
    ["graphs", "cron", id, past, future] as const,
  graphOccurrences: (id: string) => ["graphs", "occurrences", id] as const,
  logTail: (id: string) => ["log-tail", id] as const,
  cronOccurrence: (id: string) => ["cron-occurrences", "one", id] as const,
  timeTickerRequest: (id: string) => ["time-tickers", id, "request"] as const,
  cronOccurrenceRequest: (id: string) =>
    ["cron-occurrences", id, "request"] as const,
  chainTickers: (rootId: string) => ["chain-tickers", rootId] as const,
};

type QO<T> = Omit<UseQueryOptions<T>, "queryKey" | "queryFn">;

// ===== Reads =====

export function useDashboardOptions() {
  return useQuery({
    queryKey: qk.options,
    queryFn: ({ signal }) => dashboardApi.getOptions(signal),
    staleTime: 5 * 60_000,
  });
}

export function useHostStatus(opts?: QO<Awaited<ReturnType<typeof dashboardApi.getHostStatus>>>) {
  return useQuery({
    queryKey: qk.hostStatus,
    queryFn: ({ signal }) => dashboardApi.getHostStatus(signal),
    refetchInterval: 5_000,
    ...opts,
  });
}

export function useOverallStatuses() {
  return useQuery({
    queryKey: qk.overallStatuses,
    queryFn: ({ signal }) => dashboardApi.getOverallStatuses(signal),
  });
}

export function useUpcoming(count = 10) {
  return useQuery({
    queryKey: qk.upcoming(count),
    queryFn: ({ signal }) => dashboardApi.getUpcoming(count, signal),
  });
}

export function useRecentActivity(count = 10) {
  return useQuery({
    queryKey: qk.recent(count),
    queryFn: ({ signal }) => dashboardApi.getRecentActivity(count, signal),
  });
}

export function useAllFunctions() {
  return useQuery({
    queryKey: qk.functions,
    queryFn: ({ signal }) => dashboardApi.getAllFunctions(signal),
  });
}

export function useTimeTickers(filter: TimeTickerQueryFilter) {
  return useQuery<PaginationResult<TimeTickerFlatDto>>({
    queryKey: qk.timeTickers(filter),
    queryFn: ({ signal }) => dashboardApi.queryTimeTickers(filter, signal),
  });
}

export function useTimeTickerChildren(id: string | null) {
  return useQuery({
    queryKey: qk.timeTickerChildren(id ?? ""),
    queryFn: ({ signal }) => dashboardApi.getTimeTickerChildren(id!, signal),
    enabled: !!id,
  });
}

export function useCronTickers(filter: CronTickerQueryFilter) {
  return useQuery<PaginationResult<CronTickerFlatDto>>({
    queryKey: qk.cronTickers(filter),
    queryFn: ({ signal }) => dashboardApi.queryCronTickers(filter, signal),
  });
}

export function useCronOccurrences(
  cronTickerId: string | null,
  filter: CronOccurrenceQueryFilter
) {
  return useQuery({
    queryKey: qk.cronOccurrences(cronTickerId ?? "", filter),
    queryFn: ({ signal }) =>
      dashboardApi.queryCronOccurrences(cronTickerId!, filter, signal),
    enabled: !!cronTickerId,
  });
}

export function useExecutions(filter: ExecutionQueryFilter) {
  return useQuery<PaginationResult<ExecutionFlatDto>>({
    queryKey: qk.executions(filter),
    queryFn: ({ signal }) => dashboardApi.queryExecutions(filter, signal),
  });
}

export function useTimeTickersGraph(pastDays = 7, futureDays = 0) {
  return useQuery({
    queryKey: qk.graphTime(pastDays, futureDays),
    queryFn: ({ signal }) =>
      dashboardApi.getTimeTickersGraph(pastDays, futureDays, signal),
  });
}

export function useCronTickersGraph(pastDays = 7, futureDays = 0) {
  return useQuery({
    queryKey: qk.graphCron(pastDays, futureDays),
    queryFn: ({ signal }) =>
      dashboardApi.getCronTickersGraph(pastDays, futureDays, signal),
  });
}

/**
 * Log tail for one execution. Pass `live: true` while the ticker is still
 * pending/running — the tail then polls so captured lines stream into the
 * panel near-real-time (the dashboard has no SignalR client).
 */
export function useLogTail(tickerId: string | null, live = false) {
  return useQuery({
    queryKey: qk.logTail(tickerId ?? ""),
    queryFn: ({ signal }) => dashboardApi.getLogTail(tickerId!, signal),
    enabled: !!tickerId,
    refetchInterval: live ? 1_500 : false,
  });
}

/** Single time ticker by id, with optional polling for live status updates. */
export function useTimeTicker(id: string | null, opts?: QO<TimeTickerFlatDto>) {
  return useQuery({
    queryKey: qk.timeTicker(id ?? ""),
    queryFn: ({ signal }) => dashboardApi.getTimeTicker(id!, signal),
    enabled: !!id,
    ...opts,
  });
}

export function useCronTicker(id: string | null) {
  return useQuery({
    queryKey: qk.cronTicker(id ?? ""),
    queryFn: ({ signal }) => dashboardApi.getCronTicker(id!, signal),
    enabled: !!id,
  });
}

export function useCronOccurrence(id: string | null) {
  return useQuery({
    queryKey: qk.cronOccurrence(id ?? ""),
    queryFn: ({ signal }) => dashboardApi.getCronOccurrence(id!, signal),
    enabled: !!id,
  });
}

export function useCronTickerGraphById(
  cronTickerId: string | null,
  pastDays = 7,
  futureDays = 0
) {
  return useQuery({
    queryKey: qk.graphCronById(cronTickerId ?? "", pastDays, futureDays),
    queryFn: ({ signal }) =>
      dashboardApi.getCronTickerGraphById(cronTickerId!, pastDays, futureDays, signal),
    enabled: !!cronTickerId,
  });
}

export function useTimeTickerRequest(id: string | null, enabled = true) {
  return useQuery({
    queryKey: qk.timeTickerRequest(id ?? ""),
    queryFn: ({ signal }) => dashboardApi.getTimeTickerRequest(id!, signal),
    enabled: !!id && enabled,
  });
}

export function useCronOccurrenceRequest(id: string | null, enabled = true) {
  return useQuery({
    queryKey: qk.cronOccurrenceRequest(id ?? ""),
    queryFn: ({ signal }) => dashboardApi.getCronOccurrenceRequest(id!, signal),
    enabled: !!id && enabled,
  });
}

export function useNodes() {
  return useQuery({
    queryKey: qk.nodes,
    queryFn: ({ signal }) => dashboardApi.getNodes(signal),
  });
}

// Flattened chain tree (root + descendants) via bounded BFS. Used by the chain
// flowchart and the merged chain logs panel; parent/child structure is rebuilt
// from each ticker's parentId.
//
// Depth bound matches the chain builder's MAX_DEPTH (20) so any chain the UI
// can create also renders fully. The node cap is the real safety valve — it
// bounds request fan-out on pathological trees regardless of depth.
const CHAIN_MAX_DEPTH = 20;
const CHAIN_MAX_NODES = 500;

const ACTIVE_STATUSES = ["Idle", "Queued", "InProgress"];

export function useChainTickers(rootId: string | null) {
  return useQuery<TimeTickerFlatDto[]>({
    queryKey: qk.chainTickers(rootId ?? ""),
    enabled: !!rootId,
    // While any ticker in the chain is still pending/running, poll so statuses
    // (and the live log panels keyed off them) track the chain's progress.
    refetchInterval: (query) =>
      query.state.data?.some((t) => ACTIVE_STATUSES.includes(t.status))
        ? 2_000
        : false,
    queryFn: async ({ signal }) => {
      const root = await dashboardApi.getTimeTicker(rootId!, signal);
      const all: TimeTickerFlatDto[] = [root];
      let frontier: TimeTickerFlatDto[] = [root];
      for (
        let depth = 0;
        depth < CHAIN_MAX_DEPTH && frontier.length > 0 && all.length < CHAIN_MAX_NODES;
        depth++
      ) {
        const childLists = await Promise.all(
          frontier.map((t) => dashboardApi.getTimeTickerChildren(t.id, signal))
        );
        const next = childLists.flat();
        all.push(...next);
        frontier = next;
      }
      return all;
    },
  });
}

// ===== Mutations =====

function invalidateAll(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: ["time-tickers"] });
  qc.invalidateQueries({ queryKey: ["cron-tickers"] });
  qc.invalidateQueries({ queryKey: ["executions"] });
  qc.invalidateQueries({ queryKey: ["overview"] });
  qc.invalidateQueries({ queryKey: ["stats"] });
  qc.invalidateQueries({ queryKey: ["graphs"] });
}

export function useStartHost() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => dashboardApi.startHost(),
    onSuccess: () => qc.invalidateQueries({ queryKey: qk.hostStatus }),
  });
}
export function useStopHost() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => dashboardApi.stopHost(),
    onSuccess: () => qc.invalidateQueries({ queryKey: qk.hostStatus }),
  });
}
export function useRestartHost() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => dashboardApi.restartHost(),
    onSuccess: () => qc.invalidateQueries({ queryKey: qk.hostStatus }),
  });
}

export function useCancelTicker() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => dashboardApi.cancelTicker(id),
    onSuccess: () => invalidateAll(qc),
  });
}

export function useAddTimeTicker() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: AddTimeTickerRequest) => dashboardApi.addTimeTicker(body),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useBulkDeleteTimeTickers() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ids: string[]) => dashboardApi.bulkDeleteTimeTickers(ids),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useBulkDeleteCronTickers() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ids: string[]) => dashboardApi.bulkDeleteCronTickers(ids),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useBulkCancelTickers() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ids: string[]) => dashboardApi.bulkCancelTickers(ids),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useBulkRetryExecutions() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (items: BulkRetryItem[]) => dashboardApi.bulkRetryExecutions(items),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useDeleteTimeTicker() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => dashboardApi.deleteTimeTicker(id),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useRunTimeTicker() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => dashboardApi.runTimeTicker(id),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useDuplicateTimeTicker() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body?: DuplicateTimeTickerRequest }) =>
      dashboardApi.duplicateTimeTicker(id, body),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useUpdateTimeTicker() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateTimeTickerRequest }) =>
      dashboardApi.updateTimeTicker(id, body),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useAddTimeTickerChain() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: AddTimeTickerChainRequest) =>
      dashboardApi.addTimeTickerChain(body),
    onSuccess: () => invalidateAll(qc),
  });
}

export function useAddCronTicker() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: AddCronTickerRequest) => dashboardApi.addCronTicker(body),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useUpdateCronTicker() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateCronTickerRequest }) =>
      dashboardApi.updateCronTicker(id, body),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useToggleCronTicker() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ToggleCronTickerBody }) =>
      dashboardApi.toggleCronTicker(id, body),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useRunCronTicker() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => dashboardApi.runCronTicker(id),
    onSuccess: () => invalidateAll(qc),
  });
}
export function useDeleteCronTicker() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => dashboardApi.deleteCronTicker(id),
    onSuccess: () => invalidateAll(qc),
  });
}

export function useDeleteCronOccurrence() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => dashboardApi.deleteCronOccurrence(id),
    onSuccess: () => invalidateAll(qc),
  });
}
