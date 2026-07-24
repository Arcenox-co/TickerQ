import { useState } from "react";
import { CalendarClock, Check, CornerDownRight, Loader2, Pencil, Timer, Workflow, X } from "lucide-react";
import cronstrue from "cronstrue";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import {
  useAddCronTicker,
  useAddTimeTicker,
  useAddTimeTickerChain,
  useUpdateCronTicker,
} from "@/services/hooks";
import type { AssistantProposal, ProposalChainNode } from "@/services/assistant-api";
import type { RunCondition, TimeTickerNode } from "@/services/api-types";

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

function toTimeTickerNode(node: ProposalChainNode, isRoot: boolean): TimeTickerNode {
  return {
    function: node.function,
    description: node.description || null,
    retries: node.retries ?? null,
    request: node.requestJson || null,
    retryIntervalsSeconds: null,
    runCondition: isRoot ? null : ((node.runCondition ?? "OnSuccess") as RunCondition),
    children: (node.children ?? []).map((c) => toTimeTickerNode(c, false)),
  };
}

const KIND_META: Record<AssistantProposal["kind"], { title: string; icon: typeof Timer }> = {
  createTimeTicker: { title: "Create time ticker", icon: CalendarClock },
  createCronTicker: { title: "Create cron ticker", icon: Timer },
  updateCronTicker: { title: "Update cron ticker", icon: Pencil },
  createChain: { title: "Create chain", icon: Workflow },
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
  const addTime = useAddTimeTicker();
  const addCron = useAddCronTicker();
  const addChain = useAddTimeTickerChain();
  const updateCron = useUpdateCronTicker();

  const meta = KIND_META[proposal.kind] ?? KIND_META.createCronTicker;
  const Icon = meta.icon;
  const chainRoot = proposal.kind === "createChain" ? parseChain(proposal.chainJson) : null;

  async function confirm() {
    if (busy || status !== "pending") return;
    setBusy(true);
    try {
      if (proposal.kind === "createTimeTicker") {
        await addTime.mutateAsync({
          function: proposal.function,
          executionTime: proposal.executionTime || null,
          description: proposal.description || null,
          retries: proposal.retries ?? null,
          retryIntervalsSeconds: null,
          request: proposal.requestJson || null,
        });
        toast.success("Time ticker created", { description: proposal.function });
      } else if (proposal.kind === "createCronTicker") {
        await addCron.mutateAsync({
          function: proposal.function,
          expression: proposal.expression,
          description: proposal.description || null,
          retries: proposal.retries ?? null,
          retryIntervalsSeconds: null,
          request: proposal.requestJson || null,
          isEnabled: proposal.isEnabled ?? true,
        });
        toast.success("Cron ticker created", { description: proposal.function });
      } else if (proposal.kind === "createChain" && chainRoot) {
        await addChain.mutateAsync({
          executionTime: proposal.executionTime,
          root: toTimeTickerNode(chainRoot, true),
        });
        toast.success("Chain created", { description: chainRoot.function });
      } else if (proposal.kind === "updateCronTicker" && proposal.targetId) {
        await updateCron.mutateAsync({
          id: proposal.targetId,
          body: {
            expression: proposal.expression,
            description: proposal.description || null,
            retries: proposal.retries ?? null,
            retryIntervalsSeconds: null,
            isEnabled: proposal.isEnabled ?? true,
          },
        });
        toast.success("Cron ticker updated", { description: proposal.function });
      }
      onStatusChange("applied");
    } catch (err) {
      toast.error("Failed to apply", {
        description: err instanceof Error ? err.message : "An error occurred",
      });
    } finally {
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
        "max-w-[85%] rounded-2xl border p-4 text-[12.5px]",
        status === "applied"
          ? "border-status-healthy/40 bg-status-healthy/5"
          : status === "dismissed"
            ? "border-border bg-surface-1/40 opacity-60"
            : "border-primary/40 bg-primary/5"
      )}
    >
      <div className="mb-2 flex items-center gap-2">
        <Icon className="h-4 w-4 text-primary" />
        <span className="font-semibold">{meta.title}</span>
        {status === "applied" && (
          <span className="ml-auto inline-flex items-center gap-1 text-[11px] text-status-healthy">
            <Check className="h-3 w-3" /> Applied
          </span>
        )}
        {status === "dismissed" && (
          <span className="ml-auto text-[11px] text-muted-foreground">Dismissed</span>
        )}
      </div>

      <dl className="space-y-1.5">
        {proposal.kind === "createChain" && chainRoot ? (
          <Row label="Steps">
            <ChainTree node={chainRoot} isRoot />
          </Row>
        ) : (
          <Row label="Function">
            <span className="font-mono">{proposal.function}</span>
          </Row>
        )}

        {proposal.kind === "updateCronTicker" && proposal.targetId && (
          <Row label="Target">
            <span className="font-mono text-[11px] text-muted-foreground">
              {proposal.targetId.slice(0, 12)}…
            </span>
          </Row>
        )}

        {proposal.expression && (
          <Row label="Schedule">
            <div>
              {expressionChanged && (
                <div className="text-muted-foreground line-through">
                  {proposal.currentExpression}
                </div>
              )}
              <span className="font-mono">{proposal.expression}</span>
              {cronText && <div className="text-[11px] text-muted-foreground">{cronText}</div>}
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

      {status === "pending" && (
        <div className="mt-3 flex items-center gap-2">
          <button
            type="button"
            onClick={() => void confirm()}
            disabled={busy}
            className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-3 py-1.5 text-[12px] font-medium text-primary-foreground disabled:opacity-50"
          >
            {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" />}
            Confirm
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
      <dt className="w-20 shrink-0 text-[10.5px] uppercase tracking-wider text-muted-foreground/60 pt-0.5">
        {label}
      </dt>
      <dd className="min-w-0 flex-1">{children}</dd>
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
