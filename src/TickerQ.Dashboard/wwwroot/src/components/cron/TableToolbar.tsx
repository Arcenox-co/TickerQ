import type { ReactNode } from "react";
import { Filter, Search, X as XIcon } from "lucide-react";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { Checkbox } from "@/components/ui/checkbox";
import { cn } from "@/lib/utils";
import type { TickerStatus } from "@/lib/cron/status-config";

export const ALL_STATUSES: TickerStatus[] = [
  "Idle",
  "Queued",
  "InProgress",
  "Done",
  "DueDone",
  "Failed",
  "Cancelled",
  "Skipped",
];

export function FilterChip({
  label,
  value,
  onRemove,
}: {
  label: string;
  value: string;
  onRemove: () => void;
}) {
  return (
    <span className="inline-flex items-center gap-1 rounded-md bg-surface-1 border border-border/60 px-2 py-0.5 text-[11px] text-muted-foreground">
      <span className="text-muted-foreground/60">{label}:</span>
      <span className="text-foreground font-medium max-w-[200px] truncate">
        {value}
      </span>
      <button
        type="button"
        onClick={onRemove}
        className="ml-0.5 hover:text-foreground transition-colors"
        aria-label={`Remove ${label} filter`}
      >
        <XIcon className="h-3 w-3" />
      </button>
    </span>
  );
}

export function SearchInput({
  value,
  onChange,
  placeholder,
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
}) {
  return (
    <div className="relative">
      <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground" />
      <input
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder || "Search…"}
        className="h-8 w-full min-w-[220px] max-w-xs rounded-lg border border-border bg-surface-1 pl-8 pr-3 text-sm text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-1 focus:ring-ring transition-shadow"
      />
    </div>
  );
}

export interface FilterPopoverProps {
  statusFilter: TickerStatus[];
  onStatusChange: (next: TickerStatus[]) => void;
  functionFilter: string;
  onFunctionChange: (next: string) => void;
  functions: string[];
  onClear: () => void;
  extra?: ReactNode;
}

export function FilterPopover({
  statusFilter,
  onStatusChange,
  functionFilter,
  onFunctionChange,
  functions,
  onClear,
  extra,
}: FilterPopoverProps) {
  const activeCount =
    (statusFilter.length > 0 ? 1 : 0) + (functionFilter ? 1 : 0);

  return (
    <Popover>
      <PopoverTrigger asChild>
        <button
          className={cn(
            "h-8 px-2.5 rounded-md text-xs font-medium transition-all flex items-center gap-1.5 bg-surface-1 border border-border/60",
            activeCount > 0
              ? "bg-surface-2 text-foreground shadow-sm"
              : "text-muted-foreground hover:text-foreground"
          )}
        >
          <Filter className="h-3.5 w-3.5" />
          Filter{activeCount > 0 ? ` · ${activeCount}` : ""}
        </button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-80 p-0">
        <div className="px-3 py-2.5 border-b border-border/50 space-y-1.5">
          <div className="text-[10px] uppercase tracking-wide text-muted-foreground/60">
            Status
          </div>
          <div className="grid grid-cols-2 gap-x-2 gap-y-1">
            {ALL_STATUSES.map((s) => {
              const active = statusFilter.includes(s);
              return (
                <label
                  key={s}
                  className="flex items-center gap-1.5 text-[11px] cursor-pointer select-none text-muted-foreground hover:text-foreground"
                >
                  <Checkbox
                    checked={active}
                    onCheckedChange={(checked) => {
                      const next = checked
                        ? [...statusFilter, s]
                        : statusFilter.filter((x) => x !== s);
                      onStatusChange(next);
                    }}
                    className="h-3 w-3"
                  />
                  <span className={cn(active && "text-foreground font-medium")}>
                    {s}
                  </span>
                </label>
              );
            })}
          </div>
        </div>

        <div className="px-3 py-2.5 border-b border-border/50 space-y-1.5">
          <div className="text-[10px] uppercase tracking-wide text-muted-foreground/60">
            Function
          </div>
          <select
            value={functionFilter}
            onChange={(e) => onFunctionChange(e.target.value)}
            className="h-7 w-full text-[11px] rounded-md border border-border bg-card px-2 text-muted-foreground hover:text-foreground"
          >
            <option value="">Any function</option>
            {functions.map((fn) => (
              <option key={fn} value={fn}>
                {fn}
              </option>
            ))}
          </select>
        </div>

        {extra && (
          <div className="px-3 py-2.5 border-b border-border/50 space-y-1.5">
            {extra}
          </div>
        )}

        {activeCount > 0 && (
          <div className="px-3 py-2">
            <button
              onClick={onClear}
              className="text-[11px] text-muted-foreground hover:text-foreground underline-offset-2 hover:underline"
            >
              Clear all filters
            </button>
          </div>
        )}
      </PopoverContent>
    </Popover>
  );
}
