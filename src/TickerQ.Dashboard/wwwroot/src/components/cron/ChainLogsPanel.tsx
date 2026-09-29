import { useMemo, useState } from "react";
import { useQueries } from "@tanstack/react-query";
import { ChevronDown, Loader2, Terminal } from "lucide-react";
import { dashboardApi } from "@/services/dashboard-api";
import { qk, useChainTickers } from "@/services/hooks";
import {
  levelColor,
  mapLogLine,
  type LogEntry,
} from "@/components/cron/LogsPanel";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";

/**
 * Merged log tail across every ticker in a chain, ordered by timestamp, each
 * line prefixed with its `[function]`. Tail-only (no SignalR).
 */
export function ChainLogsPanel({ rootId }: { rootId: string }) {
  const { data: tickers, isLoading: treeLoading } = useChainTickers(rootId);
  const list = useMemo(() => tickers ?? [], [tickers]);

  const results = useQueries({
    queries: list.map((t) => ({
      queryKey: qk.logTail(t.id),
      queryFn: ({ signal }: { signal: AbortSignal }) =>
        dashboardApi.getLogTail(t.id, signal),
      enabled: !!t.id,
      // Live-tail any chain member that hasn't finished yet; the chain tree
      // query polls alongside, so statuses flip and polling stops per job.
      refetchInterval:
        t.status === "Idle" || t.status === "Queued" || t.status === "InProgress"
          ? 1_500
          : (false as const),
    })),
  });

  const [collapsed, setCollapsed] = useState(false);
  const { timezone } = useTimezone();

  const logs = useMemo<(LogEntry & { color: string })[]>(() => {
    const rows: { unixMs: number; entry: LogEntry & { color: string } }[] = [];
    list.forEach((t, i) => {
      const lines = results[i]?.data?.lines ?? [];
      const succeeded = t.status === "Done" || t.status === "DueDone";
      for (const ln of lines) {
        const entry = mapLogLine(ln, t.functionName, timezone);
        rows.push({
          unixMs: ln.unixMs,
          entry: { ...entry, color: levelColor(entry.level, succeeded) },
        });
      }
    });
    rows.sort((a, b) => a.unixMs - b.unixMs);
    return rows.map((r) => r.entry);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [list, results.map((r) => r.dataUpdatedAt).join(","), timezone]);

  const loading = treeLoading || results.some((r) => r.isLoading);

  return (
    <div className="rounded-lg border border-border bg-card overflow-hidden">
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
                className="flex items-start gap-2 px-1.5 py-[1px] rounded-sm hover:bg-surface-2/30"
              >
                {log.timestamp && (
                  <span className="text-muted-foreground/50 shrink-0 tabular-nums">
                    {log.timestamp}
                  </span>
                )}
                <span className={cn("flex-1 whitespace-pre-wrap break-all", log.color)}>
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
