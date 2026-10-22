import { Copy, FileWarning } from "lucide-react";
import { parseExceptionDetails } from "@/lib/cron/log-format";
import { Button } from "@/components/ui/button";

export function ExceptionDetails({ raw }: { raw?: string | null }) {
  if (!raw) return null;

  const details = parseExceptionDetails(raw);
  const source = details.sourceFile
    ? `${details.sourceFile}${details.sourceLine ? `:${details.sourceLine}` : ""}`
    : null;
  const hasDiagnostics = Boolean(details.type || details.method || source || details.stackTrace);
  if (!hasDiagnostics) return null;

  const copyText = [
    details.type,
    details.message,
    source ? `Source: ${source}` : null,
    details.method ? `Method: ${details.method}` : null,
    details.stackTrace,
  ].filter(Boolean).join("\n");

  return (
    <details className="group border-t border-border/50 bg-status-error/[0.025] px-3 py-2 text-[11px]">
      <summary className="flex cursor-pointer list-none items-center gap-2 text-status-error marker:content-none">
        <FileWarning className="size-3.5 shrink-0" />
        <span className="font-medium">Exception details</span>
        {details.type && (
          <span className="min-w-0 truncate font-mono text-muted-foreground">
            {details.type}
          </span>
        )}
        {source && (
          <span className="ml-auto shrink-0 font-mono text-muted-foreground/80">
            {source}
          </span>
        )}
      </summary>

      <div className="mt-2 space-y-2 border-l border-status-error/25 pl-3">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 space-y-1">
            <p className="break-words text-foreground">{details.message}</p>
            {details.method && (
              <p className="break-all font-mono text-muted-foreground">
                {details.method}
              </p>
            )}
          </div>
          <Button
            type="button"
            variant="ghost"
            size="xs"
            className="h-6 shrink-0 text-muted-foreground"
            onClick={() => { void navigator.clipboard.writeText(copyText).catch(() => undefined); }}
            title="Copy exception details"
          >
            <Copy className="size-3" />
            Copy
          </Button>
        </div>

        {details.stackTrace && (
          <pre className="max-h-56 overflow-auto whitespace-pre-wrap break-words rounded-md border border-border/60 bg-surface-0 p-2 font-mono text-[10px] leading-4 text-muted-foreground scrollbar-thin">
            {details.stackTrace}
          </pre>
        )}
      </div>
    </details>
  );
}
