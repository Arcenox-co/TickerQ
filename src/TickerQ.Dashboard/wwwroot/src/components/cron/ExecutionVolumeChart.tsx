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
import { parseUtc } from "@/lib/cron/format";
import type { GraphBucketDto } from "@/services/api-types";

export interface VolumePoint {
  hour: string;
  Done: number;
  Failed: number;
  DueDone: number;
  Cancelled: number;
  Skipped: number;
  Queued: number;
  InProgress: number;
  Idle: number;
}

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
            <Area type="monotone" dataKey="Done" stroke={CHART_COLORS.Done} fill={`url(#${idDone})`} strokeWidth={2} />
            <Area type="monotone" dataKey="Failed" stroke={CHART_COLORS.Failed} fill={`url(#${idFailed})`} strokeWidth={2} />
            <Area type="monotone" dataKey="Queued" stroke={CHART_COLORS.Queued} fill="none" strokeWidth={1.5} />
            <Area type="monotone" dataKey="InProgress" stroke={CHART_COLORS.InProgress} fill="none" strokeWidth={1.5} />
            <Area type="monotone" dataKey="Cancelled" stroke={CHART_COLORS.Cancelled} fill={`url(#${idCancelled})`} strokeWidth={1.5} strokeDasharray="4 2" />
            <Area type="monotone" dataKey="DueDone" stroke={CHART_COLORS.DueDone} fill="none" strokeWidth={1.5} strokeDasharray="4 2" />
            <Area type="monotone" dataKey="Skipped" stroke={CHART_COLORS.Skipped} fill="none" strokeWidth={1.5} strokeDasharray="4 2" />
          </AreaChart>
        </ResponsiveContainer>
      </div>
    </DataCard>
  );
}

export function bucketsToVolume(buckets: GraphBucketDto[] | undefined): VolumePoint[] {
  if (!buckets) return [];
  return buckets.map((b) => {
    const counts: Record<string, number> = {};
    for (const c of b.counts) counts[c.status] = (counts[c.status] ?? 0) + c.count;
    return {
      hour: parseUtc(b.date).toLocaleDateString(undefined, {
        month: "short",
        day: "numeric",
      }),
      Done: counts.Done ?? 0,
      Failed: counts.Failed ?? 0,
      DueDone: counts.DueDone ?? 0,
      Cancelled: counts.Cancelled ?? 0,
      Skipped: counts.Skipped ?? 0,
      Queued: counts.Queued ?? 0,
      InProgress: counts.InProgress ?? 0,
      Idle: counts.Idle ?? 0,
    };
  });
}

export function emptyVolumeData(days = 7): VolumePoint[] {
  const out: VolumePoint[] = [];
  const now = new Date();
  for (let i = days - 1; i >= 0; i--) {
    const d = new Date(now);
    d.setDate(now.getDate() - i);
    out.push({
      hour: d.toLocaleDateString(undefined, { month: "short", day: "numeric" }),
      Done: 0,
      Failed: 0,
      DueDone: 0,
      Cancelled: 0,
      Skipped: 0,
      Queued: 0,
      InProgress: 0,
      Idle: 0,
    });
  }
  return out;
}
