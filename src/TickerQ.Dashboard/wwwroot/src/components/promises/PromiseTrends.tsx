import { useId, useState } from "react";
import { ChevronRight } from "lucide-react";
import {
  Bar, BarChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from "recharts";

import { DataCard } from "@/components/cron/PageHeader";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDuration } from "@/lib/cron/format";
import { CHART_AXIS_STYLE, CHART_COLORS, CHART_GRID_STROKE, CHART_TOOLTIP_STYLE } from "@/lib/cron/status-config";
import {
  dayLabel, mergeAnalytics, offsetMinutes, RANGES, summarize, type Range,
} from "@/lib/promise-analytics";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";
import { usePromiseAnalytics } from "@/services/hooks";
import type { PromiseAnalytics, PromiseDay, PromiseSnapshot } from "@/services/promise-types";

// Job outcomes wear the dashboard's status colors; a surface stroke keeps a 2px gap between stacked segments.
const OUTCOMES: { key: keyof PromiseDay; label: string; color: string }[] = [
  { key: "done", label: "Done", color: CHART_COLORS.Done },
  { key: "failed", label: "Failed", color: CHART_COLORS.Failed },
  { key: "stopped", label: "Cancelled or skipped", color: CHART_COLORS.Cancelled },
  { key: "pending", label: "Waiting or running", color: CHART_COLORS.Waiting },
];
const FIX_COLOR = "var(--promise-progress)";
const SURFACE = "hsl(var(--card))";
const TIME_COLOR = "hsl(var(--primary))";

function useAnalytics(id: string | null, range: Range) {
  const { timezone } = useTimezone();
  return usePromiseAnalytics(id, range, offsetMinutes(timezone));
}

const OPEN_KEY = "tickerq.promises.trendsOpen";

function readOpen(): boolean {
  try { return localStorage.getItem(OPEN_KEY) !== "0"; } catch { return true; }
}

/**
 * The page's trends above the promise table: jobs per day, run time and fixes per day as small charts in one row, for
 * every promise or one, with the range's numbers in the header. Collapsible, and remembered per browser.
 */
export function PromiseTrendsCard({ promises }: { promises: PromiseSnapshot[] }) {
  const [range, setRange] = useState<Range>(14);
  const [selected, setSelected] = useState("all");
  const [open, setOpen] = useState(readOpen);
  const query = useAnalytics(null, range);
  const list = (query.data ?? []).filter(a => selected === "all" || a.promiseId === selected);
  const analytics = mergeAnalytics(list);
  const toggle = () => {
    setOpen(!open);
    try { localStorage.setItem(OPEN_KEY, open ? "0" : "1"); } catch { /* the choice just isn't remembered */ }
  };

  return <DataCard>
    <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-2.5">
      <button type="button" onClick={toggle} aria-expanded={open}
        className="flex min-w-0 items-center gap-2 rounded-sm text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
        <ChevronRight className={cn("size-4 shrink-0 text-muted-foreground transition-transform", open && "rotate-90")} aria-hidden />
        <span className="text-sm font-medium">Trends</span>
        {analytics && <TrendLine analytics={analytics} range={range} />}
      </button>
      {open && <div className="flex flex-wrap items-center gap-2">
        <Select value={selected} onValueChange={setSelected}>
          <SelectTrigger size="sm" className="h-7 w-48 text-xs" aria-label="Promise"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all" className="text-xs">All promises</SelectItem>
            {promises.map(p => <SelectItem key={p.id} value={p.id} className="text-xs">{p.name}</SelectItem>)}
          </SelectContent>
        </Select>
        <RangePicker value={range} onChange={setRange} />
      </div>}
    </div>
    {open && (query.isError
      ? <div className="border-t p-4"><QueryErrorNotice message="Trends could not be loaded." onRetry={() => void query.refetch()} /></div>
      : !analytics
        ? <TrendsSkeleton />
        : <div className="grid gap-px border-t bg-border md:grid-cols-3 [&>*]:bg-card">
          <Panel title="Jobs per day"><JobsPerDayChart daily={analytics.daily} /></Panel>
          <Panel title="Run time" hint={list.length > 1 ? "Weighted average; p95 of the slowest promise" : undefined}>
            <RunTimeChart daily={analytics.daily} />
          </Panel>
          <Panel title="Fixes per day"><FixesPerDayChart daily={analytics.daily} /></Panel>
        </div>)}
    {open && analytics?.truncated && <p className="border-t px-4 py-2 text-[11px] text-muted-foreground">
      More jobs ran than one read counts, so the oldest days show too few. Pick a shorter range for exact numbers.
    </p>}
  </DataCard>;
}

