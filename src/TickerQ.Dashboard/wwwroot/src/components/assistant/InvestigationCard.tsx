import { AlertTriangle, ArrowRight, Bot, CheckCircle2, Clock3, Layers3, Loader2, RotateCcw, ShieldCheck, Trash2 } from "lucide-react";
import { formatAbsolute, relativeTime } from "@/lib/cron/format";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";
import type { AssistantInvestigation } from "@/services/assistant-api";

function tone(value: AssistantInvestigation): string {
  if (value.state === "Processing") return "border-status-info/25 bg-status-info/[0.035]";
  if (value.state === "Failed") return "border-status-error/25 bg-status-error/[0.035]";
  if (value.recoveredAt) return "border-status-success/25 bg-status-success/[0.035]";
  return "border-border bg-card";
}

function StateIcon({ value }: { value: AssistantInvestigation }) {
  if (value.state === "Processing") return <Loader2 className="h-3.5 w-3.5 animate-spin text-status-info" />;
  if (value.state === "Failed") return <AlertTriangle className="h-3.5 w-3.5 text-status-error" />;
  if (value.recoveredAt) return <CheckCircle2 className="h-3.5 w-3.5 text-status-success" />;
  return <Bot className="h-3.5 w-3.5 text-primary" />;
}

function reviewLabel(value: string): string {
  if (value === "New") return "Unread";
  if (value === "Reviewed") return "Read";
  return value;
}

export function InvestigationCard({
  value,
  selected = false,
  onOpen,
  onContinue,
  onDeleteGroup,
  stackCount = 1,
  unreadCount,
}: {
  value: AssistantInvestigation;
  selected?: boolean;
  onOpen?: () => void;
  onContinue?: () => void;
  onDeleteGroup?: () => void;
  /** Number of same-function investigations represented by this card. */
  stackCount?: number;
  /** Number of represented investigations that have not been opened yet. */
  unreadCount?: number;
}) {
  const { timezone } = useTimezone();
  const occurred = value.occurredAt || value.createdAt;
  const representedUnread = unreadCount ?? (value.reviewState === "New" ? 1 : 0);
  return (
    <div className={cn("relative", stackCount > 1 && "mb-2 mr-2")}>
      {stackCount > 2 ? (
        <div aria-hidden="true" className="absolute inset-0 translate-x-2 translate-y-2 rounded-xl border border-border/35 bg-card/35" />
      ) : null}
      {stackCount > 1 ? (
        <div aria-hidden="true" className="absolute inset-0 translate-x-1 translate-y-1 rounded-xl border border-border/55 bg-card/70" />
      ) : null}
      <article className={cn(
        "group relative z-10 rounded-xl border p-3.5 transition-colors",
        tone(value),
        selected && "ring-1 ring-primary/45",
        onOpen && "cursor-pointer hover:border-primary/30",
      )} onClick={onOpen}>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <StateIcon value={value} />
            <h3 className="truncate text-[13px] font-semibold text-foreground">{value.functionName}</h3>
            {stackCount > 1 ? (
              <span
                className="inline-flex shrink-0 items-center gap-1 rounded-full border border-primary/15 bg-primary/[0.07] px-1.5 py-0.5 text-[9px] font-medium text-primary"
                title={`${stackCount} investigations for this function`}
              >
                <Layers3 className="h-2.5 w-2.5" /> {stackCount}
              </span>
            ) : null}
          </div>
          <div className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1 text-[10.5px] text-muted-foreground">
            <span>{value.classification}</span>
            <span aria-hidden="true">·</span>
            <span title={formatAbsolute(occurred, timezone)}>{relativeTime(occurred)}</span>
            <span aria-hidden="true">·</span>
            <span>{value.trigger === "Automatic"
              ? "Automatic"
              : value.trigger === "Automatic reuse"
                ? "Reused diagnosis"
                : "Requested"}</span>
          </div>
        </div>
        <span className={cn(
          "shrink-0 rounded-full border px-2 py-0.5 text-[9.5px] font-medium",
          representedUnread > 0
            ? "border-status-warning/25 bg-status-warning/10 text-status-warning"
            : value.recoveredAt
              ? "border-status-success/25 bg-status-success/10 text-status-success"
              : "border-border bg-surface-2 text-muted-foreground",
        )}>
          {representedUnread > 1
            ? `${representedUnread} unread`
            : representedUnread === 1
              ? "Unread"
              : value.recoveredAt
                ? "Recovery detected"
                : reviewLabel(value.reviewState)}
        </span>
      </div>

      {value.state === "Processing" ? (
        <p className="mt-3 text-[11.5px] text-muted-foreground">Collecting bounded evidence and preparing the diagnosis…</p>
      ) : value.state === "Failed" ? (
        <p className="mt-3 line-clamp-2 text-[11.5px] text-status-error/85">{value.failureMessage || "The provider did not complete this investigation."}</p>
      ) : (
        <p className="mt-3 line-clamp-2 text-[11.5px] leading-relaxed text-foreground/75">
          {value.summary || "Investigation completed without a saved summary."}
        </p>
      )}

      <div className="mt-3 flex items-center justify-between border-t border-border/60 pt-2.5">
        <div className="flex items-center gap-2 text-[10px] text-muted-foreground/70">
          {value.sourcePath ? <span className="inline-flex items-center gap-1"><ShieldCheck className="h-3 w-3" />Source frame captured</span> : null}
          {value.recoveredAt ? <span className="inline-flex items-center gap-1"><RotateCcw className="h-3 w-3" />Later run succeeded</span> : null}
          {!value.sourcePath && !value.recoveredAt ? <span className="inline-flex items-center gap-1"><Clock3 className="h-3 w-3" />{value.executionStatus}</span> : null}
        </div>
        <div className="flex items-center gap-3">
          {onDeleteGroup ? (
            <button type="button" onClick={(event) => { event.stopPropagation(); onDeleteGroup(); }}
              className="inline-flex items-center gap-1 text-[10.5px] font-medium text-status-error/75 hover:text-status-error"
              aria-label={`Delete all investigations for ${value.functionName}`}>
              <Trash2 className="h-3 w-3" /> Delete group
            </button>
          ) : null}
          {onContinue && value.state === "Completed" ? (
            <button type="button" onClick={(event) => { event.stopPropagation(); onContinue(); }}
              className="inline-flex items-center gap-1 text-[10.5px] font-medium text-primary hover:text-primary/80">
              Continue in chat <ArrowRight className="h-3 w-3" />
            </button>
          ) : null}
        </div>
      </div>

      </article>
    </div>
  );
}
