import { useState } from "react";
import { RefreshCw } from "lucide-react";
import type { TickerLogTailResponse } from "@/services/api-types";

export function LogTailNotice({ data, isError, onRefresh }: {
  data?: TickerLogTailResponse;
  isError: boolean;
  onRefresh: () => void | Promise<unknown>;
}) {
  const [isRefreshing, setIsRefreshing] = useState(false);

  const refresh = async () => {
    if (isRefreshing) return;
    setIsRefreshing(true);
    try {
      await onRefresh();
    } finally {
      setIsRefreshing(false);
    }
  };

  return <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/50 px-3 py-2 text-xs text-muted-foreground">
    <div className="min-w-0 space-y-1">
      <p>{!data ? "Log storage information unavailable" : data.storage === "disabled" ? "Log capture is disabled." : data.storage === "database" ? "Database log tail · recent lines may still be flushing" : "Local log tail · lost on restart"}</p>
      {data?.storage === "database" && data.writeHealth?.writesAvailable === false && <p role="alert" className="text-status-warning">Log writes are failing on this node. Recent lines may not survive a restart.</p>}
      {data?.storage === "database" && (data.writeHealth?.droppedUnflushedExecutions ?? 0) > 0 && <p role="alert" className="text-status-warning">This node dropped {data!.writeHealth!.droppedUnflushedExecutions} unflushed tails since startup. See About for storage health.</p>}
      {data?.truncated && <p>Some lines were shortened or removed to keep this tail within its limits.</p>}
      {(isError || data?.persistenceAvailable === false) && <p role="alert" className="text-status-warning">Logs could not be fully loaded. Displayed lines may be incomplete.</p>}
    </div>
    <button
      type="button"
      onClick={() => { void refresh(); }}
      disabled={isRefreshing}
      aria-busy={isRefreshing}
      className="inline-flex shrink-0 items-center gap-1.5 text-primary hover:underline disabled:cursor-wait disabled:no-underline disabled:opacity-75"
    >
      <RefreshCw className={isRefreshing ? "h-3.5 w-3.5 animate-spin" : "h-3.5 w-3.5"} />
      {isRefreshing ? "Refreshing…" : "Refresh logs"}
    </button>
  </div>;
}
