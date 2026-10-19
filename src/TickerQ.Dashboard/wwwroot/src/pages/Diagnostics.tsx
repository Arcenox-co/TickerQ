import { Link } from "react-router-dom";
import { Activity, AlertTriangle, ArrowUpRight, CheckCircle2, RefreshCw } from "lucide-react";
import { DataCard, DataCardHeader, PageHeader } from "@/components/cron/PageHeader";
import { Detail } from "@/components/cron/Detail";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { Button } from "@/components/ui/button";
import { formatAbsolute } from "@/lib/cron/format";
import { getRuntimeHealth } from "@/lib/runtime-health";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";
import { useDashboardOptions, useHostStatus, useRuntimeDiagnostics } from "@/services/hooks";

export default function DiagnosticsPage() {
  const { timezone } = useTimezone();
  const optionsQuery = useDashboardOptions();
  const hostQuery = useHostStatus();
  const diagnosticsQuery = useRuntimeDiagnostics();
  const host = hostQuery.data;
  const diagnostics = diagnosticsQuery.data;
  const logs = diagnostics?.executionLogs;
  const persistLogs = optionsQuery.data ? !!(optionsQuery.data.executionLogs?.enabled && optionsQuery.data.executionLogs.persistToDatabase) : undefined;
  const hasError = optionsQuery.isError || hostQuery.isError || diagnosticsQuery.isError;
  const refreshing = optionsQuery.isFetching || hostQuery.isFetching || diagnosticsQuery.isFetching;
  const health = getRuntimeHealth({ host, diagnostics, persistLogs, unavailable: hasError });
  const HealthIcon = health.status === "warning" ? AlertTriangle : health.status === "healthy" ? CheckCircle2 : Activity;
  const missing = (pending: boolean) => pending ? "Loading…" : "Unavailable";
  const timestamp = (value: string | null | undefined, empty: string) => value ? formatAbsolute(value, timezone) : diagnostics ? empty : missing(diagnosticsQuery.isPending);
  const refresh = () => {
    void optionsQuery.refetch();
    void hostQuery.refetch();
    void diagnosticsQuery.refetch();
  };

  return (
    <div className="mx-auto max-w-6xl space-y-5">
      <PageHeader title="Diagnostics" description="Live scheduler and execution-log health." className="flex-wrap">
        <Button variant="outline" size="sm" onClick={refresh} disabled={refreshing}>
          <RefreshCw className={cn("mr-1.5 size-3.5", refreshing && "animate-spin")} aria-hidden="true" />
          Refresh
        </Button>
      </PageHeader>

      {hasError && <QueryErrorNotice message="Some diagnostics could not be refreshed. Any existing values below are from the last successful response." onRetry={refresh} />}

      <DataCard className={cn("p-4", health.status === "warning" && "border-status-warning/40")}>
        <div className="flex items-start gap-3" role="status">
          <HealthIcon className={cn("mt-0.5 size-4 shrink-0", health.status === "warning" ? "text-status-warning" : health.status === "healthy" ? "text-status-healthy" : "text-muted-foreground")} aria-hidden="true" />
          <div className="min-w-0 space-y-2">
            <h2 className="text-sm font-medium">{health.label}</h2>
            {health.issues.length > 0 ? (
              <ul className="list-disc space-y-2 pl-4 text-xs leading-relaxed text-muted-foreground">
                {health.issues.map(issue => <li key={issue}>{issue}</li>)}
              </ul>
            ) : (
              <p className="text-xs leading-relaxed text-muted-foreground">
                {health.status === "healthy" ? "The scheduler is running and the latest overdue-work scan succeeded. See the checks below for details." : hasError ? "Refresh to check the current state." : "Waiting for the first completed check. This does not indicate a failure."}
              </p>
            )}
          </div>
        </div>
      </DataCard>

      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 text-xs text-muted-foreground">
        <p className="min-w-0 break-all">Node: <span className="font-mono text-foreground">{diagnostics?.node || missing(diagnosticsQuery.isPending)}</span></p>
        <p>Refreshes every 5 seconds while visible</p>
      </div>

      <div className="grid items-start gap-5 xl:grid-cols-2">
        <DataCard>
          <DataCardHeader title="Scheduler" />
          <dl>
            <Detail label="State">{host ? (host.isRunning ? "Running" : "Stopped") : missing(hostQuery.isPending)}</Detail>
            <Detail label="Running attempts" hint="Function attempts currently executing, including asynchronous I/O and individual chain steps.">{host?.activeExecutions?.toLocaleString() ?? missing(hostQuery.isPending)}</Detail>
            <Detail label="Execution limit" hint="Maximum concurrent attempts on this host. Each function can also have its own limit.">{host ? host.maxConcurrency.toLocaleString() : missing(hostQuery.isPending)}</Detail>
            <Detail label="Queued attempts" hint="Admitted attempts waiting for a global execution slot.">{host?.queuedExecutions?.toLocaleString() ?? missing(hostQuery.isPending)}</Detail>
            <Detail label="Waiting for function capacity" hint="Waiting for a per-function concurrency permit. These jobs do not occupy execution slots.">{host?.waitingForFunction?.toLocaleString() ?? missing(hostQuery.isPending)}</Detail>
            <Detail label="Retry delays" hint="Jobs waiting before another attempt. These jobs do not occupy execution slots.">{host?.retryingExecutions?.toLocaleString() ?? missing(hostQuery.isPending)}</Detail>
            <Detail label="Active workflows" hint="Accepted root jobs and chains, including running, queued, and waiting work. This overlaps the other counts.">{host?.activeWorkflows?.toLocaleString() ?? missing(hostQuery.isPending)}</Detail>
          </dl>
          <div className="border-t border-border px-4 py-3">
            <Link to="/" className="inline-flex min-h-8 items-center gap-1.5 text-xs font-medium text-primary hover:underline">
              View scheduler activity <ArrowUpRight className="size-3.5" aria-hidden="true" />
            </Link>
          </div>
        </DataCard>

        <DataCard>
          <DataCardHeader title="Overdue-work scans" />
          <dl>
            <Detail label="Latest scan">{!diagnostics ? missing(diagnosticsQuery.isPending) : health.scanFailing ? <span className="text-status-warning">Failed — retrying</span> : diagnostics.lastSuccessfulFallbackScanUtc ? "Succeeded" : "Waiting for first scan"}</Detail>
            <Detail label="Last successful scan">{timestamp(diagnostics?.lastSuccessfulFallbackScanUtc, "No completed scan yet")}</Detail>
            <Detail label="Scan failures since startup">{diagnostics ? diagnostics.fallbackScanFailures.toLocaleString() : missing(diagnosticsQuery.isPending)}</Detail>
            <Detail label="Last failed scan">{timestamp(diagnostics?.lastFallbackFailureUtc, "None")}</Detail>
          </dl>
          <p className="border-t border-border px-4 py-3 text-xs leading-relaxed text-muted-foreground">Checks for work that missed its scheduled time. A past failure remains in the count after the scan recovers.</p>
        </DataCard>
      </div>

      {persistLogs && <DataCard>
        <DataCardHeader title="Database execution logs" />
        <dl className="grid md:grid-cols-2">
          <Detail label="Database log writes">{!logs ? missing(diagnosticsQuery.isPending) : logs.writesAvailable === false ? <span className="text-status-warning">Failing — check host logs</span> : logs.writesAvailable === true ? "Last write succeeded" : "Waiting for first write"}</Detail>
          <Detail label="Pending log writes" hint="Execution tails with unsaved changes. A small pending count between flushes is normal.">{logs ? `${logs.pendingWrites.toLocaleString()} tails` : missing(diagnosticsQuery.isPending)}</Detail>
          <Detail label="Last successful log flush">{timestamp(logs?.lastSuccessfulFlushUtc, "No successful flush yet")}</Detail>
          <Detail label="Last failed log write">{timestamp(logs?.lastFailureUtc, "None")}</Detail>
          <Detail label="Write failures since startup">{logs ? logs.writeFailures.toLocaleString() : missing(diagnosticsQuery.isPending)}</Detail>
          <Detail label="Unflushed tails dropped" hint="Buffers removed before all their lines were saved, due to memory or retention limits. Older saved lines may still exist. Counts reset when the host restarts.">{logs ? logs.droppedUnflushedExecutions.toLocaleString() : missing(diagnosticsQuery.isPending)}</Detail>
        </dl>
        <p className="border-t border-border px-4 py-3 text-xs leading-relaxed text-muted-foreground">Write health reflects the latest save attempt. Before the first write, it is unknown. Individual job logs are available from execution details.</p>
      </DataCard>}

      <div className="flex flex-wrap items-center justify-between gap-3 text-xs leading-relaxed text-muted-foreground">
        <p className="max-w-2xl">These are checks for the responding host, not cluster-wide totals. Counters reset when that host restarts. Timestamps use your selected display time zone.</p>
        <Link to="/about" className="inline-flex min-h-8 items-center gap-1.5 font-medium text-primary hover:underline">
          View configuration in About <ArrowUpRight className="size-3.5" aria-hidden="true" />
        </Link>
      </div>
    </div>
  );
}
