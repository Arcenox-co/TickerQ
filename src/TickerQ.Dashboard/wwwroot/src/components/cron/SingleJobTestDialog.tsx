import { Loader2, TestTube2 } from "lucide-react";

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { StatusBadge } from "./StatusBadges";
import { LogsPanel } from "./LogsPanel";
import type { TestRunResponse } from "@/services/api-types";

function decodePayload(payload: string | null): string | null {
  if (!payload) return null;
  try {
    const bytes = Uint8Array.from(atob(payload), character => character.charCodeAt(0));
    const value = new TextDecoder().decode(bytes);
    try {
      return JSON.stringify(JSON.parse(value), null, 2);
    } catch {
      return value;
    }
  } catch {
    return payload;
  }
}

export function SingleJobTestDialog({
  open,
  pending,
  result,
  error,
  functionName,
  onClose,
}: {
  open: boolean;
  pending: boolean;
  result: TestRunResponse | null;
  error: string | null;
  functionName: string;
  onClose: () => void;
}) {
  const output = decodePayload(result?.result ?? null);
  return (
    <Dialog open={open} onOpenChange={next => { if (!next && !pending) onClose(); }}>
      <DialogContent className="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-base">
            <TestTube2 className="h-4 w-4 text-primary" /> Test run
          </DialogTitle>
          <DialogDescription className="text-xs leading-relaxed">
            Executes <span className="font-mono text-foreground">{functionName}</span> without saving
            a ticker or occurrence. This is a real function invocation and may change application data or call external services.
          </DialogDescription>
        </DialogHeader>

        {pending && (
          <div className="flex min-h-32 flex-col items-center justify-center gap-3 rounded-lg border border-border bg-card text-sm text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin text-primary" />
            Executing with the form request…
          </div>
        )}

        {!pending && error && (
          <div className="rounded-lg border border-status-error/30 bg-status-error/5 p-3 text-sm text-status-error">
            {error}
          </div>
        )}

        {!pending && result && (
          <div className="space-y-3">
            <div className="flex items-center justify-between rounded-lg border border-border bg-card px-3 py-2">
              <StatusBadge status={result.status} />
              <span className="text-xs tabular-nums text-muted-foreground">
                {result.elapsedMilliseconds.toLocaleString()} ms
              </span>
            </div>
            {output && (
              <div className="overflow-hidden rounded-lg border border-border bg-card">
                <div className="border-b border-border px-3 py-1.5 text-[11px] font-medium text-muted-foreground">
                  Output
                </div>
                <pre className="max-h-56 overflow-auto whitespace-pre-wrap break-words p-3 font-mono text-[11px]">{output}</pre>
              </div>
            )}
            <LogsPanel
              functionName={functionName}
              status={result.status}
              tickerId={result.executionId}
              exceptionMessage={result.error}
            />
          </div>
        )}

        <div className="flex justify-end">
          <Button type="button" variant="outline" size="sm" disabled={pending} onClick={onClose}>
            Close
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