/** The range in one line: jobs, success rate, failures and how many needed a fix. */
function TrendLine({ analytics, range }: { analytics: PromiseAnalytics; range: Range }) {
  const s = summarize(analytics);
  const fixed = s.fixes + s.autoFixes;
  return <span className="truncate text-[11px] text-muted-foreground">
    Last {range} days · <span className="tabular-nums text-foreground">{s.jobs.toLocaleString()}</span> jobs
    {s.successRate != null && <> · <span className={cn("tabular-nums", s.successRate < 90 ? "text-destructive" : "text-foreground")}>
      {s.successRate}%</span> succeeded</>}
    {s.failed > 0 && <> · <span className="tabular-nums text-destructive">{s.failed.toLocaleString()}</span> failed</>}
    {fixed > 0 && <> · <span className="tabular-nums text-foreground">{fixed.toLocaleString()}</span> from fixes</>}
    {analytics.p95Ms != null && <> · p95 <span className="tabular-nums text-foreground">{formatDuration(analytics.p95Ms)}</span></>}
  </span>;
}

function RangePicker({ value, onChange }: { value: Range; onChange: (range: Range) => void }) {
  return <div role="radiogroup" aria-label="Range" className="flex rounded-md border p-0.5">
    {RANGES.map(r => <button key={r} type="button" role="radio" aria-checked={value === r} onClick={() => onChange(r)}
      className={cn("rounded px-2 py-0.5 text-[11px] tabular-nums transition-colors",
        value === r ? "bg-primary/10 font-medium text-foreground" : "text-muted-foreground hover:text-foreground")}>
      {r}d
    </button>)}
  </div>;
}

function Panel({ title, hint, className, children }: { title: string; hint?: string; className?: string; children: React.ReactNode }) {
  return <div className={cn("min-w-0 space-y-2 px-4 py-3", className)}>
    <div>
      <h3 className="text-xs font-medium">{title}</h3>
      {hint && <p className="text-[11px] text-muted-foreground">{hint}</p>}
    </div>
    {children}
  </div>;
}

function TrendsSkeleton() {
  return <div className="grid gap-4 border-t p-4 md:grid-cols-3"><Skeleton className="h-32" /><Skeleton className="h-32" /><Skeleton className="h-32" /></div>;
}

const axis = { tick: CHART_AXIS_STYLE, axisLine: false, tickLine: false } as const;

// Automatic fixes share the fix hue and are told apart by stripes, so color is never the only cue.
const fill = (color: string, striped?: boolean) => ({
  background: striped ? `repeating-linear-gradient(135deg, ${color} 0 2px, transparent 2px 4px)` : color,
  boxShadow: striped ? `inset 0 0 0 1px ${color}` : undefined,
});

function Swatch({ color, striped }: { color: string; striped?: boolean }) {
  return <span aria-hidden className="size-2 shrink-0 rounded-[2px]" style={fill(color, striped)} />;
}

function Legend({ items }: { items: { label: string; color: string; value?: string; dashed?: boolean; striped?: boolean }[] }) {
  return <ul className="flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-muted-foreground">
    {items.map(item => <li key={item.label} className="inline-flex items-center gap-1.5">
      {item.dashed != null
        ? <span aria-hidden className="w-3 border-t-2" style={{ borderTopColor: item.color, borderTopStyle: item.dashed ? "dashed" : "solid" }} />
        : <Swatch color={item.color} striped={item.striped} />}
      {item.label}
      {item.value != null && <span className="tabular-nums text-foreground">{item.value}</span>}
    </li>)}
  </ul>;
}

export function JobsPerDayChart({ daily, height = 120 }: { daily: PromiseDay[]; height?: number }) {
  const totals = OUTCOMES.map(o => daily.reduce((sum, d) => sum + (d[o.key] as number), 0));
  return <div className="space-y-2">
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={daily} barCategoryGap="20%">
        <CartesianGrid strokeDasharray="3 3" stroke={CHART_GRID_STROKE} vertical={false} />
        <XAxis dataKey="day" tickFormatter={dayLabel} minTickGap={16} {...axis} />
        <YAxis allowDecimals={false} width={28} {...axis} />
        <Tooltip cursor={{ fill: "hsl(var(--muted))", opacity: 0.4 }} contentStyle={CHART_TOOLTIP_STYLE}
          labelFormatter={value => dayLabel(String(value))} />
        {OUTCOMES.map((o, i) => <Bar key={o.key} dataKey={o.key} name={o.label} stackId="jobs" fill={o.color} stroke={SURFACE}
          strokeWidth={1} maxBarSize={28} isAnimationActive={false} radius={i === OUTCOMES.length - 1 ? [3, 3, 0, 0] : 0} />)}
      </BarChart>
    </ResponsiveContainer>
    <Legend items={OUTCOMES.map((o, i) => ({ label: o.label, color: o.color, value: totals[i].toLocaleString() }))} />
  </div>;
}

