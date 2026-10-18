import { useState } from "react";
import { Ban } from "lucide-react";
import { toast } from "sonner";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { isReadOnly } from "@/lib/runtime-config";
import { useCancelTicker } from "@/services/hooks";
import type { CancelTickerCommand, ExecutionType, TickerStatus } from "@/services/api-types";

interface CancelTickerButtonProps {
  id: string;
  type?: ExecutionType;
  status: TickerStatus;
  hasChildren?: boolean;
  onChanged?: () => void;
}

export function CancelTickerButton(props: CancelTickerButtonProps) {
  // A different job or status starts a fresh confirmation/request state.
  return <CancellationAction key={`${props.type}:${props.id}:${props.status}`} {...props} />;
}

function CancellationAction({ id, type = "TimeTicker", status, hasChildren = false, onChanged }: CancelTickerButtonProps) {
  const mutation = useCancelTicker();
  const [confirmation, setConfirmation] = useState<(CancelTickerCommand & { hasChildren: boolean }) | null>(null);
  const [requested, setRequested] = useState(false);

  const running = status === "InProgress";
  const pending = type === "TimeTicker" && (status === "Idle" || status === "Queued");
  if (isReadOnly() || (!running && !pending)) return null;

  async function confirm() {
    if (!confirmation) return;
    try {
      const result = await mutation.mutateAsync(confirmation);
      setConfirmation(null);
      setRequested(result.outcome === "Requested");
      toast.success(result.outcome === "Requested" ? "Cancellation requested" : "Job cancelled", {
        description: result.outcome === "Requested"
          ? "The job stays running until its function exits."
          : result.affected > 1 ? `${result.affected} pending jobs cancelled, including child jobs.` : undefined,
      });
      onChanged?.();
    } catch (error) {
      toast.error("Could not cancel job", { description: error instanceof Error ? error.message : "Please refresh and try again." });
      onChanged?.();
    }
  }

  return <>
    <Button variant="destructive" size="sm" className="h-7 text-xs"
      disabled={mutation.isPending || requested}
      onClick={() => setConfirmation({ id, type, running, hasChildren })}>
      <Ban className="h-3 w-3 mr-1" />
      {requested ? "Cancellation requested" : running ? "Request cancellation" : "Cancel job"}
    </Button>
    <ConfirmDialog open={confirmation !== null} onOpenChange={(open) => { if (!open) setConfirmation(null); }}
      title={confirmation?.running ? "Request cancellation?" : "Cancel pending job?"}
      description={confirmation?.running
        ? "Signals this execution's cancellation token. The function must observe it and exit before its status changes. Work already performed is not undone. Child jobs follow their run conditions."
        : confirmation?.hasChildren
          ? "Cancels this job and its pending child jobs. Their records stay in history. Running and finished jobs are unaffected."
          : "Prevents this pending job from executing and keeps its record in history."}
      confirmLabel={confirmation?.running ? "Request cancellation" : "Cancel job"}
      dismissLabel="Go back" confirmVariant="destructive" isPending={mutation.isPending} onConfirm={confirm} />
  </>;
}
