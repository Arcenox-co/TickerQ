import { useId, type ReactNode } from "react";
import { Info } from "lucide-react";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";

export function Detail({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
  const hintId = useId();
  return (
    <div className="grid gap-1 border-b border-border/60 px-4 py-3 last:border-0 sm:grid-cols-[minmax(0,1fr)_minmax(0,1.5fr)] sm:items-center sm:gap-4">
      <dt className="flex items-center gap-1 text-xs text-muted-foreground">
        <span>{label}</span>
        {hint && (
          <Popover>
            <PopoverTrigger asChild>
              <button
                type="button"
                aria-label={`Explain ${label.toLowerCase()}`}
                className="inline-flex size-6 shrink-0 items-center justify-center rounded-md transition-colors hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <Info className="size-3.5" aria-hidden="true" />
              </button>
            </PopoverTrigger>
            <PopoverContent
              align="start"
              side="top"
              sideOffset={8}
              collisionPadding={12}
              aria-label={`${label} information`}
              aria-describedby={hintId}
              className="max-w-[calc(100vw-1.5rem)] text-xs leading-relaxed"
            >
              <p id={hintId}>{hint}</p>
            </PopoverContent>
          </Popover>
        )}
      </dt>
      <dd className="min-w-0 break-words text-sm font-medium">
        {children}
      </dd>
    </div>
  );
}
