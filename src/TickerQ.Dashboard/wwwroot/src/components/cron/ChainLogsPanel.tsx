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
import { LogTailNotice } from "./LogTailNotice";

/**
 * Merged log tail across every ticker in a chain, ordered by timestamp, each
 * line prefixed with its `[function]`. Tail-only (no SignalR).
 */
export function ChainLogsPanel({ rootId }: { rootId: string }) {
  const [collapsed, setCollapsed] = useState(false);
  const client = useQueryClient();
  const { data: tickers, isLoading: treeLoading } = useChainTickers(rootId);
  const list = useMemo(() => tickers ?? [], [tickers]);

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

  const loading = treeLoading || result.isLoading;

  return (
    <div className="@container rounded-lg border border-border bg-card overflow-hidden">
      <button
        type="button"
        onClick={() => setCollapsed((c) => !c)}
        className="w-full flex items-center justify-between px-3 py-1.5 border-b border-border/50 text-[11px] text-muted-foreground"
      >
        <span className="flex items-center gap-1.5">
          <Terminal className="h-3 w-3 text-primary" />
          Chain logs
          <span className="text-muted-foreground/50">{list.length} jobs</span>
        </span>
        <ChevronDown className={cn("h-3 w-3 transition-transform", collapsed && "-rotate-90")} />
      </button>
      {!collapsed && <LogTailNotice
        data={tails.length ? { lines: [], storage: tails[0].storage,
          truncated: result.data?.displayTruncated || tails.some(t => t.truncated),
          persistenceAvailable: !tails.some(t => t.persistenceAvailable === false),
          writeHealth: result.data?.writeHealth } : undefined}
        isError={result.isError}
        onRefresh={() => { forceRefresh.current = true; void result.refetch(); }}
      />}
      {!collapsed && (
        <div className="font-mono text-[11px] leading-[20px] p-2 max-h-[320px] overflow-y-auto scrollbar-thin">
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
                className="flex flex-col @md:flex-row items-start gap-0.5 @md:gap-2 px-1.5 py-[1px] rounded-sm hover:bg-surface-2/30"
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
