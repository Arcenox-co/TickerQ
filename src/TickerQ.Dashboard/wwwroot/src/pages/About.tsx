import type { ReactNode } from "react";
import {
  Cpu,
  Clock,
  Code2,
  Database,
  Eye,
  Gauge,
  Network,
  Route as RouteIcon,
  Trash2,
} from "lucide-react";
import { DataCard, DataCardHeader, PageHeader } from "@/components/cron/PageHeader";
import { MetricTile } from "@/components/cron/MetricTile";
import { PriorityBadge } from "@/components/cron/StatusBadges";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { useAllFunctions, useDashboardOptions, useHostStatus } from "@/services/hooks";
import { getRuntimeConfig, normalizeBasePath } from "@/lib/runtime-config";
import type { FunctionInfoDto, RetentionConfig } from "@/services/api-types";

/**
 * Read-only Configuration view. Surfaces non-secret runtime information from
 * three existing sources — the injected runtime config (paths + branding),
 * the /api/options scheduler read model (concurrency, timing, retention), and
 * the registered-functions endpoint — as one coherent page. It never renders
 * credentials, signing keys, connection strings, or webhook URLs; those are
 * kept server-side and are not part of any of these read models.
 */
export default function AboutPage() {
  const optionsQuery = useDashboardOptions();
  const functionsQuery = useAllFunctions();
  const hostQuery = useHostStatus();

  const cfg = getRuntimeConfig();
  const options = optionsQuery.data;
  const functions = functionsQuery.data;
  const host = hostQuery.data;

  // Paths the dashboard talks to at runtime — mirrors how dashboard-api.ts and
  // ticker-hub.ts build their URLs (base path prefix, "" when mounted at root).
  const base = normalizeBasePath(cfg.basePath);
  const prefix = base === "/" ? "" : base;
  const apiPath = `${prefix}/api`;
  const hubPath = cfg.realtime.enabled
    ? `${prefix}/${cfg.realtime.hubPath}`
    : "Disabled";

  return (
    <div className="space-y-6">
      <PageHeader
        title="About TickerQ"
        description="Runtime configuration, scheduler settings, retention policy, and registered functions. This page is read-only."
      >
        {cfg.readOnly && (
          <span
            title="The dashboard is running in read-only mode — all mutations are disabled."
            className="inline-flex items-center gap-1 rounded-full border border-border px-2 py-0.5 text-[10px] text-muted-foreground"
          >
            <Eye className="h-3 w-3" />
            Read-only
          </span>
        )}
      </PageHeader>

      {(optionsQuery.isError || functionsQuery.isError) && (
        <QueryErrorNotice
          message="Failed to load configuration from the scheduler — some values below may be missing."
          onRetry={() => {
            void optionsQuery.refetch();
            void functionsQuery.refetch();
          }}
        />
      )}

      {/* Headline metrics */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <MetricTile
          icon={Cpu}
          label="Max Concurrency"
          value={options?.maxConcurrency ?? "—"}
          iconClassName="text-primary"
        />
        <MetricTile
          icon={Gauge}
          label="Active Threads"
          value={
            host
              ? `${host.activeThreads} / ${host.maxConcurrency}`
              : "—"
          }
          iconClassName="text-status-warning"
        />
        <MetricTile
          icon={Code2}
          label="Registered Functions"
          value={functions?.length ?? (functionsQuery.isLoading ? "…" : 0)}
          iconClassName="text-status-info"
        />
        <MetricTile
          icon={Trash2}
          label="Retention"
          value={
            options?.retention ? (options.retention.enabled ? "Active" : "Off") : "—"
          }
          iconClassName={
            options?.retention?.enabled ? "text-status-healthy" : "text-muted-foreground"
          }
        />
      </div>

      {/* Detail cards */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
        {/* Runtime paths — from the injected window.TickerQConfig */}
        <DataCard>
          <DataCardHeader title="Runtime Paths">
            <RouteIcon className="h-3.5 w-3.5 text-muted-foreground/50" />
          </DataCardHeader>
          <div className="p-4 space-y-0.5">
            <InfoRow label="Base path" value={<Mono>{base}</Mono>} />
            <InfoRow
              label="Backend domain"
              value={
                cfg.backendDomain ? (
                  <Mono>{cfg.backendDomain}</Mono>
                ) : (
                  <Muted>Same origin</Muted>
                )
              }
            />
            <InfoRow label="API path" value={<Mono>{apiPath}</Mono>} />
            <InfoRow
              label="SignalR hub"
              value={
                cfg.realtime.enabled ? <Mono>{hubPath}</Mono> : <Muted>Disabled</Muted>
              }
            />
            <InfoRow
              label="Dashboard version"
              value={cfg.version ? <Mono>v{cfg.version}</Mono> : <Muted>—</Muted>}
            />
          </div>
        </DataCard>

        {/* Scheduler & workers — from /api/options */}
        <DataCard>
          <DataCardHeader title="Scheduler & Workers">
            <Clock className="h-3.5 w-3.5 text-muted-foreground/50" />
          </DataCardHeader>
          <div className="p-4 space-y-0.5">
            {optionsQuery.isLoading ? (
              <RowSkeletons rows={7} />
            ) : (
              <>
                <InfoRow
                  label="Node identifier"
                  value={<Mono>{options?.currentMachine ?? "—"}</Mono>}
                />
                <InfoRow
                  label="Scheduler timezone"
                  value={<Mono>{options?.schedulerTimeZone ?? "—"}</Mono>}
                />
                <InfoRow
                  label="Max concurrency"
                  value={<Mono>{options?.maxConcurrency ?? "—"}</Mono>}
                />
                <InfoRow
                  label="Idle worker timeout"
                  value={<Mono>{formatTimeSpan(options?.idleWorkerTimeOut)}</Mono>}
                />
                <InfoRow
                  label="Min polling interval"
                  value={<Mono>{formatTimeSpan(options?.minPollingInterval)}</Mono>}
                />
                <InfoRow
                  label="Fallback checker"
                  value={<Mono>{formatTimeSpan(options?.fallbackIntervalChecker)}</Mono>}
                />
                <InfoRow
                  label="Default exec. timeout"
                  value={
                    options?.defaultExecutionTimeout ? (
                      <Mono>{formatTimeSpan(options.defaultExecutionTimeout)}</Mono>
                    ) : (
                      <Muted>None</Muted>
                    )
                  }
                />
                <InfoRow
                  label="Stale job recovery"
                  value={
                    <Badge variant={options?.staleJobRecoveryEnabled ? "secondary" : "outline"}>
                      {options?.staleJobRecoveryEnabled ? "Enabled" : "Disabled"}
                    </Badge>
                  }
                />
              </>
            )}
          </div>
        </DataCard>

        {/* Retention — from /api/options.retention */}
        <DataCard className="lg:col-span-2">
          <DataCardHeader title="Retention Policy">
            <Database className="h-3.5 w-3.5 text-muted-foreground/50" />
          </DataCardHeader>
          <div className="p-4">
            {optionsQuery.isLoading ? (
              <div className="space-y-2">
                <RowSkeletons rows={4} />
              </div>
            ) : !options?.retention ? (
              <Empty>Retention settings are unavailable.</Empty>
            ) : !options.retention.enabled ? (
              <Empty>
                Retention is disabled — historical terminal records are kept indefinitely.
              </Empty>
            ) : (
              <RetentionDetails retention={options.retention} />
            )}
          </div>
        </DataCard>
      </div>

      {/* Registered functions — from /api/dashboard/functions */}
      <DataCard>
        <DataCardHeader title="Registered Functions">
          <Network className="h-3.5 w-3.5 text-muted-foreground/50" />
        </DataCardHeader>
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow className="hover:bg-transparent bg-surface-0/30 border-b border-border">
                <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Function</TableHead>
                <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Priority</TableHead>
                <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Concurrency</TableHead>
                <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Cron</TableHead>
                <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Request Type</TableHead>
                <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Schema</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {functionsQuery.isLoading ? (
                Array.from({ length: 4 }).map((_, i) => (
                  <TableRow key={i} className="border-b border-border/30 last:border-0">
                    <TableCell colSpan={6} className="px-4 py-2.5">
                      <Skeleton className="h-4 w-full" />
                    </TableCell>
                  </TableRow>
                ))
              ) : (functions ?? []).length === 0 ? (
                <TableRow>
                  <TableCell
                    colSpan={6}
                    className="h-20 text-center text-[11px] text-muted-foreground"
                  >
                    No registered functions.
                  </TableCell>
                </TableRow>
              ) : (
                (functions ?? []).map((fn) => (
                  <FunctionRow key={fn.functionName} fn={fn} />
                ))
              )}
            </TableBody>
          </Table>
        </div>
      </DataCard>
    </div>
  );
}

// ===== Row / cell helpers =====

function FunctionRow({ fn }: { fn: FunctionInfoDto }) {
  const contract = fn.requestContract;
  const requestType = contract?.typeName ?? fn.requestType;
  const hasSchema = !!contract?.schemaJson;
  return (
    <TableRow className="border-b border-border/30 last:border-0 hover:bg-surface-2/15">
      <TableCell className="px-4 py-2 font-mono text-[11px]">{fn.functionName}</TableCell>
      <TableCell className="px-4 py-2">
        <PriorityBadge priority={fn.priority} />
      </TableCell>
      <TableCell className="px-4 py-2 font-mono text-[11px] text-muted-foreground">
        {fn.maxConcurrency > 0 ? fn.maxConcurrency : "Unlimited"}
      </TableCell>
      <TableCell className="px-4 py-2 font-mono text-[11px] text-muted-foreground">
        {fn.cronExpression ?? "—"}
      </TableCell>
      <TableCell className="px-4 py-2">
        {requestType ? (
          <span className="inline-flex items-center gap-1.5">
            <span className="font-mono text-[11px] truncate max-w-[220px]">{requestType}</span>
            {contract?.required && (
              <Badge variant="outline" className="text-[9px]">
                required
              </Badge>
            )}
          </span>
        ) : (
          <Muted>—</Muted>
        )}
      </TableCell>
      <TableCell className="px-4 py-2">
        {hasSchema ? (
          <Badge variant="secondary">Schema</Badge>
        ) : requestType ? (
          <Muted>No schema</Muted>
        ) : (
          <Muted>No request</Muted>
        )}
      </TableCell>
    </TableRow>
  );
}

function RetentionDetails({ retention }: { retention: RetentionConfig }) {
  const windows: { label: string; value: string | null }[] = [
    { label: "Succeeded", value: retention.deleteSucceededAfter },
    { label: "Failed", value: retention.deleteFailedAfter },
    { label: "Cancelled", value: retention.deleteCancelledAfter },
    { label: "Skipped", value: retention.deleteSkippedAfter },
  ];
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-x-8">
      <div className="space-y-0.5">
        <p className="text-[10px] font-semibold uppercase tracking-[0.1em] text-muted-foreground/50 mb-1.5">
          Delete after
        </p>
        {windows.map((w) => (
          <InfoRow
            key={w.label}
            label={w.label}
            value={
              w.value ? (
                <Mono>{formatTimeSpan(w.value)}</Mono>
              ) : (
                <Muted>Kept forever</Muted>
              )
            }
          />
        ))}
      </div>
      <div className="space-y-0.5">
        <p className="text-[10px] font-semibold uppercase tracking-[0.1em] text-muted-foreground/50 mb-1.5">
          Sweep
        </p>
        <InfoRow label="Sweep interval" value={<Mono>{formatTimeSpan(retention.sweepInterval)}</Mono>} />
        <InfoRow label="Batch size" value={<Mono>{retention.batchSize}</Mono>} />
        <InfoRow label="Max batches / sweep" value={<Mono>{retention.maxBatchesPerSweep}</Mono>} />
        <InfoRow label="Max nodes / chain" value={<Mono>{retention.maxNodesPerChain}</Mono>} />
      </div>
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 py-1.5 border-b border-border/30 last:border-0">
      <span className="text-[12px] text-muted-foreground">{label}</span>
      <span className="text-[12px] text-foreground text-right">{value}</span>
    </div>
  );
}

function RowSkeletons({ rows }: { rows: number }) {
  return (
    <>
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="flex items-center justify-between py-1.5">
          <Skeleton className="h-3.5 w-28" />
          <Skeleton className="h-3.5 w-20" />
        </div>
      ))}
    </>
  );
}

