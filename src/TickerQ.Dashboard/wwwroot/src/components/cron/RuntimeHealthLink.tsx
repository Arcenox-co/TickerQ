import { Link } from "react-router-dom";
import { ArrowUpRight, Activity } from "lucide-react";
import { getRuntimeHealth } from "@/lib/runtime-health";
import { useDashboardOptions, useRuntimeDiagnostics } from "@/services/hooks";
import type { HostStatusDto } from "@/services/api-types";
import { cn } from "@/lib/utils";

export function RuntimeHealthLink({ host, hostUnavailable }: { host?: HostStatusDto; hostUnavailable: boolean }) {
  const diagnostics = useRuntimeDiagnostics();
  const options = useDashboardOptions();
  const health = getRuntimeHealth({
    host,
    diagnostics: diagnostics.data,
    persistLogs: options.data ? !!(options.data.executionLogs?.enabled && options.data.executionLogs.persistToDatabase) : undefined,
    unavailable: hostUnavailable || diagnostics.isError || options.isError,
  });

  return (
    <Link to="/diagnostics" className="inline-flex max-w-full flex-wrap items-center gap-2 rounded-md border border-border px-3 py-2 text-xs transition-colors hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
      <Activity className="size-3.5 text-muted-foreground" aria-hidden="true" />
      <span>Runtime health</span>
      <span className={cn(health.status === "warning" ? "text-status-warning" : health.status === "healthy" ? "text-status-healthy" : "text-muted-foreground")}>{health.label}</span>
      <ArrowUpRight className="size-3.5 text-muted-foreground" aria-hidden="true" />
    </Link>
  );
}
