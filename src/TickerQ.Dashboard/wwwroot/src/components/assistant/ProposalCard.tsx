import { useRef, useState } from "react";
import { CalendarClock, Check, CornerDownRight, Loader2, Pencil, Timer, Workflow, X, RotateCcw, Ban, Trash2, Bell, Activity } from "lucide-react";
import cronstrue from "cronstrue";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { useQueryClient } from "@tanstack/react-query";
import { useDashboardOptions } from "@/services/hooks";
import { dashboardApi } from "@/services/dashboard-api";
import { applyAssistantProposal } from "@/lib/apply-assistant-proposal";
import type { AssistantProposal, ProposalChainNode } from "@/services/assistant-api";

export type ProposalStatus = "pending" | "applied" | "dismissed";

function humanizeCron(expression: string): string {
  try {
    return cronstrue.toString(expression);
  } catch {
    return "";
  }
}

function parseChain(json: string): ProposalChainNode | null {
  try {
    const parsed = JSON.parse(json) as ProposalChainNode;
    return parsed && typeof parsed.function === "string" ? parsed : null;
  } catch {
    return null;
  }
}


const KIND_META: Record<AssistantProposal["kind"], { title: string; icon: typeof Timer }> = {
  createTimeTicker: { title: "Create time ticker", icon: CalendarClock },
  createCronTicker: { title: "Create cron ticker", icon: Timer },
  updateCronTicker: { title: "Update cron ticker", icon: Pencil },
  createChain: { title: "Create chain", icon: Workflow },
  createCronChain: { title: "Create recurring chain", icon: Workflow },
  retryExecution: { title: "Retry execution", icon: RotateCcw },
  cancelExecution: { title: "Cancel execution", icon: Ban },
  deleteExecution: { title: "Delete permanently", icon: Trash2 },
  createAlertRule: { title: "Create alert rule", icon: Bell },
  createMonitor: { title: "Create monitor", icon: Activity },
};

/**
 * Confirmation card for a model-drafted mutation. The model only PROPOSES —
 * clicking Confirm executes through the dashboard's normal REST endpoints
 * (same auth, validation, read-only guards and live updates as manual edits).
 */
