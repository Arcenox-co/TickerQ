import { ExecutionStatus } from "./ExecutionStatus";
import { isChainActive } from "@/lib/cron/chain-summary";
import { useEffect, useRef, useState } from "react";
import { ArrowLeft, Copy, LayoutTemplate, Pencil, Play, RotateCcw, Sparkles, X } from "lucide-react";
import { Link } from "react-router-dom";
import { toast } from "sonner";

import { ResizableSidePanel } from "@/components/cron/ResizableSidePanel";
import { LogsPanel } from "@/components/cron/LogsPanel";
import { ChainLogsPanel } from "@/components/cron/ChainLogsPanel";
import { PayloadPreview } from "@/components/cron/PayloadPreview";
import { EditTimeTickerFields } from "@/components/cron/EditTimeTickerFields";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { SaveDefinitionDialog } from "@/components/cron/SaveDefinitionDialog";
import { CancelTickerButton } from "@/components/cron/CancelTickerButton";
import { LockHolder } from "@/components/cron/LockHolder";
import { ChainDeleteBehaviorControl } from "@/components/cron/ChainDeleteBehaviorControl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { nowAsDatetimeLocal } from "@/components/form-fields/datetime-field";
import { findDuplicateTemplateName } from "@/lib/cron/saved-definitions";
import {
  useDuplicateTimeTicker,
  useChainTickers,
  useRunTimeTicker,
  useTimeTicker,
  useSaveExistingTickerAsTemplate,
  useDefinitions,
} from "@/services/hooks";
import { formatDuration, relativeTime } from "@/lib/cron/format";
import { investigationExecutionId } from "@/lib/assistant-investigation";
import { isAssistantEnabled, isReadOnly } from "@/lib/runtime-config";
import type { TimeTickerFlatDto } from "@/services/api-types";

const PENDING = ["Idle", "Queued"];
const ACTIVE = ["Idle", "Queued", "InProgress"];

function errMsg(err: unknown) {
  return err instanceof Error ? err.message : "An error occurred";
}

