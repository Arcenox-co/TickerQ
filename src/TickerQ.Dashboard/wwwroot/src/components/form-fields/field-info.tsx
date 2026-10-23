import { useState, type ReactNode } from "react";
import { Info } from "lucide-react";

import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";

export function FieldInfo({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);

  return (
    <Tooltip open={open}>
      <TooltipTrigger asChild>
        <button
          type="button"
          aria-label="More information"
          aria-expanded={open}
          onPointerEnter={() => setOpen(true)}
          onPointerLeave={() => setOpen(false)}
          onClick={() => setOpen(current => !current)}
          onBlur={() => setOpen(false)}
          onKeyDown={event => {
            if (event.key === "Escape") setOpen(false);
          }}
          className="inline-flex size-5 shrink-0 items-center justify-center rounded-full text-muted-foreground/70 transition-colors hover:bg-surface-2 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <Info className="size-3.5" />
        </button>
      </TooltipTrigger>
      <TooltipContent side="top" className="max-w-[280px] leading-relaxed">
        {children}
      </TooltipContent>
    </Tooltip>
  );
}
