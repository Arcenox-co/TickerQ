import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip as ChartTooltip,
  XAxis,
  YAxis,
} from "recharts";
import {
  CHART_AXIS_STYLE,
  CHART_COLORS,
  CHART_GRID_STROKE,
  CHART_TOOLTIP_STYLE,
} from "@/lib/cron/status-config";
import { DataCard, DataCardHeader } from "@/components/cron/PageHeader";
import type { VolumePoint, VolumeStatusKey } from "@/lib/cron/execution-volume";

const SERIES: ReadonlyArray<{
  key: VolumeStatusKey;
  label: string;
  color: string;
  dash?: string;
}> = [
  { key: "Idle", label: "Waiting", color: CHART_COLORS.Waiting, dash: "2 3" },
  { key: "Queued", label: "Queued", color: CHART_COLORS.Queued },
  { key: "InProgress", label: "Running", color: CHART_COLORS.InProgress },
  { key: "Done", label: "Done", color: CHART_COLORS.Done },
  { key: "DueDone", label: "DueDone", color: CHART_COLORS.DueDone, dash: "4 2" },
  { key: "Failed", label: "Failed", color: CHART_COLORS.Failed },
  { key: "Cancelled", label: "Cancelled", color: CHART_COLORS.Cancelled, dash: "4 2" },
  { key: "Skipped", label: "Skipped", color: CHART_COLORS.Skipped, dash: "4 2" },
];

export function ExecutionVolumeChart({
  data,
  title = "Executions (7d)",
  gradientPrefix = "ev",
  height = 160,
  className,
}: {
  data: VolumePoint[];
  title?: string;
  gradientPrefix?: string;
  height?: number;
  className?: string;
}) {
  const idDone = `${gradientPrefix}GradDone`;
  const idFailed = `${gradientPrefix}GradFailed`;
  const idCancelled = `${gradientPrefix}GradCancelled`;
  const totals = new Map(
    SERIES.map((series) => [
      series.key,
      data.reduce((sum, point) => sum + point[series.key], 0),
    ])
  );

  return (
    <DataCard className={className}>
      <DataCardHeader title={title}>
        <span className="text-[11px] text-muted-foreground/60">Last 7 days</span>
      </DataCardHeader>
      <div className="p-4">
        <ResponsiveContainer width="100%" height={height}>
          <AreaChart data={data}>
            <defs>
              <linearGradient id={idDone} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={CHART_COLORS.Done} stopOpacity={0.25} />
                <stop offset="100%" stopColor={CHART_COLORS.Done} stopOpacity={0.02} />
              </linearGradient>
              <linearGradient id={idFailed} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={CHART_COLORS.Failed} stopOpacity={0.25} />
                <stop offset="100%" stopColor={CHART_COLORS.Failed} stopOpacity={0.02} />
              </linearGradient>
              <linearGradient id={idCancelled} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={CHART_COLORS.Cancelled} stopOpacity={0.2} />
                <stop offset="100%" stopColor={CHART_COLORS.Cancelled} stopOpacity={0.02} />
              </linearGradient>
            </defs>
            <CartesianGrid strokeDasharray="3 3" stroke={CHART_GRID_STROKE} vertical={false} />
            <XAxis dataKey="hour" tick={CHART_AXIS_STYLE} axisLine={false} tickLine={false} />
            <YAxis tick={CHART_AXIS_STYLE} axisLine={false} tickLine={false} width={28} />
            <ChartTooltip contentStyle={CHART_TOOLTIP_STYLE} />
            {SERIES.map((series) => {
              const fill = series.key === "Done"
                ? `url(#${idDone})`
                : series.key === "Failed"
                  ? `url(#${idFailed})`
                  : series.key === "Cancelled"
                    ? `url(#${idCancelled})`
                    : "none";
              return (
                <Area
                  key={series.key}
                  type="monotone"
                  dataKey={series.key}
                  name={series.label}
                  stroke={series.color}
                  fill={fill}
                  strokeWidth={series.key === "Done" || series.key === "Failed" ? 2 : 1.5}
                  strokeDasharray={series.dash}
                />
              );
            })}
          </AreaChart>
        </ResponsiveContainer>
        <div
          className="mt-3 flex flex-wrap gap-x-4 gap-y-1.5 border-t border-border/50 pt-3"
          aria-label="Execution status totals shown in the chart"
        >
          {SERIES.map((series) => (
            <div key={series.key} className="inline-flex items-center gap-1.5 text-[10px]">
              <span
                aria-hidden="true"
                className="w-3 border-t-2"
                style={{
                  borderTopColor: series.color,
                  borderTopStyle: series.dash ? "dashed" : "solid",
                }}
              />
              <span className="text-muted-foreground">{series.label}</span>
              <span className="tabular-nums text-foreground">{totals.get(series.key) ?? 0}</span>
            </div>
          ))}
        </div>
      </div>
    </DataCard>
  );
}