function Mono({ children }: { children: ReactNode }) {
  return <span className="font-mono text-[11px]">{children}</span>;
}

function Muted({ children }: { children: ReactNode }) {
  return <span className="text-muted-foreground">{children}</span>;
}

function Empty({ children }: { children: ReactNode }) {
  return (
    <div className="flex items-center justify-center h-16 text-[12px] text-muted-foreground/60">
      {children}
    </div>
  );
}

/**
 * Humanize a .NET TimeSpan string ("00:01:00", "1.00:00:00", "00:00:00.5000000")
 * into a compact label like "1m", "30s", "1h 30m", "1d". Falls back to the raw
 * string when it doesn't parse, and "—" for null/empty.
 */
function formatTimeSpan(value: string | null | undefined): string {
  if (!value) return "—";
  const match = /^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$/.exec(value.trim());
  if (!match) return value;
  const [, sign, d = "0", h, m, s, frac] = match;
  const days = Number(d);
  const hours = Number(h);
  const minutes = Number(m);
  const seconds = Number(s) + (frac ? Number(`0.${frac}`) : 0);
  const parts: string[] = [];
  if (days) parts.push(`${days}d`);
  if (hours) parts.push(`${hours}h`);
  if (minutes) parts.push(`${minutes}m`);
  if (seconds) parts.push(`${seconds % 1 === 0 ? seconds : seconds.toFixed(1)}s`);
  if (parts.length === 0) return "0s";
  return (sign ?? "") + parts.slice(0, 2).join(" ");
}