export function ProposalCard({
  proposal,
  status,
  onStatusChange,
}: {
  proposal: AssistantProposal;
  status: ProposalStatus;
  onStatusChange: (status: ProposalStatus) => void;
}) {
  const [busy, setBusy] = useState(false);
  const confirming = useRef(false);
  const { data: options } = useDashboardOptions();
  const queryClient = useQueryClient();
  const meta = KIND_META[proposal.kind];
  const Icon = meta?.icon ?? Timer;
  const chainRoot = ["createChain", "createCronChain"].includes(proposal.kind) ? parseChain(proposal.chainJson) : null;
  const destructive = proposal.kind === "deleteExecution";

  async function confirm() {
    if (confirming.current || status !== "pending") return;
    confirming.current = true;
    setBusy(true);
    try {
      const message = await applyAssistantProposal(proposal, dashboardApi);
      onStatusChange("applied");
      toast.success(message);
      void queryClient.invalidateQueries();
    } catch (err) {
      toast.error("Failed to apply", { description: err instanceof Error ? err.message : "An error occurred" });
    } finally {
      confirming.current = false;
      setBusy(false);
    }
  }

  const cronText = proposal.expression ? humanizeCron(proposal.expression) : "";
  const expressionChanged =
    proposal.kind === "updateCronTicker" &&
    proposal.currentExpression &&
    proposal.currentExpression !== proposal.expression;

  return (
    <div
      className={cn(
        "w-full max-w-xl rounded-2xl border p-3 sm:p-4 text-[12.5px]",
        status === "applied"
          ? "border-status-healthy/40 bg-status-healthy/5"
          : status === "dismissed"
            ? "border-border bg-surface-1/40 opacity-60"
            : destructive ? "border-destructive/40 bg-destructive/5" : "border-primary/40 bg-primary/5"
      )}
    >
      <div className="mb-2 flex items-center gap-2">
        <Icon className="h-4 w-4 text-primary" />
        <span className="font-semibold">{meta?.title ?? "Unsupported proposal"}</span>
        {status === "applied" && (
          <span className="ml-auto inline-flex items-center gap-1 text-[11px] text-status-healthy">
            <Check className="h-3 w-3" /> {proposal.kind === "cancelExecution" && proposal.expectedStatus === "InProgress" ? "Requested" : "Applied"}
          </span>
        )}
        {status === "dismissed" && (
          <span className="ml-auto text-[11px] text-muted-foreground">Dismissed</span>
        )}
      </div>

      <dl className="space-y-1.5">
        {chainRoot ? (
          <Row label="Steps">
            <ChainTree node={chainRoot} isRoot />
          </Row>
        ) : (
          <Row label="Function">
            <span className="font-mono">{proposal.function || "All functions"}</span>
          </Row>
        )}

        {proposal.targetId && (
          <Row label="Target">
            <span className="break-all font-mono text-[11px] text-muted-foreground">
              {proposal.targetType && `${proposal.targetType} · `}{proposal.targetId}
            </span>
          </Row>
        )}

        {proposal.expectedStatus && <Row label="Current state">{proposal.expectedStatus}</Row>}
        {proposal.skipIfAlreadyRunning != null && <Row label="Overlap">{proposal.skipIfAlreadyRunning ? "Skip while the previous chain is unfinished" : "Runs may overlap"}</Row>}
        {proposal.rule && <>
          <Row label="Rule">{proposal.rule.name}</Row>
          <Row label="Events">{proposal.rule.events.join(", ")}</Row>
          <Row label="Cooldown">{proposal.rule.cooldownSeconds} seconds per node</Row>
        </>}
        {proposal.monitor && <>
          <Row label="Monitor">{proposal.monitor.name}</Row>
          <Row label="Condition">{proposal.monitor.kind === "deadline"
            ? `Finish by ${proposal.monitor.deadlineLocalTime} (${proposal.monitor.timeZoneId}), ${proposal.monitor.graceMinutes} min grace`
            : proposal.monitor.kind === "long_running" ? `Runs longer than ${proposal.monitor.durationSeconds} seconds`
            : `${proposal.monitor.failureThreshold} consecutive failures within ${proposal.monitor.lookbackHours} hours`}</Row>
          <Row label="Recovery">{proposal.monitor.notifyRecovery ? "Send when the incident clears" : "Off"}</Row>
        </>}
        {!!proposal.destinationNames?.length && <Row label="Send to">{proposal.destinationNames.join(", ")}</Row>}
        {proposal.expression && (
          <Row label="Schedule">
            <div>
              {expressionChanged && (
                <div className="text-muted-foreground line-through">
                  {proposal.currentExpression}
                </div>
              )}
              <span className="font-mono">{proposal.expression}</span>
              {cronText && <div className="text-[11px] text-muted-foreground">{cronText} · {options?.schedulerTimeZone ?? "Scheduler time zone"}</div>}
            </div>
          </Row>
        )}

        {proposal.executionTime && (
          <Row label="Runs at">
            <span className="tabular-nums">
              {proposal.executionTime.replace("T", " ").slice(0, 19)} UTC
            </span>
          </Row>
        )}

        {proposal.retries != null && proposal.retries > 0 && (
          <Row label="Retries">{proposal.retries}</Row>
        )}

        {proposal.kind !== "createTimeTicker" && proposal.isEnabled != null && (
          <Row label="Enabled">{proposal.isEnabled ? "Yes" : "No"}</Row>
        )}

        {proposal.description && <Row label="Description">{proposal.description}</Row>}

        {proposal.requestJson && (
          <Row label="Payload">
            <pre className="max-h-24 overflow-auto rounded bg-surface-0/80 p-2 font-mono text-[11px] scrollbar-thin">
              {proposal.requestJson}
            </pre>
          </Row>
        )}
      </dl>

      {proposal.warning && <p className={cn("mt-3 border-t border-border/60 pt-3 text-xs leading-relaxed", destructive ? "text-destructive" : "text-muted-foreground")}>{proposal.warning}</p>}
      {status === "pending" && (
        <div className="mt-3 flex items-center gap-2">
          <button
            type="button"
            onClick={() => void confirm()}
            disabled={busy || !meta}
            className={cn("inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-[12px] font-medium disabled:opacity-50", destructive ? "bg-destructive text-destructive-foreground" : "bg-primary text-primary-foreground")}
          >
            {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" />}
            {destructive ? "Confirm deletion" : proposal.kind === "cancelExecution" && proposal.expectedStatus === "InProgress" ? "Request cancellation" : "Confirm"}
          </button>
          <button
            type="button"
            onClick={() => onStatusChange("dismissed")}
            disabled={busy}
            className="inline-flex items-center gap-1.5 rounded-lg border border-border px-3 py-1.5 text-[12px] text-muted-foreground hover:text-foreground disabled:opacity-50"
          >
            <X className="h-3.5 w-3.5" />
            Dismiss
          </button>
        </div>
      )}
    </div>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex gap-3">
      <dt className="w-16 sm:w-20 shrink-0 text-[10.5px] uppercase tracking-wider text-muted-foreground/60 pt-0.5">
        {label}
      </dt>
      <dd className="min-w-0 flex-1 [overflow-wrap:anywhere]">{children}</dd>
    </div>
  );
}

function ChainTree({ node, isRoot = false }: { node: ProposalChainNode; isRoot?: boolean }) {
  return (
    <div className={cn(!isRoot && "mt-1")}>
      <div className="flex items-center gap-1.5">
        {!isRoot && <CornerDownRight className="h-3 w-3 shrink-0 text-muted-foreground/50" />}
        <span className="font-mono">{node.function}</span>
        {!isRoot && node.runCondition && (
          <span className="rounded-full border border-border px-1.5 py-px text-[10px] text-muted-foreground">
            {node.runCondition}
          </span>
        )}
        {node.retries != null && node.retries > 0 && (
          <span className="text-[10px] text-muted-foreground">retries {node.retries}</span>
        )}
      </div>
      {node.description && <p className="mt-1 text-xs text-muted-foreground">{node.description}</p>}
      {node.requestJson && <details className="mt-1 text-xs"><summary className="cursor-pointer text-muted-foreground">Payload</summary><pre className="max-h-28 overflow-auto whitespace-pre-wrap break-all rounded bg-surface-0 p-2">{node.requestJson}</pre></details>}
      {(node.children ?? []).length > 0 && (
        <div className="ml-4 border-l border-border/60 pl-2">
          {(node.children ?? []).map((child, i) => (
            <ChainTree key={i} node={child} />
          ))}
        </div>
      )}
    </div>
  );
}