export function RunTimeChart({ daily, height = 120 }: { daily: PromiseDay[]; height?: number }) {
  const ran = daily.some(d => d.averageMs != null);
  if (!ran) return <Empty height={height} text="No job finished in this range." />;
  return <div className="space-y-2">
    <ResponsiveContainer width="100%" height={height}>
      <LineChart data={daily}>
        <CartesianGrid strokeDasharray="3 3" stroke={CHART_GRID_STROKE} vertical={false} />
        <XAxis dataKey="day" tickFormatter={dayLabel} minTickGap={16} {...axis} />
        <YAxis width={44} tickFormatter={value => Number(value) === 0 ? "0" : formatDuration(Number(value))} {...axis} />
        <Tooltip contentStyle={CHART_TOOLTIP_STYLE} labelFormatter={value => dayLabel(String(value))}
          formatter={value => formatDuration(Number(value))} />
        <Line dataKey="averageMs" name="Average" stroke={TIME_COLOR} strokeWidth={2} dot={{ r: 2 }} connectNulls isAnimationActive={false} />
        <Line dataKey="p95Ms" name="p95" stroke={TIME_COLOR} strokeWidth={2} strokeDasharray="4 3" connectNulls
          dot={{ r: 2.5, fill: SURFACE, strokeDasharray: "" }}
          isAnimationActive={false} />
      </LineChart>
    </ResponsiveContainer>
    <Legend items={[
      { label: "Average", color: TIME_COLOR, dashed: false },
      { label: "p95 (19 in 20 jobs are faster)", color: TIME_COLOR, dashed: true },
    ]} />
  </div>;
}

function FixesPerDayChart({ daily, height = 120 }: { daily: PromiseDay[]; height?: number }) {
  const patternId = useId().replace(/:/g, "");
  const fixes = daily.reduce((sum, d) => sum + d.fixes, 0);
  const autoFixes = daily.reduce((sum, d) => sum + d.autoFixes, 0);
  if (fixes + autoFixes === 0) return <Empty height={height} text="No fixes in this range — the app scheduled everything." />;
  return <div className="space-y-2">
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={daily} barCategoryGap="20%">
        <defs>
          <pattern id={patternId} width="4" height="4" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
            <rect width="4" height="4" fill={SURFACE} />
            <rect width="2" height="4" fill={FIX_COLOR} />
          </pattern>
        </defs>
        <CartesianGrid strokeDasharray="3 3" stroke={CHART_GRID_STROKE} vertical={false} />
        <XAxis dataKey="day" tickFormatter={dayLabel} minTickGap={16} {...axis} />
        <YAxis allowDecimals={false} width={28} {...axis} />
        <Tooltip cursor={{ fill: "hsl(var(--muted))", opacity: 0.4 }} contentStyle={CHART_TOOLTIP_STYLE}
          labelFormatter={value => dayLabel(String(value))} />
        <Bar dataKey="fixes" name="By a person" stackId="fixes" fill={FIX_COLOR} stroke={SURFACE} maxBarSize={20} isAnimationActive={false} />
        <Bar dataKey="autoFixes" name="Automatic" stackId="fixes" fill={`url(#${patternId})`} stroke={FIX_COLOR}
          maxBarSize={20} radius={[3, 3, 0, 0]} isAnimationActive={false} />
      </BarChart>
    </ResponsiveContainer>
    <Legend items={[
      { label: "By a person", color: FIX_COLOR, value: fixes.toLocaleString() },
      { label: "Automatic", color: FIX_COLOR, value: autoFixes.toLocaleString(), striped: true },
    ]} />
  </div>;
}

function Empty({ height, text }: { height: number; text: string }) {
  return <div className="flex items-center justify-center rounded-md border border-dashed text-center text-[11px] text-muted-foreground"
    style={{ height }}>{text}</div>;
}