export function TimeTickerDetailPanel({
  ticker,
  onClose,
  onChanged,
  onDuplicated,
}: {
  ticker: TimeTickerFlatDto | null;
  onClose: () => void;
  onChanged: () => void;
  onDuplicated: (newId: string) => void;
}) {
  const [tab, setTab] = useState<"logs" | "edit">("logs");
  const [confirmDup, setConfirmDup] = useState(false);
  const [confirmRerun, setConfirmRerun] = useState(false);
  const [dupTime, setDupTime] = useState("");
  const [saveTemplateOpen, setSaveTemplateOpen] = useState(false);
  const readOnly = isReadOnly();
  const runM = useRunTimeTicker();
  const dupM = useDuplicateTimeTicker();
  const saveTemplateM = useSaveExistingTickerAsTemplate();
  const templateDefinitions = useDefinitions("Template", "TimeTicker");

  // The `ticker` prop is a snapshot of the list row from when the panel was
  // opened. Poll the single-ticker endpoint while the run is still active so
  // status, actions and the live log tail track the execution in real time.
  const liveQ = useTimeTicker(ticker?.id ?? null, {
    refetchInterval: (query) => {
      const s = query.state.data?.status ?? ticker?.status;
      return (s && ACTIVE.includes(s)) || isChainActive(query.state.data?.chainSummary ?? ticker?.chainSummary) ? 2_000 : false;
    },
  });
  const chainQ = useChainTickers(ticker?.childCount ? ticker.id : null);

  // When the live status flips (Idle → InProgress → Done/Failed), refresh the
  // backing list so the table row behind the panel stays in sync.
  const prevStatus = useRef<string | null>(null);
  useEffect(() => {
    const s = liveQ.data?.status;
    if (!s) return;
    if (prevStatus.current && prevStatus.current !== s) onChanged();
    prevStatus.current = s;
  }, [liveQ.data?.status, onChanged]);

  if (!ticker) return null;

  const t = liveQ.data ?? ticker;
  const isPending = PENDING.includes(t.status);
  const isRunning = t.status === "InProgress";
  const isChain = t.childCount > 0;
  const investigationId = investigationExecutionId(
    isChain ? (chainQ.data ?? [t]) : [t]
  );
  const canInvestigate = investigationId !== null && isAssistantEnabled();
  const bare = t.functionName;
  const lockHolder = t.lockHolder;

  async function handleRun() {
    try {
      await runM.mutateAsync(t.id);
      toast.success("Ticker rescheduled to run now");
      onChanged();
    } catch (err) {
      toast.error("Failed to run", { description: errMsg(err) });
    }
  }
  async function handleRerun() {
    try {
      await runM.mutateAsync(t.id);
      setConfirmRerun(false);
      toast.success("Ticker re-run started", {
        description: "The existing ticker record and ID are being reused.",
      });
      onChanged();
    } catch (err) {
      toast.error("Failed to re-run", { description: errMsg(err) });
    }
  }
  async function handleDuplicate() {
    try {
      const res = await dupM.mutateAsync({
        id: t.id,
        body: dupTime ? { executionTime: dupTime } : undefined,
      });
      toast.success(isChain ? "Chain duplicated" : "Ticker duplicated");
      setConfirmDup(false);
      onDuplicated(res.id);
      onClose();
    } catch (err) {
      toast.error("Failed to duplicate", { description: errMsg(err) });
    }
  }

  return (
    <ResizableSidePanel open={!!ticker} onClose={onClose}>
      <div className="flex flex-col">
        {/* Header */}
        <div className="px-5 py-4 border-b border-border flex items-start justify-between gap-3">
          <div className="min-w-0">
            <div className="font-mono text-sm font-semibold truncate">{bare}</div>
            {lockHolder && (
              <LockHolder value={lockHolder} className="text-muted-foreground/60" />
            )}
          </div>
          <div className="flex items-center gap-2 shrink-0">
            {isPending && !readOnly && (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 gap-1 text-xs"
                onClick={() => setTab((x) => (x === "edit" ? "logs" : "edit"))}
              >
                {tab === "edit" ? (
                  <>
                    <ArrowLeft className="h-3 w-3" /> Back
                  </>
                ) : (
                  <>
                    <Pencil className="h-3 w-3" /> Edit
                  </>
                )}
              </Button>
            )}
            <ExecutionStatus status={t.status} summary={t.chainSummary} isChain={isChain} />
            <button
              type="button"
              onClick={onClose}
              className="text-muted-foreground/60 hover:text-foreground"
            >
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        {/* Actions */}
        {(!readOnly || canInvestigate) && (
        <div className="px-5 py-3 border-b border-border flex items-center gap-2">
          {!readOnly && isChain && (
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs"
              onClick={() => setSaveTemplateOpen(true)}
            >
              <LayoutTemplate className="h-3 w-3 mr-1" />
              Save template
            </Button>
          )}
          {!readOnly && !isRunning &&
            (isPending ? (
              <Button
                variant="gradient"
                size="sm"
                className="h-7 text-xs"
                disabled={runM.isPending}
                onClick={handleRun}
              >
                <Play className="h-3 w-3 mr-1" />
                Run now
              </Button>
            ) : (
              <>
              {!isChain && (
                <Button
                  variant="gradient"
                  size="sm"
                  className="h-7 text-xs"
                  disabled={runM.isPending}
                  onClick={() => setConfirmRerun(true)}
                >
                  <RotateCcw className="h-3 w-3 mr-1" />
                  Re-run
                </Button>
              )}
              <Button
                variant="outline"
                size="sm"
                className="h-7 text-xs"
                onClick={() => {
                  setDupTime("");
                  setConfirmDup(true);
                }}
              >
                <Copy className="h-3 w-3 mr-1" />
                Duplicate
              </Button>
              </>
            ))}
          {canInvestigate && (
            <Button
              asChild
              variant="outline"
              size="sm"
              className="h-7 gap-1 text-xs text-primary"
            >
              <Link to={`/assistant?investigate=${encodeURIComponent(investigationId!)}`}>
                <Sparkles className="h-3 w-3" />
                Investigate with AI
              </Link>
            </Button>
          )}
          {!readOnly && (
            <CancelTickerButton id={t.id} status={t.status} hasChildren={isChain} onChanged={onChanged} />
          )}
        </div>
        )}

        {/* Meta grid */}
        <div className="px-5 py-3 border-b border-border grid grid-cols-3 gap-3 text-[11px]">
          <Meta label="Scheduled" value={relativeTime(t.scheduledFor)} />
          <Meta
            label="Duration"
            value={t.executedAt ? formatDuration(t.elapsedTime) : "—"}
          />
          <Meta
            label="Retries"
            value={t.retries > 0 ? `${t.retryCount}/${t.retries}` : "—"}
          />
          {lockHolder && (
            <div className="col-span-3 min-w-0">
              <p className="text-muted-foreground/60 uppercase tracking-wider mb-1">Node</p>
              <LockHolder value={lockHolder} showDetails />
            </div>
          )}
        </div>

        {t.progressPercent != null && (
          <div className="border-b border-border px-5 py-3">
            <div className="mb-1.5 flex items-center justify-between gap-3 text-[11px]">
              <span className="truncate text-muted-foreground">{t.progressMessage || "Job progress"}</span>
              <span className="shrink-0 font-medium tabular-nums">{Math.round(t.progressPercent)}%</span>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-surface-2">
              <div
                className="h-full rounded-full bg-primary transition-[width] duration-300"
                style={{ width: `${Math.max(0, Math.min(100, t.progressPercent))}%` }}
              />
            </div>
          </div>
        )}

        {/* Description */}
        {!t.parentId && isChain && (
          <div className="px-5 py-3 border-b border-border">
            <ChainDeleteBehaviorControl key={t.id} ticker={t} />
          </div>
        )}
        {t.description && (
          <div className="px-5 py-3 border-b border-border text-[12px] text-muted-foreground whitespace-pre-wrap">
            {t.description}
          </div>
        )}

        {/* Body */}
        <div className="p-4 space-y-3">
          {tab === "edit" && isPending ? (
            <EditTimeTickerFields
              ticker={t}
              onCancel={() => setTab("logs")}
              onSaved={() => {
                setTab("logs");
                onChanged();
              }}
            />
          ) : (
            <>
              <PayloadPreview kind="time" id={t.id} />
              {isChain ? (
                <ChainLogsPanel rootId={t.id} />
              ) : (
                <LogsPanel
                  functionName={bare}
                  status={t.status}
                  tickerId={t.id}
                  exceptionMessage={t.exceptionMessage}
                  skippedReason={t.skippedReason}
                  executedAt={t.executedAt}
                />
              )}
            </>
          )}
        </div>
      </div>

      <ConfirmDialog
        open={confirmRerun}
        onOpenChange={setConfirmRerun}
        title="Re-run this ticker?"
        description={
          <>
            Runs <span className="font-mono font-semibold text-foreground">{bare}</span> again
            using this ticker’s existing ID. Its previous status, result and progress are replaced;
            no duplicate ticker is created.
          </>
        }
        confirmLabel="Re-run"
        isPending={runM.isPending}
        onConfirm={handleRerun}
      />
      <ConfirmDialog
        open={confirmDup}
        onOpenChange={setConfirmDup}
        title="Duplicate ticker?"
        description={
          <>
            Creates an identical copy of{" "}
            <span className="font-mono font-semibold text-foreground">{bare}</span>
            {isChain ? " and its chain" : ""}.
          </>
        }
        confirmLabel="Duplicate"
        isPending={dupM.isPending}
        onConfirm={handleDuplicate}
      >
        <div className="space-y-1.5">
          <label htmlFor="dup-execution-time" className="text-xs font-medium">
            Execution time
          </label>
          <div className="relative">
            <Input
              id="dup-execution-time"
              type="datetime-local"
              step={1}
              value={dupTime}
              onFocus={() => {
                if (!dupTime) setDupTime(nowAsDatetimeLocal());
              }}
              onChange={(e) => setDupTime(e.target.value)}
              className="pr-14"
            />
            {dupTime && (
              <button
                type="button"
                aria-label="Clear"
                title="Clear (fire at submit time)"
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => setDupTime("")}
                className="absolute right-8 top-1/2 -translate-y-1/2 rounded p-0.5 text-muted-foreground/60 hover:text-foreground hover:bg-surface-1"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            )}
          </div>
          <p className="text-[11px] text-muted-foreground/60">
            Empty fires at submit time
          </p>
        </div>
      </ConfirmDialog>
      {isChain && <SaveDefinitionDialog
        open={saveTemplateOpen}
        kind="Template"
        initialName={`${bare} workflow`}
        pending={saveTemplateM.isPending}
        validateName={name => findDuplicateTemplateName(templateDefinitions.data ?? [], name)
          ? "A template with this name already exists."
          : null}
        onOpenChange={setSaveTemplateOpen}
        onSave={async values => {
          try {
            await saveTemplateM.mutateAsync({
              id: t.id,
              target: "TimeTicker",
              body: {
                name: values.name,
                description: values.description || null,
                includeRequestData: values.includeRequestData,
              },
            });
            setSaveTemplateOpen(false);
            toast.success("Template saved");
          } catch (error) {
            toast.error("Could not save the template", { description: errMsg(error) });
          }
        }}
      />}
    </ResizableSidePanel>
  );
}

function Meta({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-muted-foreground/60 uppercase tracking-wider mb-1">{label}</p>
      <p className="tabular-nums">{value}</p>
    </div>
  );
}
