import { useMemo, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, Loader2, Terminal } from "lucide-react";
import { dashboardApi } from "@/services/dashboard-api";
import { useChainTickers } from "@/services/hooks";
import {
  logColor,
  mapLogLine,
  type LogEntry,
} from "@/lib/cron/log-format";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";
import { fetchChainLogs, needsChainLogRefresh, type ChainLogState } from "@/lib/cron/chain-logs";
import { PARALLEL_GROUP_FUNCTION } from "@/lib/cron/chain-flow-layout";
import { LogTailNotice } from "./LogTailNotice";
import type { TimeTickerFlatDto } from "@/services/api-types";

/**
 * Merged log tail across every ticker in a chain, ordered by timestamp, each
 * line prefixed with its `[function]`. Tail-only (no SignalR).
 */
export function ChainLogsPanel({
  rootId,
  tickers: suppliedTickers,
  className,
  fillHeight = false,
}: {
  rootId: string;
  /** Temporary/external graph rows, used by memory-only test executions. */
  tickers?: TimeTickerFlatDto[];
  className?: string;
  fillHeight?: boolean;
}) {
  const [collapsed, setCollapsed] = useState(false);
  const client = useQueryClient();
  const { data: queriedTickers, isLoading: treeLoading } = useChainTickers(
    suppliedTickers ? null : rootId
  );
  const list = useMemo(
    () => suppliedTickers ?? queriedTickers ?? [],
    [queriedTickers, suppliedTickers]
  );
  const jobCount = useMemo(
    () => list.filter(ticker => ticker.functionName !== PARALLEL_GROUP_FUNCTION).length,
    [list]
  );

  const forceRefresh = useRef(false);
  const queryKey = ["chain-log-batch", rootId];
  const result = useQuery<ChainLogState>({
    queryKey,
    enabled: !collapsed && list.length > 0,
    queryFn: async ({ signal }) => {
      const previous = client.getQueryData<ChainLogState>(queryKey);
      const force = forceRefresh.current || !needsChainLogRefresh(list, previous);
      const data = await fetchChainLogs(list, previous,
        items => dashboardApi.getLogBatch(items, signal), force);
      forceRefresh.current = false;
      return data;
    },
    refetchInterval: query => !collapsed && needsChainLogRefresh(list, query.state.data) ? 1_500 : false,
    refetchIntervalInBackground: false,
  });
  const tails = Object.values(result.data?.entries ?? {}).map(entry => entry.tail);
  const { timezone } = useTimezone();

  const logs = useMemo<(LogEntry & { color: string })[]>(() => {
    const rows: { unixMs: number; entry: LogEntry & { color: string } }[] = [];
    list.forEach(t => {
      const lines = result.data?.entries[t.id]?.tail.lines ?? [];
      for (const ln of lines) {
        const entry = mapLogLine(ln, t.functionName, timezone);
        rows.push({
          unixMs: ln.unixMs,
          entry: { ...entry, color: logColor(entry) },
        });
      }
    });
    rows.sort((a, b) => a.unixMs - b.unixMs);
    return rows.map((r) => r.entry);
  }, [list, result.data, timezone]);

  const loading = (!suppliedTickers && treeLoading) || result.isLoading;

  return (
    <div className={cn(
      "@container rounded-lg border border-border bg-card overflow-hidden",
      fillHeight && "flex h-full min-h-0 w-full flex-col",
      className
    )}>
      <button
        type="button"
        onClick={() => setCollapsed((c) => !c)}
        className="w-full flex items-center justify-between px-3 py-1.5 border-b border-border/50 text-[11px] text-muted-foreground"
      >
        <span className="flex items-center gap-1.5">
          <Terminal className="h-3 w-3 text-primary" />
          Chain logs
          <span className="text-muted-foreground/50">{jobCount} jobs</span>
        </span>
        <ChevronDown className={cn("h-3 w-3 transition-transform", collapsed && "-rotate-90")} />
      </button>
      {!collapsed && <LogTailNotice
        data={tails.length ? { lines: [], storage: tails[0].storage,
          truncated: result.data?.displayTruncated || tails.some(t => t.truncated),
          persistenceAvailable: !tails.some(t => t.persistenceAvailable === false),
          writeHealth: result.data?.writeHealth } : undefined}
        isError={result.isError}
        onRefresh={() => {
          forceRefresh.current = true;
          return result.refetch();
        }}
      />}
      {!collapsed && (
        <div className={cn(
          "w-full font-mono text-[11px] leading-[20px] px-2 pt-2 pb-0 overflow-y-auto scrollbar-thin",
          fillHeight ? "min-h-0 flex-1" : "max-h-[320px]"
        )}>
          {loading ? (
            <div className="flex items-center gap-2 px-1.5 py-1 text-muted-foreground">
              <Loader2 className="h-3 w-3 animate-spin" /> Loading logs…
            </div>
          ) : logs.length === 0 ? (
            <div className="px-1.5 py-1 text-muted-foreground/60">No logs across the chain yet.</div>
          ) : (
            logs.map((log, i) => (
              <div
                key={i}
                className="flex w-full flex-col @md:flex-row items-start gap-0.5 @md:gap-2 px-1.5 py-[1px] rounded-sm hover:bg-surface-2/30"
              >
                {log.timestamp && (
                  <span className="text-muted-foreground/50 shrink-0 tabular-nums">
                    {log.timestamp}
                  </span>
                )}
                <span className={cn("min-w-0 flex-1 whitespace-pre-wrap break-words [overflow-wrap:anywhere]", log.color)}>
                  {log.message}
                </span>
              </div>
            ))
          )}
        </div>
      )}
    </div>
  );
}
