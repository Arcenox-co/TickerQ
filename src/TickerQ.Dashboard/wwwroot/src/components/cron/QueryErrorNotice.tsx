import { AlertTriangle, RotateCw } from "lucide-react";
import { Button } from "@/components/ui/button";

/**
 * Inline banner for failed read queries. Mutations already toast their
 * errors; reads previously failed silently, leaving blank cards/tables with
 * no explanation — this gives the user the "why" and a retry affordance.
 */
export function QueryErrorNotice({
  message,
  onRetry,
}: {
  message?: string;
  onRetry?: () => void;
}) {
  return (
    <div
      role="alert"
      className="flex items-center justify-between gap-3 rounded-lg border border-status-error/30 bg-status-error/5 px-4 py-3 text-[12px]"
    >
      <div className="flex items-center gap-2 text-status-error">
        <AlertTriangle className="h-4 w-4 shrink-0" />
        <span>{message ?? "Failed to load data from the scheduler."}</span>
      </div>
      {onRetry && (
        <Button
          variant="outline"
          size="sm"
          className="h-7 text-[11px] bg-transparent border-border text-muted-foreground hover:text-foreground rounded-lg"
          onClick={onRetry}
        >
          <RotateCw className="h-3 w-3 mr-1.5" /> Retry
        </Button>
      )}
    </div>
  );
}
