import { ExecutionStatus } from "@/components/cron/ExecutionStatus";
import { useMemo } from "react";
import { Link } from "react-router-dom";
import {
  Activity,
  ArrowUpRight,
  CheckCircle2,
  Clock,
  Code2,
  Timer,
  XCircle,
  Zap,
} from "lucide-react";
import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from "recharts";
import { DataCard, DataCardHeader } from "@/components/cron/PageHeader";
import { SchedulerHostCard } from "@/components/cron/SchedulerHostCard";
import { MetricTile } from "@/components/cron/MetricTile";
import { LockHolder } from "@/components/cron/LockHolder";
import { PriorityBadge, TypeBadge } from "@/components/cron/StatusBadges";
import { ExecutionVolumeChart } from "@/components/cron/ExecutionVolumeChart";
import {
  bucketsToVolume,
  emptyVolumeData,
  mergeVolumeData,
} from "@/lib/cron/execution-volume";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  useAllFunctions,
  useCronTickersGraph,
  useOverallStatuses,
  useRecentActivity,
  useTimeTickersGraph,
  useUpcoming,
} from "@/services/hooks";
import { formatDuration } from "@/lib/cron/format";
import { TimeCell } from "@/components/cron/TimeCell";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import {
  CHART_COLORS,
  CHART_TOOLTIP_STYLE,
  displayStatusLabel,
} from "@/lib/cron/status-config";
import type { ExecutionType, TickerStatus } from "@/services/api-types";

const ACTIVE_STATUSES: TickerStatus[] = ["Idle", "Queued", "InProgress"];

function TickerDetailsLink({ id, type, cronTickerId, functionName }: {
  id: string;
  type: ExecutionType;
  cronTickerId?: string | null;
  functionName: string;
}) {
  const isCron = type === "CronOccurrence";
  if (isCron && !cronTickerId) return <span>{functionName}</span>;
  const label = `View ${isCron ? "cron" : "time ticker"} details for ${functionName || id}`;
  return (
    <Link
      to={isCron ? `/cron-tickers/${cronTickerId}?occurrence=${id}` : `/time-tickers?ticker=${id}`}
      title={label}
      aria-label={label}
      className="group inline-flex max-w-[200px] items-center gap-1.5 rounded-sm text-foreground transition-colors hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
    >
      <span className="truncate">{functionName || "Unnamed function"}</span>
      <ArrowUpRight className="size-3.5 shrink-0 text-muted-foreground group-hover:text-primary group-focus-visible:text-primary" aria-hidden="true" />
    </Link>
  );
}

