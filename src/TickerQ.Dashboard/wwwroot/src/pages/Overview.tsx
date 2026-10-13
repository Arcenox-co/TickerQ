import { useMemo } from "react";
import {
  Activity,
  CheckCircle2,
  Clock,
  Code2,
  Play,
  RotateCcw,
  Square,
  Timer,
  XCircle,
  Zap,
} from "lucide-react";
import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from "recharts";
import { DataCard, DataCardHeader } from "@/components/cron/PageHeader";
import { MetricTile } from "@/components/cron/MetricTile";
import { HealthDot, PriorityBadge, StatusBadge, TypeBadge } from "@/components/cron/StatusBadges";
import {
  ExecutionVolumeChart,
  bucketsToVolume,
  emptyVolumeData,
} from "@/components/cron/ExecutionVolumeChart";
import { Button } from "@/components/ui/button";
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
  useHostStatus,
  useOverallStatuses,
  useRecentActivity,
  useRestartHost,
  useStartHost,
  useStopHost,
  useTimeTickersGraph,
  useUpcoming,
} from "@/services/hooks";
import { formatDuration } from "@/lib/cron/format";
import { TimeCell } from "@/components/cron/TimeCell";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { CHART_COLORS, CHART_TOOLTIP_STYLE } from "@/lib/cron/status-config";
import { isReadOnly } from "@/lib/runtime-config";
import type { TickerStatus } from "@/services/api-types";

const ACTIVE_STATUSES: TickerStatus[] = ["Idle", "Queued", "InProgress"];

export default function OverviewPage() {
  const hostQuery = useHostStatus();
  const overallQuery = useOverallStatuses();
  const host = hostQuery.data;
  const overall = overallQuery.data;
  const { data: upcoming } = useUpcoming(10);
  const { data: recent } = useRecentActivity(10);
  const { data: fns } = useAllFunctions();
  const { data: graph } = useTimeTickersGraph(7, 0);

  const readOnly = isReadOnly();
  const startMutation = useStartHost();
  const stopMutation = useStopHost();
  const restartMutation = useRestartHost();

  const isRunning = host?.isRunning ?? false;
  const volumeData = useMemo(
    () => (graph ? bucketsToVolume(graph) : emptyVolumeData()),
    [graph]
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
        .map((s) => ({ name: s.status, value: s.count })),
    [overall]
  );
  const totalCount = pieData.reduce((s, d) => s + d.value, 0);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <HealthDot status={isRunning ? "green" : "red"} className="h-2.5 w-2.5" />
          <div>
            <h1 className="text-lg font-semibold tracking-tight">Scheduler Host</h1>
            <div className="flex items-center gap-2 mt-0.5">
              <span className="text-[11px] text-muted-foreground">
                {isRunning
                  ? `${host?.activeThreads ?? 0} / ${host?.maxConcurrency ?? 0} threads`
                  : "Host stopped"}
              </span>
            </div>
          </div>
        </div>

        {!readOnly && (
          <div className="flex items-center gap-1.5">
            {isRunning ? (
              <>
                <Button
                  variant="outline"
                  size="sm"
                  className="h-7 text-[11px] bg-transparent border-border text-muted-foreground hover:text-foreground hover:bg-surface-2 rounded-lg"
                  disabled={restartMutation.isPending}
                  onClick={() => restartMutation.mutate()}
                >
                  <RotateCcw className="h-3 w-3 mr-1.5" /> Restart
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  className="h-7 text-[11px] bg-transparent border-status-error/30 text-status-error hover:bg-status-error/10 rounded-lg"
                  disabled={stopMutation.isPending}
                  onClick={() => stopMutation.mutate()}
                >
                  <Square className="h-3 w-3 mr-1.5" /> Stop
                </Button>
              </>
            ) : (
              <Button
                variant="outline"
                size="sm"
                className="h-7 text-[11px] bg-transparent border-status-healthy/30 text-status-healthy hover:bg-status-healthy/10 rounded-lg"
                disabled={startMutation.isPending}
                onClick={() => startMutation.mutate()}
              >
                <Play className="h-3 w-3 mr-1.5" /> Start
              </Button>
            )}
          </div>
        )}
      </div>

      {(hostQuery.isError || overallQuery.isError) && (
        <QueryErrorNotice
          message="Failed to load scheduler data — the values below may be stale or empty."
          onRetry={() => {
            void hostQuery.refetch();
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
          className="lg:col-span-2"
          data={volumeData}
          title="Execution Volume (7d)"
          gradientPrefix="ov"
          height={240}
        />

        <DataCard>
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
                          <Cell key={d.name} fill={CHART_COLORS[d.name] ?? "hsl(var(--muted))"} />
                        ))}
                      </Pie>
                      <Tooltip contentStyle={CHART_TOOLTIP_STYLE} />
                    </PieChart>
                  </ResponsiveContainer>
                </div>
                <div className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1.5">
                  {pieData.map((d) => (
                    <div
                      key={d.name}
                      className="flex items-center justify-between text-[11px]"
                    >
                      <div className="flex items-center gap-1.5 min-w-0">
                        <span
                          className="h-2 w-2 rounded-full shrink-0"
                          style={{ background: CHART_COLORS[d.name] }}
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
                  <TableHead className="text-[11px] text-muted-foreground/60 px-4 py-2">Lock Holder</TableHead>
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
                    <TableRow key={t.id} className="border-b border-border/30 last:border-0 hover:bg-surface-2/15">
                      <TableCell className="px-4 py-2 font-mono text-[11px]">
                        {t.functionName}
                      </TableCell>
                      <TableCell className="px-4 py-2">
                        <TypeBadge
                          type="Time"
                          chainChildCount={t.childCount}
                        />
                      </TableCell>
                      <TableCell className="px-4 py-2">
                        <TimeCell value={t.scheduledFor} />
                      </TableCell>
                      <TableCell className="px-4 py-2 font-mono text-[11px] text-muted-foreground">
                        {t.lockHolder ?? "—"}
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
                    <TableRow key={r.id} className="border-b border-border/30 last:border-0 hover:bg-surface-2/15">
                      <TableCell className="px-4 py-2 font-mono text-[11px] truncate max-w-[180px]">
                        {r.functionName}
                      </TableCell>
                      <TableCell className="px-4 py-2">
                        <TypeBadge
                          type={r.type === "CronOccurrence" ? "Cron" : "Time"}
                          chainChildCount={r.childCount}
                        />
                      </TableCell>
                      <TableCell className="px-4 py-2">
                        <StatusBadge status={r.status} />
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
