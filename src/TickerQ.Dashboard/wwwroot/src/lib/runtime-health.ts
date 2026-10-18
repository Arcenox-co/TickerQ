import type { HostStatusDto, RuntimeDiagnostics } from "../services/api-types";

export function getRuntimeHealth({ host, diagnostics, persistLogs, unavailable = false }: {
  host?: HostStatusDto;
  diagnostics?: RuntimeDiagnostics;
  persistLogs?: boolean;
  unavailable?: boolean;
}) {
  const scanFailing = !!diagnostics?.lastFallbackFailureUtc && (
    !diagnostics.lastSuccessfulFallbackScanUtc ||
    Date.parse(diagnostics.lastFallbackFailureUtc) >= Date.parse(diagnostics.lastSuccessfulFallbackScanUtc)
  );
  const issues: string[] = [];
  if (host && !host.isRunning) issues.push("The scheduler is stopped. Check its state on Overview if this is unexpected.");
  if (scanFailing) issues.push("The latest overdue-work scan failed. Check the host logs for the cause; TickerQ will retry.");
  if (persistLogs && diagnostics?.executionLogs.writesAvailable === false)
    issues.push("Database log writes are failing. Check the database connection and host logs; unsaved lines remain subject to local buffer limits.");
  if (persistLogs && (diagnostics?.executionLogs.droppedUnflushedExecutions ?? 0) > 0)
    issues.push("Some log tails were dropped before all their lines were saved. Review buffer and retention limits in About. This count covers the time since the host started.");

  // Never present a cached success as current health after a failed refresh.
  const status = unavailable || !host || !diagnostics || persistLogs === undefined
    ? "unknown"
    : issues.length > 0 ? "warning"
    : !diagnostics.lastSuccessfulFallbackScanUtc ? "unknown" : "healthy";
  const label = unavailable ? "Status unavailable"
    : status === "warning" ? "Needs attention"
    : status === "healthy" ? "No reported issues"
    : "Waiting for diagnostics";
  return { status, label, issues, scanFailing } as const;
}
