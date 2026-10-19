import { useRef, useState } from "react";
import { Loader2, Play, RotateCcw, Server, Square } from "lucide-react";
import { toast } from "sonner";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { DataCard } from "@/components/cron/PageHeader";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { RuntimeHealthLink } from "@/components/cron/RuntimeHealthLink";
import { Button } from "@/components/ui/button";
import { isReadOnly } from "@/lib/runtime-config";
import { cn } from "@/lib/utils";
import { useHostStatus, useRestartHost, useStartHost, useStopHost } from "@/services/hooks";

const actions = {
  start: {
    label: "Start scheduler",
    pending: "Starting…",
    description: "Allow this host to schedule and accept work again. Due jobs may begin running.",
    success: "Scheduler start requested",
  },
  stop: {
    label: "Stop scheduler",
    pending: "Stopping…",
    description: "Stop this host from accepting new work. Queued and running jobs follow the host’s configured shutdown behavior.",
    success: "Scheduler stop requested",
  },
  restart: {
    label: "Restart scheduler",
    pending: "Restarting…",
    description: "Restart this host’s scheduling loop to re-read pending work. The application process stays running.",
    success: "Scheduler restart requested",
  },
} as const;
type HostAction = keyof typeof actions;

export function SchedulerHostCard() {
  const hostQuery = useHostStatus();
  const host = hostQuery.data;
  const startMutation = useStartHost();
  const stopMutation = useStopHost();
  const restartMutation = useRestartHost();
  const [pendingAction, setPendingAction] = useState<HostAction | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const inFlight = useRef(false);
  const readOnly = isReadOnly();
  const isPending = startMutation.isPending || stopMutation.isPending || restartMutation.isPending;
  const hostKnown = !!host && !hostQuery.isError;
  const actionAvailable = !!pendingAction && hostKnown && !readOnly &&
    (pendingAction === "start" ? !host.isRunning : host.isRunning);
  const definition = pendingAction ? actions[pendingAction] : null;
  const status = isPending && definition ? definition.pending
    : hostQuery.isError ? "Status unavailable"
    : !host ? "Loading status…"
    : host.isRunning ? "Running" : "Stopped";

  const requestAction = (action: HostAction) => {
    if (!hostKnown || readOnly || isPending) return;
    setActionError(null);
    setPendingAction(action);
  };

  const confirmAction = async () => {
    if (!pendingAction || !actionAvailable || isPending || inFlight.current) return;
    inFlight.current = true;
    setActionError(null);
    try {
      const mutation = pendingAction === "start" ? startMutation : pendingAction === "stop" ? stopMutation : restartMutation;
      await mutation.mutateAsync();
      toast.success(actions[pendingAction].success);
      setPendingAction(null);
    } catch (error) {
      setActionError(error instanceof Error ? error.message : "The request failed. Please try again.");
    } finally {
      inFlight.current = false;
    }
  };

  return (
    <>
      <DataCard>
        <div className="flex flex-wrap items-center justify-between gap-4 p-4 sm:p-5">
          <div className="flex min-w-0 items-start gap-3">
            <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
              <Server className="size-5" aria-hidden="true" />
            </div>
            <div className="min-w-0 space-y-1.5">
              <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
                <h1 className="text-lg font-semibold tracking-tight">Scheduler Host</h1>
                <span role="status" className={cn(
                  "inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-xs font-medium",
                  isPending || !hostKnown ? "bg-surface-2 text-muted-foreground"
                    : host.isRunning ? "bg-status-healthy/10 text-status-healthy" : "bg-status-warning/10 text-status-warning"
                )}>
                  {isPending || hostQuery.isPending ? <Loader2 className="size-3 animate-spin" aria-hidden="true" /> : <span className="size-1.5 rounded-full bg-current" aria-hidden="true" />}
                  {status}
                </span>
              </div>
              <p className="text-xs leading-relaxed text-muted-foreground">Manage scheduling on the host serving this dashboard.</p>
            </div>
          </div>

          {!readOnly && hostKnown && (
            <div className="flex w-full flex-wrap gap-2 sm:w-auto" role="group" aria-label="Scheduler controls">
              {host.isRunning ? (
                <>
                  <Button variant="outline" className="h-9 flex-1 gap-2 sm:flex-none" disabled={isPending} onClick={() => requestAction("restart")}>
                    <RotateCcw className="size-3.5" aria-hidden="true" /> Restart
                  </Button>
                  <Button variant="destructive" className="h-9 flex-1 gap-2 sm:flex-none" disabled={isPending} onClick={() => requestAction("stop")}>
                    <Square className="size-3.5" aria-hidden="true" /> Stop scheduler
                  </Button>
                </>
              ) : (
                <Button variant="default" className="h-9 flex-1 gap-2 sm:flex-none" disabled={isPending} onClick={() => requestAction("start")}>
                  <Play className="size-3.5" aria-hidden="true" /> Start scheduler
                </Button>
              )}
            </div>
          )}
        </div>

        {hostQuery.isError && (
          <div className="px-4 pb-4 sm:px-5">
            <QueryErrorNotice message="Scheduler status could not be refreshed. Check the current status before using host controls." onRetry={() => void hostQuery.refetch()} />
          </div>
        )}

        <div className="flex flex-wrap items-center justify-between gap-4 border-t border-border px-4 py-3 sm:px-5">
          <dl className="grid grid-cols-2 gap-5 sm:gap-8">
            <div>
              <dt className="min-h-8 text-xs text-muted-foreground">Current workers</dt>
              <dd className="mt-1">
                <span className="block text-base font-semibold tabular-nums">{hostKnown ? host.activeThreads.toLocaleString() : "—"}</span>
                <span className="mt-0.5 block text-[11px] text-muted-foreground">Includes idle workers</span>
              </dd>
            </div>
            <div>
              <dt className="min-h-8 text-xs text-muted-foreground">Maximum concurrent workers</dt>
              <dd className="mt-1">
                <span className="block text-base font-semibold tabular-nums">{hostKnown ? host.maxConcurrency.toLocaleString() : "—"}</span>
                <span className="mt-0.5 block text-[11px] text-muted-foreground">Scheduler capacity on this host</span>
              </dd>
            </div>
          </dl>
          <RuntimeHealthLink host={host} hostUnavailable={hostQuery.isError} />
        </div>
      </DataCard>

      <ConfirmDialog
        open={!!pendingAction}
        onOpenChange={open => {
          if (!open && !isPending) {
            setPendingAction(null);
            setActionError(null);
          }
        }}
        title={definition ? `${definition.label}?` : "Scheduler action"}
        description={definition?.description}
        confirmLabel={definition?.label}
        confirmVariant={pendingAction === "stop" ? "destructive" : "default"}
        isPending={isPending}
        confirmDisabled={!actionAvailable}
        onConfirm={confirmAction}
      >
        <p className="rounded-lg border border-border bg-card px-3 py-2 text-xs text-muted-foreground">Applies to the host serving this dashboard.</p>
        {actionError && <p role="alert" className="break-words text-xs leading-relaxed text-status-error">{actionError}</p>}
        {pendingAction && !isPending && !actionAvailable && (
          <p role="alert" className="text-xs leading-relaxed text-status-warning">The scheduler status changed or is unavailable. Close this dialog and check the current status before continuing.</p>
        )}
      </ConfirmDialog>
    </>
  );
}
