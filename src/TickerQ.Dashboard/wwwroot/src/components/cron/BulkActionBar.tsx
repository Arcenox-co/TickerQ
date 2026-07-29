import { type ReactNode } from "react";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";

/**
 * Slim bar shown above a table while rows are selected — hosts the bulk
 * action buttons the page provides plus a clear-selection affordance.
 */
export function BulkActionBar({
  count,
  onClear,
  children,
}: {
  count: number;
  onClear: () => void;
  children: ReactNode;
}) {
  return (
    <div className="flex items-center gap-3 rounded-lg border border-primary/25 bg-primary/5 px-3 py-2">
      <span className="text-[12px] font-medium tabular-nums">
        {count} selected
      </span>
      <div className="ml-auto flex items-center gap-2">
        {children}
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="h-7 gap-1 text-xs text-muted-foreground"
          onClick={onClear}
        >
          <X className="h-3 w-3" />
          Clear
        </Button>
      </div>
    </div>
  );
}
