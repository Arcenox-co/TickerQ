import { useMemo } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
} from "@/components/ui/dialog";
import { PARALLEL_GROUP_FUNCTION } from "@/lib/cron/chain-flow-layout";
import type {
  TestRunChainResponse,
  TimeTickerFlatDto,
} from "@/services/api-types";
import { ChainFlowchartView } from "./ChainFlowchartView";

function toFlowchartTickers(result: TestRunChainResponse): TimeTickerFlatDto[] {
  return result.steps.map(step => ({
    id: step.executionId,
    parentId: step.parentExecutionId,
    functionName: step.isGroup ? PARALLEL_GROUP_FUNCTION : step.function,
    status: step.status,
    scheduledFor: step.parentExecutionId ? null : result.startedAtUtc,
    elapsedTime: step.elapsedMilliseconds,
    progressPercent: step.progressPercent,
    progressMessage: step.progressMessage,
    progressUpdatedAt: step.progressUpdatedAtUtc,
    retries: step.retries,
    retryCount: step.retryCount,
    priority: step.priority,
    createdAt: step.createdAtUtc,
    childCount: step.childCount,
    exceptionMessage: step.status === "Skipped" ? null : step.error,
    skippedReason: step.status === "Skipped" ? step.error : null,
    runCondition: step.runCondition,
    executedAt: step.executedAtUtc,
    description: step.description,
    lockHolder: null,
    lockedAt: null,
    retryIntervalsSeconds: null,
    onStale: "Restart",
    timeoutSeconds: null,
    deleteBehavior: "Cascade",
    schedulePolicyName: null,
    schedulePolicyVersion: null,
    blockedOccurrenceBehavior: null,
    missedExecutionAction: "RunOnce",
    maximumLatenessSeconds: null,
    requestedExecutionTime: null,
    scheduleAdjustmentReason: null,
    type: "TimeTicker",
  }));
}

export function ChainTestResultsDialog({
  open,
  result,
  onClose,
}: {
  open: boolean;
  result: TestRunChainResponse | null;
  onClose: () => void;
}) {
  const tickers = useMemo(
    () => result ? toFlowchartTickers(result) : [],
    [result]
  );

  return (
    <Dialog open={open && !!result} onOpenChange={next => { if (!next) onClose(); }}>
      <DialogContent
        showCloseButton={false}
        className="h-[calc(100dvh-1rem)] w-[calc(100vw-1rem)] max-w-none overflow-hidden rounded-lg p-0 sm:max-w-none"
      >
        <DialogTitle className="sr-only">Chain test execution</DialogTitle>
        <DialogDescription className="sr-only">
          Live, read-only execution of the draft chain. Temporary ticker IDs and logs are kept only in memory.
        </DialogDescription>
        {result && (
          <ChainFlowchartView
            rootId={result.rootExecutionId}
            previewTickers={tickers}
            ephemeralTest
            ephemeralTestCompleted={result.completed}
            backLabel="Back to chain editor"
            onBack={onClose}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}
