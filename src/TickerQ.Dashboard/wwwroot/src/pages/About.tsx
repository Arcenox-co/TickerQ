import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ArrowUpRight, Clock, Code2, Database, Info, RefreshCw, ScrollText } from "lucide-react";
import { DataCard, DataCardHeader, PageHeader } from "@/components/cron/PageHeader";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Detail } from "@/components/cron/Detail";
import { getRuntimeConfig } from "@/lib/runtime-config";
import { formatMinimumSchedulerWait, formatTimeSpan } from "@/lib/time-span";
import { cn } from "@/lib/utils";
import { useAllFunctions, useDashboardOptions } from "@/services/hooks";

const sections = [
  { id: "general", label: "General", icon: Info, description: "Version, host identity, and dashboard access." },
  { id: "scheduler", label: "Scheduler", icon: Clock, description: "Scheduling time zone, concurrency, timing, and recovery." },
  { id: "retention", label: "Retention", icon: Database, description: "Automatic cleanup policies for completed job history." },
  { id: "execution-logs", label: "Execution logs", icon: ScrollText, description: "Capture, storage, and limits for diagnostic log tails." },
  { id: "functions", label: "Functions", icon: Code2, description: "Search the function definitions registered on this host." },
] as const;

export default function AboutPage() {
  const config = getRuntimeConfig();
  const optionsQuery = useDashboardOptions();
  const functionsQuery = useAllFunctions();
  const options = optionsQuery.data;
  const retention = options?.retention;
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const functions = functionsQuery.data;
  const matching = (functions ?? [])
    .filter(fn => fn.functionName.toLowerCase().includes(search.trim().toLowerCase()))
    .sort((a, b) => a.functionName.localeCompare(b.functionName));
  const pageSize = 20;
  const pages = Math.max(1, Math.ceil(matching.length / pageSize));
  const currentPage = Math.min(page, pages);
  const visible = matching.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const refreshing = optionsQuery.isFetching || functionsQuery.isFetching;
  const hasError = optionsQuery.isError || functionsQuery.isError;
  const refresh = () => {
    void optionsQuery.refetch();
    void functionsQuery.refetch();
  };
  const unavailable = (pending: boolean) => pending ? "Loading…" : "Unavailable";

  const [searchParams, setSearchParams] = useSearchParams();
  const activeSection = sections.find(section => section.id === searchParams.get("section")) ?? sections[0];
  const pageRef = useRef<HTMLDivElement>(null);
  const sectionParams = (id: string) => {
    const next = new URLSearchParams(searchParams);
    next.set("section", id);
    return next;
  };

  useEffect(() => {
    // The shell owns scrolling. Also reset it for browser Back/Forward navigation.
    pageRef.current?.closest("main")?.scrollTo({ top: 0, behavior: "instant" });
  }, [activeSection.id]);

  return (
    <div ref={pageRef} className="mx-auto max-w-6xl space-y-5">
      <PageHeader title="About" description="Registered functions and configuration for this TickerQ host." className="flex-wrap">
        <Button variant="outline" size="sm" onClick={refresh} disabled={refreshing}>
          <RefreshCw className={`mr-1.5 h-3.5 w-3.5 ${refreshing ? "animate-spin" : ""}`} aria-hidden="true" />
          Refresh
        </Button>
      </PageHeader>

      {hasError && <QueryErrorNotice message="Some host information could not be refreshed. Existing values may be out of date." onRetry={refresh} />}

      <div className="grid items-start gap-5 lg:grid-cols-[11rem_minmax(0,1fr)] lg:gap-6">
        <div className="sticky top-0 z-10 -mx-1 bg-background px-1 py-2 lg:top-4 lg:mx-0 lg:p-0">
          <div className="rounded-lg border border-border bg-card p-3 lg:hidden">
            <label id="about-section-label" htmlFor="about-section-select" className="mb-2 block text-xs font-medium text-muted-foreground">About section</label>
            <Select value={activeSection.id} onValueChange={id => setSearchParams(sectionParams(id))}>
              <SelectTrigger id="about-section-select" aria-labelledby="about-section-label" className="h-10 w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent position="popper" align="start">
                {sections.map(section => <SelectItem key={section.id} value={section.id}>{section.label}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <nav aria-label="About sections" className="hidden space-y-1 rounded-lg border border-border bg-card p-2 lg:block">
            {sections.map(section => (
              <Link
                key={section.id}
                to={{ search: `?${sectionParams(section.id)}` }}
                aria-current={activeSection.id === section.id ? "page" : undefined}
                className={cn(
                  "flex min-h-10 items-center gap-2.5 rounded-lg border px-3 py-2 text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                  activeSection.id === section.id
                    ? "border-primary/20 bg-primary/10 font-medium text-foreground"
                    : "border-transparent text-muted-foreground hover:bg-accent hover:text-foreground"
                )}
              >
                <section.icon className={cn("size-4 shrink-0", activeSection.id === section.id && "text-primary")} aria-hidden="true" />
                {section.label}
              </Link>
            ))}
          </nav>
        </div>

        <section aria-labelledby="about-section-title" className="min-w-0 space-y-4">
          <div className="space-y-1">
            <h2 id="about-section-title" className="text-base font-semibold">{activeSection.label}</h2>
            <p className="text-xs leading-relaxed text-muted-foreground">{activeSection.description}</p>
          </div>

          {activeSection.id === "general" && (
            <>
              <DataCard>
                <DataCardHeader title="Host" />
                <dl>
                  <Detail label="Dashboard version">{config.version || "Unavailable"}</Detail>
                  <Detail label="Node">{options?.currentMachine || unavailable(optionsQuery.isPending)}</Detail>
                  <Detail label="Registered functions">
                    <Link to={{ search: `?${sectionParams("functions")}` }} className="inline-flex min-h-6 items-center gap-1.5 text-primary hover:underline">
                      {functions ? `${functions.length.toLocaleString()} functions` : unavailable(functionsQuery.isPending)}
                      <ArrowUpRight className="size-3.5" aria-hidden="true" />
                    </Link>
                  </Detail>
                </dl>
              </DataCard>
              <DataCard>
                <DataCardHeader title="Dashboard" />
                <dl>
                  <Detail label="Your dashboard access">{config.readOnly ? "Read-only" : "Manage jobs"}</Detail>
                  <Detail label="Authentication">{config.auth ? (config.auth.enabled ? config.auth.mode : "Anonymous access") : "Unavailable"}</Detail>
                  <Detail label="Live updates configured" hint="Connection status is shown in the toolbar.">{config.realtime.enabled ? "Yes" : "No"}</Detail>
                  <Detail label="AI assistant configured">{config.assistant.enabled ? (config.assistant.model || "Yes") : "No"}</Detail>
                </dl>
                <div className="border-t border-border px-4 py-3">
                  <Link to="/license" className="inline-flex min-h-8 items-center gap-1.5 text-xs font-medium text-primary hover:underline">
                    View license and certificate details <ArrowUpRight className="size-3.5" aria-hidden="true" />
                  </Link>
                </div>
              </DataCard>
            </>
          )}

          {activeSection.id === "scheduler" && (
            <DataCard>
              <DataCardHeader title="Scheduler configuration" />
              <dl>
                <Detail label="Scheduler time zone" hint="Used to interpret cron schedules. The display time zone is selected separately in the toolbar.">{options?.schedulerTimeZone || unavailable(optionsQuery.isPending)}</Detail>
                <Detail label="Maximum concurrent executions" hint="Limits individual function attempts, including chain steps, on this host. Function limits can restrict this further.">{options?.maxConcurrency ?? unavailable(optionsQuery.isPending)}</Detail>
                <Detail label="Configured default timeout" hint="Applies per attempt unless the job overrides it. Zero or negative values disable the default; runtime timer limits still apply.">{options ? formatTimeSpan(options.defaultExecutionTimeout, "No default timeout", true) : unavailable(optionsQuery.isPending)}</Detail>
                <Detail label="Minimum scheduler wait" hint="Lower bound on the wait for the next scheduled job, not a fixed polling timer. Later jobs allow longer waits; notifications can interrupt them. Immediate dispatch is separate.">{options ? formatMinimumSchedulerWait(options.minPollingInterval) : unavailable(optionsQuery.isPending)}</Detail>
                <Detail label="Overdue-job check delay" hint="Fallback delay when no overdue work is found or a check fails. Catch-up checks can run sooner while work remains.">{options ? formatTimeSpan(options.fallbackIntervalChecker) : unavailable(optionsQuery.isPending)}</Detail>
                <Detail label="Built-in worker model" hint="Async consumers wait without holding native threads, so idle worker retirement is unnecessary.">Async workers</Detail>
                <Detail label="Stale-job recovery configured" hint="Recovery of jobs abandoned by a failed node requires a persistence provider that supports leases.">{options ? (options.staleJobRecoveryEnabled ? "Yes" : "No") : unavailable(optionsQuery.isPending)}</Detail>
              </dl>
            </DataCard>
          )}

          {activeSection.id === "retention" && (
            <DataCard>
              <DataCardHeader title="Retention policy" />
              {!retention ? (
                <p className="p-4 text-sm text-muted-foreground" role="status">{optionsQuery.isPending ? "Loading retention settings…" : "Retention settings are unavailable."}</p>
              ) : !retention.enabled ? (
                <div className="space-y-1.5 p-4">
                  <p className="text-sm font-medium">Automatic retention is not configured.</p>
                  <p className="text-sm text-muted-foreground">TickerQ's retention policy does not automatically delete job history on this host.</p>
                </div>
              ) : (
                <>
                  <dl className="grid sm:grid-cols-2 2xl:grid-cols-4">
                    {([
                      ["Succeeded", retention.deleteSucceededAfter],
                      ["Failed", retention.deleteFailedAfter],
                      ["Cancelled", retention.deleteCancelledAfter],
                      ["Skipped", retention.deleteSkippedAfter],
                    ] as const).map(([label, window]) => (
                      <div key={label} className="min-w-0 space-y-1.5 p-4">
                        <dt className="text-xs text-muted-foreground">{label}</dt>
                        <dd className="text-sm font-medium">{window == null ? "No automatic deletion" : `Eligible after ${formatTimeSpan(window)}`}</dd>
                      </div>
                    ))}
                  </dl>
                  <p className="border-t border-border px-4 py-3 text-xs leading-relaxed text-muted-foreground">
                    Age is measured from the recorded execution time. Eligible records are removed during a later sweep, within its batch limits. A time-ticker chain is kept until every node is eligible; active jobs and cron definitions are kept. These settings do not report cleanup results.
                  </p>
                  <details className="border-t border-border">
                    <summary className="cursor-pointer px-4 py-3 text-xs font-medium focus-visible:outline-2 focus-visible:outline-ring">Cleanup frequency and limits</summary>
                    <dl>
                      <Detail label="Delay between sweeps" hint="Also applies before the first cleanup sweep.">{formatTimeSpan(retention.sweepInterval)}</Detail>
                      <Detail label="Maximum per batch">{retention.batchSize.toLocaleString()} chains or occurrences</Detail>
                      <Detail label="Maximum batches per sweep" hint="Shared by time-ticker chains and cron occurrences.">{retention.maxBatchesPerSweep.toLocaleString()}</Detail>
                      <Detail label="Maximum nodes per chain" hint="Larger chains are kept intact.">{retention.maxNodesPerChain.toLocaleString()}</Detail>
                    </dl>
                  </details>
                </>
              )}
            </DataCard>
          )}

          {activeSection.id === "execution-logs" && (
            options?.executionLogs ? (
              <DataCard>
                <DataCardHeader title="Execution logs" />
                <dl>
                  <Detail label="Log storage">{!options.executionLogs.enabled ? "Disabled" : options.executionLogs.persistToDatabase ? "Database + local buffer" : "Local memory only"}</Detail>
                  <Detail label="Minimum captured level">{options.executionLogs.minimumLevel}</Detail>
                  <Detail label="Maximum tail per execution">{options.executionLogs.maxLinesPerExecution.toLocaleString()} lines · {options.executionLogs.maxBytesPerExecution / 1024} KiB before compression</Detail>
                  <Detail label="Local buffer expiry">{formatTimeSpan(options.executionLogs.memoryRetention)}</Detail>
                  {options.executionLogs.persistToDatabase && <>
                    <Detail label="Stored-log expiry">{formatTimeSpan(options.executionLogs.retention)}</Detail>
                    <Detail label="Maximum stored tails" hint="Per application; oldest tails are removed during bounded cleanup.">{options.executionLogs.maxStoredExecutions.toLocaleString()}</Detail>
                    <Detail label="Log flush interval">{formatTimeSpan(options.executionLogs.flushInterval)}</Detail>
                  </>}
                </dl>
                <p className="border-t border-border p-4 text-xs text-muted-foreground">Diagnostic tails have separate limits from job-history retention. They may be truncated or evicted; unflushed lines can be lost on a crash.</p>
              </DataCard>
            ) : (
              <DataCard>
                <p className="p-4 text-sm text-muted-foreground" role="status">{optionsQuery.isPending ? "Loading execution log settings…" : "Execution log settings are unavailable."}</p>
              </DataCard>
            )
          )}

          {activeSection.id === "functions" && (
            <DataCard>
              <DataCardHeader title="Function registry">
                {functions && <span className="text-xs tabular-nums text-muted-foreground">{functions.length.toLocaleString()} registered</span>}
              </DataCardHeader>
              <div className="border-b border-border p-4">
                <p className="mb-3 text-xs text-muted-foreground">Registered function definitions on this host. A default cron expression does not confirm that its schedule is currently enabled.</p>
                <p className="mb-3 text-xs text-muted-foreground">Execution limits apply to each function on this host, including chained jobs.</p>
                <label htmlFor="about-function-search" className="mb-1.5 block text-xs text-muted-foreground">Find a function</label>
                <Input id="about-function-search" placeholder="Search by name" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} className="max-w-sm" />
              </div>
              {!functions ? (
                <p className="p-4 text-sm text-muted-foreground" role="status">{functionsQuery.isPending ? "Loading registered functions…" : "Registered functions are unavailable."}</p>
              ) : matching.length === 0 ? (
                <p className="p-4 text-sm text-muted-foreground">{functions.length === 0 ? "No functions are registered on this host." : "No functions match your search."}</p>
              ) : (
                <>
                  <ul className="divide-y divide-border">
                    {visible.map(fn => (
                      <li key={fn.functionName} className="grid gap-3 px-4 py-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,14rem)] sm:items-center">
                        <div className="min-w-0 space-y-1">
                          <span className="block break-all font-mono text-xs font-medium">{fn.functionName}</span>
                          <span className="block break-all text-xs text-muted-foreground">{fn.cronExpression ? `Default cron: ${fn.cronExpression}` : "No default cron"}</span>
                        </div>
                        <dl className="space-y-1 text-xs">
                          <dt className="text-muted-foreground">Maximum concurrent executions</dt>
                          <dd className="font-medium tabular-nums">{fn.maxConcurrency == null ? "Unavailable" : fn.maxConcurrency <= 0 ? "No per-function limit" : fn.maxConcurrency.toLocaleString()}</dd>
                        </dl>
                      </li>
                    ))}
                  </ul>
                  <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3">
                    <p className="text-xs tabular-nums text-muted-foreground" role="status">{(currentPage - 1) * pageSize + 1}–{Math.min(currentPage * pageSize, matching.length)} of {matching.length.toLocaleString()}{search.trim() ? " matching functions" : " functions"}</p>
                    {pages > 1 && (
                      <nav aria-label="Function registry pages" className="flex items-center gap-2">
                        <Button variant="outline" size="sm" disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}>Previous</Button>
                        <span className="text-xs text-muted-foreground">{currentPage} / {pages}</span>
                        <Button variant="outline" size="sm" disabled={currentPage === pages} onClick={() => setPage(currentPage + 1)}>Next</Button>
                      </nav>
                    )}
                  </div>
                </>
              )}
            </DataCard>
          )}

          <Link to="/diagnostics" className="inline-flex min-h-8 items-center gap-1.5 text-xs font-medium text-primary hover:underline">
            View live runtime diagnostics <ArrowUpRight className="size-3.5" aria-hidden="true" />
          </Link>
        </section>
      </div>
    </div>
  );
}
