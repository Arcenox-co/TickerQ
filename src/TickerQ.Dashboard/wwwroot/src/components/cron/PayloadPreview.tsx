import { useState } from "react";
import { ChevronDown, FileJson } from "lucide-react";
import {
  useCronOccurrenceRequest,
  useTimeTickerRequest,
} from "@/services/hooks";
import { cn } from "@/lib/utils";

function safeAtob(b64: string): string {
  try {
    return decodeURIComponent(escape(atob(b64)));
  } catch {
    try {
      return atob(b64);
    } catch {
      return b64;
    }
  }
}

/**
 * Collapsible read-only view of a ticker/occurrence request payload. The API
 * returns the payload as a raw JSON string; safeAtob keeps compatibility with
 * rows written by older clients that stored it base64-encoded.
 */
export function PayloadPreview({
  kind,
  id,
}: {
  kind: "time" | "cron-occurrence";
  id: string;
}) {
  const [open, setOpen] = useState(true);
  const timeQ = useTimeTickerRequest(kind === "time" ? id : null, open);
  const cronQ = useCronOccurrenceRequest(kind === "cron-occurrence" ? id : null, open);

  const data = kind === "time" ? timeQ.data : cronQ.data;
  const isLoading = kind === "time" ? timeQ.isLoading : cronQ.isLoading;
  const raw = data?.payload ?? null;
  const text = raw ? safeAtob(raw) : "";

  return (
    <div className="rounded-lg border border-border bg-card overflow-hidden">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="w-full flex items-center justify-between px-3 py-1.5 border-b border-border/50 text-[11px] text-muted-foreground"
      >
        <span className="flex items-center gap-1.5">
          <FileJson className="h-3 w-3 text-primary" />
          Request payload
          {text && <span className="text-muted-foreground/50 tabular-nums">{text.length} chars</span>}
        </span>
        <ChevronDown className={cn("h-3 w-3 transition-transform", !open && "-rotate-90")} />
      </button>
      {open && (
        <div className="p-2">
          {isLoading ? (
            <p className="px-1 text-[11px] text-muted-foreground/60">Loading…</p>
          ) : text ? (
            <pre className="font-mono text-[11px] leading-[18px] max-h-[180px] overflow-auto scrollbar-thin whitespace-pre-wrap break-all text-foreground/80">
              {text}
            </pre>
          ) : (
            <p className="px-1 text-[11px] text-muted-foreground/60">No payload.</p>
          )}
        </div>
      )}
    </div>
  );
}
