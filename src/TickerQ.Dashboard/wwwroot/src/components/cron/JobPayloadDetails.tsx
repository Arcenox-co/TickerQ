import { useMemo, useState } from "react";
import { Braces, ChevronDown } from "lucide-react";
import { useTimeTickerRequest, useTimeTickerResult } from "@/services/hooks";
import { cn } from "@/lib/utils";

function decodePayload(value: string | null | undefined): string | null {
  if (!value) return null;
  try {
    const binary = atob(value);
    const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
    return new TextDecoder().decode(bytes);
  } catch {
    return value;
  }
}

function formatPayload(value: string | null): string | null {
  if (!value) return null;
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

function PayloadSection({
  title,
  value,
  metadata,
}: {
  title: string;
  value: string;
  metadata?: string | null;
}) {
  const [open, setOpen] = useState(true);
  return (
    <div className="overflow-hidden rounded-lg border border-border/70 bg-surface-0/45">
      <button
        type="button"
        onClick={() => setOpen(current => !current)}
        className="flex w-full items-center gap-2 px-3 py-2 text-left text-[10px] transition-colors hover:bg-surface-2/40"
      >
        <Braces className="h-3.5 w-3.5 shrink-0 text-primary" />
        <span className="font-medium text-foreground/90">{title}</span>
        {metadata && (
          <span className="min-w-0 truncate font-mono text-[9px] text-muted-foreground/60" title={metadata}>
            {metadata}
          </span>
        )}
        <ChevronDown className={cn("ml-auto h-3 w-3 shrink-0 text-muted-foreground transition-transform", !open && "-rotate-90")} />
      </button>
      {open && (
        <pre className="max-h-52 overflow-auto border-t border-border/50 px-3 py-2 font-mono text-[10px] leading-4 text-foreground/75 scrollbar-thin whitespace-pre-wrap break-all">
          {value}
        </pre>
      )}
    </div>
  );
}

export function JobPayloadDetails({
  id,
  parentResultId,
}: {
  id: string;
  parentResultId?: string | null;
}) {
  const request = useTimeTickerRequest(id);
  const parentResult = useTimeTickerResult(parentResultId ?? null, Boolean(parentResultId));
  const result = useTimeTickerResult(id);
  const input = useMemo(
    () => formatPayload(decodePayload(request.data?.payload)),
    [request.data?.payload]
  );
  const inputFromParent = useMemo(
    () => formatPayload(decodePayload(parentResult.data?.payload)),
    [parentResult.data?.payload]
  );
  const output = useMemo(
    () => formatPayload(decodePayload(result.data?.payload)),
    [result.data?.payload]
  );

  if (!input && !inputFromParent && !output) return null;

  return (
    <div className="space-y-2 border-t border-border/50 pt-3">
      {input && <PayloadSection title="Input request" value={input} metadata="application/json" />}
      {inputFromParent && (
        <PayloadSection
          title="Input from parent"
          value={inputFromParent}
          metadata={parentResult.data?.contractType ?? parentResult.data?.mediaType}
        />
      )}
      {output && (
        <PayloadSection
          title="Output result"
          value={output}
          metadata={result.data?.contractType ?? result.data?.mediaType}
        />
      )}
    </div>
  );
}
