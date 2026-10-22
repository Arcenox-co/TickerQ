import { useState } from "react";
import { CalendarClock, Loader2, Play } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";

type ActivationTiming = "now" | "scheduled";

export function ActivateDraftDialog({
  open,
  draftName,
  initialExecutionTime = "",
  pending = false,
  onOpenChange,
  onActivate,
}: {
  open: boolean;
  draftName?: string;
  initialExecutionTime?: string;
  pending?: boolean;
  onOpenChange: (open: boolean) => void;
  onActivate: (executionTime: string | null) => void | Promise<unknown>;
}) {
  return (
    <Dialog open={open} onOpenChange={pending ? undefined : onOpenChange}>
      {open && (
        <ActivateDraftDialogContent
          key={`${draftName ?? "draft"}:${initialExecutionTime}`}
          draftName={draftName}
          initialExecutionTime={initialExecutionTime}
          pending={pending}
          onCancel={() => onOpenChange(false)}
          onActivate={onActivate}
        />
      )}
    </Dialog>
  );
}

function ActivateDraftDialogContent({
  draftName,
  initialExecutionTime,
  pending,
  onCancel,
  onActivate,
}: {
  draftName?: string;
  initialExecutionTime: string;
  pending: boolean;
  onCancel: () => void;
  onActivate: (executionTime: string | null) => void | Promise<unknown>;
}) {
  const [timing, setTiming] = useState<ActivationTiming>(
    initialExecutionTime ? "scheduled" : "now",
  );
  const [executionTime, setExecutionTime] = useState(initialExecutionTime);

  const scheduledTimestamp = executionTime ? new Date(executionTime).getTime() : Number.NaN;
  const scheduledTimeIsInvalid = timing === "scheduled" && !Number.isFinite(scheduledTimestamp);

  return (
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <div className="mb-1 flex h-9 w-9 items-center justify-center rounded-lg bg-primary/10 text-primary">
            <CalendarClock className="h-4 w-4" />
          </div>
          <DialogTitle>Activate draft</DialogTitle>
          <DialogDescription>
            Review when {draftName ? `“${draftName}”` : "this job or chain"} should enter the scheduler.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-1">
          <div className="grid grid-cols-2 gap-2" role="group" aria-label="Activation timing">
            <button
              type="button"
              aria-pressed={timing === "now"}
              onClick={() => setTiming("now")}
              className={cn(
                "rounded-lg border p-3 text-left transition-colors",
                timing === "now"
                  ? "border-primary/50 bg-primary/[0.07] text-foreground"
                  : "border-border bg-surface-0/40 text-muted-foreground hover:border-primary/30",
              )}
            >
              <span className="flex items-center gap-1.5 text-xs font-semibold">
                <Play className="h-3.5 w-3.5" /> Run now
              </span>
              <span className="mt-1 block text-[10px] leading-4 text-muted-foreground">
                Start as soon as capacity is available.
              </span>
            </button>
            <button
              type="button"
              aria-pressed={timing === "scheduled"}
              onClick={() => setTiming("scheduled")}
              className={cn(
                "rounded-lg border p-3 text-left transition-colors",
                timing === "scheduled"
                  ? "border-primary/50 bg-primary/[0.07] text-foreground"
                  : "border-border bg-surface-0/40 text-muted-foreground hover:border-primary/30",
              )}
            >
              <span className="flex items-center gap-1.5 text-xs font-semibold">
                <CalendarClock className="h-3.5 w-3.5" /> Schedule for
              </span>
              <span className="mt-1 block text-[10px] leading-4 text-muted-foreground">
                Keep or change the draft's execution time.
              </span>
            </button>
          </div>

          {timing === "scheduled" && (
            <div className="space-y-1.5">
              <Label htmlFor="draft-activation-time" className="text-xs">
                Execution time <span className="font-normal text-muted-foreground">(your local time)</span>
              </Label>
              <Input
                id="draft-activation-time"
                type="datetime-local"
                step={1}
                value={executionTime}
                aria-invalid={scheduledTimeIsInvalid}
                onChange={event => setExecutionTime(event.target.value)}
              />
              {scheduledTimeIsInvalid ? (
                <p className="text-[11px] leading-4 text-status-error">Choose a valid execution time.</p>
              ) : (
                <p className="text-[11px] leading-4 text-muted-foreground">
                  The saved time is prefilled. You can change it before activation; a past time starts as soon as capacity is available.
                </p>
              )}
            </div>
          )}
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={onCancel} disabled={pending}>
            Cancel
          </Button>
          <Button
            type="button"
            variant="gradient"
            disabled={pending || scheduledTimeIsInvalid}
            onClick={() => void onActivate(timing === "now" ? null : executionTime)}
          >
            {pending && <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />}
            {pending ? "Activating…" : timing === "now" ? "Activate now" : "Activate and schedule"}
          </Button>
        </DialogFooter>
      </DialogContent>
  );
}