export default function OverviewPage() {
  const overallQuery = useOverallStatuses();
  const overall = overallQuery.data;
  const { data: upcoming } = useUpcoming(10);
  const { data: recent } = useRecentActivity(10);
  const { data: fns } = useAllFunctions();
  const { data: timeGraph } = useTimeTickersGraph(7, 0);
  const { data: cronGraph } = useCronTickersGraph(7, 0);

  const volumeData = useMemo(
    () => mergeVolumeData(
      timeGraph ? bucketsToVolume(timeGraph) : emptyVolumeData(),
      cronGraph ? bucketsToVolume(cronGraph) : emptyVolumeData()
    ),
    [timeGraph, cronGraph]
  );

  const statusMap = useMemo(() => {
    const map = new Map<TickerStatus, number>();
    (overall ?? []).forEach((x) => map.set(x.status, x.count));
    return map;
  }, [overall]);

  const activeCount = ACTIVE_STATUSES.reduce(
    (s, st) => s + (statusMap.get(st) ?? 0),
    0
  );
  const completedCount =
    (statusMap.get("Done") ?? 0) + (statusMap.get("DueDone") ?? 0);
  const failedCount = statusMap.get("Failed") ?? 0;

  const avgDurationMs = useMemo(() => {
    const finished = (recent ?? []).filter((r) => r.executedAt && r.elapsedTime > 0);
    if (finished.length === 0) return null;
    return finished.reduce((s, r) => s + r.elapsedTime, 0) / finished.length;
  }, [recent]);

  const pieData = useMemo(
    () =>
      (overall ?? [])
        .filter((s) => s.count > 0)
        .map((s) => ({
          status: s.status,
          name: displayStatusLabel(s.status, true),
          value: s.count,
        })),
    [overall]
  );
  const totalCount = pieData.reduce((s, d) => s + d.value, 0);

  return (
    <div className="space-y-6">
      <SchedulerHostCard />

      {overallQuery.isError && (
        <QueryErrorNotice
          message="Failed to load execution counts — the values below may be stale or empty."
          onRetry={() => {
            void overallQuery.refetch();
          }}
        />
      )}

      {/* Metric tiles — single-process scheduler; no node fleet */}
      <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-5 gap-3">
        <MetricTile
          icon={Activity}
          label="Active Jobs"
          value={activeCount}
          iconClassName="text-status-warning"
        />
        <MetricTile
          icon={CheckCircle2}
          label="Completed"
          value={completedCount}
          iconClassName="text-status-healthy"
        />
        <MetricTile
          icon={XCircle}
          label="Failed"
          value={failedCount}
          iconClassName="text-status-error"
        />
        <MetricTile
          icon={Timer}
          label="Avg Duration"
          value={avgDurationMs != null ? formatDuration(avgDurationMs) : "—"}
          iconClassName="text-status-warning"
        />
        <MetricTile
          icon={Code2}
          label="Functions"
          value={fns?.length ?? 0}
          iconClassName="text-primary"
        />
      </div>

      {/* Chart + Status Distribution */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-3">
        <ExecutionVolumeChart
          className="min-w-0 overflow-hidden lg:col-span-2"
          data={volumeData}
          title="Execution Volume (7d)"
          gradientPrefix="ov"
          height={240}
        />

        <DataCard className="min-w-0 overflow-hidden">
          <DataCardHeader title="Status Distribution" />
          <div className="p-4">
            {pieData.length > 0 ? (
              <>
                <div className="h-[180px]">
                  <ResponsiveContainer width="100%" height="100%">
                    <PieChart>
                      <Pie
                        data={pieData}
                        dataKey="value"
                        nameKey="name"
                        cx="50%"
                        cy="50%"
                        innerRadius={45}
                        outerRadius={75}
                        paddingAngle={2}
                        strokeWidth={0}
                      >
                        {pieData.map((d) => (
                          <Cell key={d.status} fill={CHART_COLORS[d.status] ?? "hsl(var(--muted))"} />
                        ))}
                      </Pie>
                      <Tooltip contentStyle={CHART_TOOLTIP_STYLE} />
                    </PieChart>
                  </ResponsiveContainer>
                </div>
                <div className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1.5">
                  {pieData.map((d) => (
                    <div
                      key={d.status}
                      className="flex items-center justify-between text-[11px]"
                    >
                      <div className="flex items-center gap-1.5 min-w-0">
                        <span
                          className="h-2 w-2 rounded-full shrink-0"
                          style={{ background: CHART_COLORS[d.status] }}
                        />
                        <span className="truncate text-muted-foreground">{d.name}</span>
                      </div>
                      <span className="tabular-nums text-foreground">{d.value}</span>
                    </div>
                  ))}
                </div>
                <div className="text-center text-[10px] text-muted-foreground/50 mt-3 tabular-nums">
                  {totalCount} total
                </div>
              </>
            ) : (
              <div className="flex items-center justify-center h-[220px] text-[11px] text-muted-foreground/50">
                No execution data yet.
              </div>
            )}
          </div>
        </DataCard>
      </div>

      {/* Next Scheduled + Recent Executions tables */}
      <div className="grid grid-cols-1 xl:grid-cols-2 gap-3">
        <DataCard>
          <DataCardHeader title="Next Scheduled Tickers">
            <Clock className="h-3.5 w-3.5 text-muted-foreground/50" />
          </DataCardHeader>
          <div className="overflow-hidden">
            <Table>
              <TableHeader>
                <TableRow className="hover:bg-transparent bg-surface-0/30 border-b border-border">
                  <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Function</TableHead>
                  <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Type</TableHead>
                  <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Scheduled For</TableHead>
                  <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Node</TableHead>
                  <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Priority</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(upcoming ?? []).length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={5} className="h-20 text-center text-[11px] text-muted-foreground">
                      No upcoming tickers.
                    </TableCell>
                  </TableRow>
                ) : (
                  (upcoming ?? []).map((t) => (
                    <TableRow key={`${t.type ?? "TimeTicker"}:${t.id}`} className="border-b border-border/30 last:border-0 hover:bg-surface-2/15">
                      <TableCell className="px-4 py-2 font-mono text-[11px]">
                        <TickerDetailsLink id={t.id} type={t.type ?? "TimeTicker"} cronTickerId={t.cronTickerId} functionName={t.functionName} />
                      </TableCell>
                      <TableCell className="px-4 py-2">
                        <TypeBadge
                          type={t.type === "CronOccurrence" ? "Cron" : "Time"}
                          chainChildCount={t.childCount}
                        />
                      </TableCell>
                      <TableCell className="px-4 py-2">
                        <TimeCell value={t.scheduledFor} />
                      </TableCell>
                      <TableCell className="px-4 py-2 font-mono text-[11px] text-muted-foreground">
                        <LockHolder value={t.lockHolder} />
                      </TableCell>
                      <TableCell className="px-4 py-2">
                        <PriorityBadge priority={t.priority} />
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </div>
        </DataCard>

        <DataCard>
          <DataCardHeader title="Recent Executions">
            <Zap className="h-3.5 w-3.5 text-muted-foreground/50" />
          </DataCardHeader>
          <div className="overflow-hidden">
            <Table>
              <TableHeader>
                <TableRow className="hover:bg-transparent bg-surface-0/30 border-b border-border">
                  <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Function</TableHead>
                  <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Type</TableHead>
                  <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Status</TableHead>
                  <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Duration</TableHead>
                  <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Executed</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(recent ?? []).length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={5} className="h-20 text-center text-[11px] text-muted-foreground">
                      No recent executions.
                    </TableCell>
                  </TableRow>
                ) : (
                  (recent ?? []).map((r) => (
                    <TableRow key={`${r.type}:${r.id}`} className="border-b border-border/30 last:border-0 hover:bg-surface-2/15">
                      <TableCell className="px-4 py-2 font-mono text-[11px]">
                        <TickerDetailsLink id={r.id} type={r.type} cronTickerId={r.cronTickerId} functionName={r.functionName} />
                      </TableCell>
                      <TableCell className="px-4 py-2">
                        <TypeBadge
                          type={r.type === "CronOccurrence" ? "Cron" : "Time"}
                          chainChildCount={r.childCount}
                          isChain={!!r.chainRootId || r.childCount > 0}
                        />
                      </TableCell>
                      <TableCell className="px-4 py-2">
                        <ExecutionStatus status={r.status} summary={r.chainSummary} isChain={!!r.chainRootId || r.childCount > 0} displayIdleAsWaiting />
                      </TableCell>
                      <TableCell className="px-4 py-2 text-[11px] tabular-nums">
                        {r.executedAt ? formatDuration(r.elapsedTime) : "—"}
                      </TableCell>
                      <TableCell className="px-4 py-2">
                        <TimeCell value={r.executedAt} />
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </div>
        </DataCard>
      </div>
    </div>
  );
}
