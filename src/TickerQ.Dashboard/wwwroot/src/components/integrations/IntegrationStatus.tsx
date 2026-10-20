import { RefreshCw } from "lucide-react";
import { DataCard, PageHeader } from "@/components/cron/PageHeader";
import { Button } from "@/components/ui/button";
import type { IntegrationSnapshot } from "@/services/integration-types";

/** Observability remains available without configuration or delivery permissions. */
export function IntegrationStatus({ snapshot, refreshing, refresh }: {
  snapshot: IntegrationSnapshot;
  refreshing: boolean;
  refresh: () => void;
}) {
  const configuration = snapshot.configuration;
  const monitors = configuration.monitors ?? [];
  const destinationName = (id: string) => configuration.destinations.find((d) => d.id === id)?.name ?? "Removed integration";
  return <div className="space-y-5">
    <PageHeader title="Integrations" description="Monitor integration health and recent delivery outcomes." />
    <div className="flex items-center justify-between gap-3">
      <p className="text-xs text-muted-foreground">Viewing status. Changes require integration management permission.</p>
      <Button size="sm" variant="outline" disabled={refreshing} onClick={refresh}>
        <RefreshCw className="size-3.5" /> Refresh
      </Button>
    </div>
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
      {[
        ["Connections", configuration.destinations.length],
        ["Pending deliveries", snapshot.monitoringPendingDeliveries ?? 0],
        ["Dropped alerts", snapshot.dropped],
        ["Suppressed alerts", snapshot.suppressed],
      ].map(([label, count]) => <DataCard key={label} className="px-4 py-3">
        <p className="text-xs text-muted-foreground">{label}</p>
        <p className="mt-1 text-xl font-semibold tabular-nums">{count}</p>
      </DataCard>)}
    </div>
    <DataCard className="p-4">
      <h2 className="mb-3 text-sm font-semibold">Connections</h2>
      {configuration.destinations.length === 0 && <p className="text-xs text-muted-foreground">No integrations configured.</p>}
      <ul className="divide-y divide-border">
        {configuration.destinations.map((destination) => <li key={destination.id} className="flex items-center justify-between gap-3 py-3 text-xs">
          <span>{destination.name} <span className="text-muted-foreground">· {destination.provider}</span></span>
          <span>{destination.enabled ? "Enabled" : "Paused"}</span>
        </li>)}
      </ul>
    </DataCard>
    <DataCard className="p-4">
      <h2 className="mb-3 text-sm font-semibold">Monitor health</h2>
      {!snapshot.monitoringAvailable && <p className="text-xs text-muted-foreground">Monitoring is unavailable on this host.</p>}
      {snapshot.monitoringAvailable && monitors.length === 0 && <p className="text-xs text-muted-foreground">No monitors configured.</p>}
      <ul className="divide-y divide-border">
        {monitors.map((monitor) => {
          const state = snapshot.monitors?.find((item) => item.monitorId === monitor.id);
          return <li key={monitor.id} className="space-y-1 py-3 text-xs">
            <div className="flex justify-between gap-3"><span className="font-medium">{monitor.name} · {monitor.function}</span><span>{state?.status ?? "unknown"}</span></div>
            <p className="text-muted-foreground">{state?.message ?? "Waiting for the first check."}</p>
            {state?.lastCheckedUtc && <p className="text-muted-foreground">Last checked {new Date(state.lastCheckedUtc).toLocaleString()}</p>}
          </li>;
        })}
      </ul>
    </DataCard>
    <DataCard className="overflow-x-auto p-4">
      <h2 className="mb-3 text-sm font-semibold">Recent deliveries</h2>
      {snapshot.recentDeliveries.length === 0 ? <p className="text-xs text-muted-foreground">No recent deliveries.</p> :
        <table className="w-full text-left text-xs">
          <thead><tr className="border-b border-border text-muted-foreground"><th className="py-2 font-medium">Integration</th><th className="py-2 font-medium">Time</th><th className="py-2 font-medium">Outcome</th></tr></thead>
          <tbody>{snapshot.recentDeliveries.map((delivery, index) => <tr key={`${delivery.destinationId}:${delivery.atUtc}:${index}`} className="border-b border-border last:border-0">
            <td className="py-3 pr-4">{destinationName(delivery.destinationId)}</td><td className="py-3 pr-4">{new Date(delivery.atUtc).toLocaleString()}</td><td className="py-3">{delivery.outcome}</td>
          </tr>)}</tbody>
        </table>}
    </DataCard>
  </div>;
}
