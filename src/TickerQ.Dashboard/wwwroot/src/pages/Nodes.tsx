import { useEffect, useMemo, useState } from "react";
import { AlertTriangle, ArrowUpRight, CheckCircle2, RefreshCw, Server, ServerOff } from "lucide-react";
import { DataCard, DataCardHeader, PageHeader } from "@/components/cron/PageHeader";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { formatAbsolute } from "@/lib/cron/format";
import { formatApplicationNamespace, formatFriendlyVersion, getRuntimeConfig } from "@/lib/runtime-config";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";
import { useNodes } from "@/services/hooks";
import type { NodeDto } from "@/services/api-types";

const PAGE_SIZE = 8;

function safeDashboardUrl(value: string | null) {
  if (!value) return null;
  try {
    const parsed = new URL(value);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.href : null;
  } catch { return null; }
}

function health(node: NodeDto) {
  if (node.healthStatus === "Healthy") return { label: node.isRunning ? "Healthy" : "Scheduler stopped", icon: CheckCircle2, tone: "text-status-healthy" };
  if (node.healthStatus === "Degraded") return { label: "Heartbeat delayed", icon: AlertTriangle, tone: "text-status-warning" };
  return { label: "Offline", icon: ServerOff, tone: "text-status-error" };
}

function nodeOrder(node: NodeDto) {
  if (node.healthStatus === "Healthy") return node.isRunning ? 0 : 1;
  return node.healthStatus === "Degraded" ? 2 : 3;
}

export default function NodesPage() {
  const query = useNodes();
  const { timezone } = useTimezone();
  const nodes = query.data ?? [];
  const applicationNamespace = formatApplicationNamespace(getRuntimeConfig().applicationNamespace);
  const [page, setPage] = useState(1);
  const sortedNodes = useMemo(() => [...nodes].sort((left, right) => {
    const statusDifference = nodeOrder(left) - nodeOrder(right);
    if (statusDifference !== 0) return statusDifference;
    return (right.lastHeartbeat ?? "").localeCompare(left.lastHeartbeat ?? "");
  }), [nodes]);
  const pageCount = Math.max(1, Math.ceil(sortedNodes.length / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount);
  const visibleNodes = sortedNodes.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);

  useEffect(() => {
    if (page > pageCount) setPage(pageCount);
  }, [page, pageCount]);

  return <div className="mx-auto max-w-7xl space-y-5">
    <PageHeader title="Nodes" count={nodes.length} description="Scheduler instances sharing the configured application namespace." className="flex-wrap">
      <Badge variant="outline" className="h-8 max-w-full gap-1.5 rounded-md px-2.5 font-normal" title={`Application namespace: ${applicationNamespace}`}>
        <span className="text-muted-foreground">Namespace</span>
        <span className="max-w-56 truncate font-mono">{applicationNamespace}</span>
      </Badge>
      <Button variant="outline" size="sm" onClick={() => void query.refetch()} disabled={query.isFetching}>
        <RefreshCw className={cn("mr-1.5 size-3.5", query.isFetching && "animate-spin")} aria-hidden="true" /> Refresh
      </Button>
    </PageHeader>

    {query.isError && <QueryErrorNotice message="The node registry could not be refreshed." onRetry={() => void query.refetch()} />}

    <DataCard className="px-4 py-3 text-xs leading-relaxed text-muted-foreground">
      Node links are optional operator-configured dashboard addresses. Cancellation and future node controls use the shared operational store, so private HTTP connectivity is not required.
    </DataCard>

    {!query.isPending && nodes.length === 0 && <DataCard className="p-8 text-center">
      <Server className="mx-auto size-6 text-muted-foreground" aria-hidden="true" />
      <p className="mt-3 text-sm font-medium">No cluster-capable nodes reported yet</p>
      <p className="mt-1 text-xs text-muted-foreground">The configured operational provider may not support node discovery, or its first heartbeat is still pending.</p>
    </DataCard>}

    <div className="grid items-start gap-4 xl:grid-cols-2">
      {visibleNodes.map(node => {
        const state = health(node);
        const StateIcon = state.icon;
        const dashboardUrl = safeDashboardUrl(node.dashboardUrl);
        const offline = node.healthStatus === "Down";
        return <DataCard
          key={node.nodeKey}
          className={cn("transition-[opacity,filter]", offline && "opacity-60 saturate-50 hover:opacity-80")}
        >
          <DataCardHeader title={node.nodeName || node.nodeIdentifier}>
            <div className="flex items-center gap-2">
              <Badge variant="outline" className="gap-1.5 font-normal">
                <StateIcon className={cn("size-3", state.tone)} aria-hidden="true" /> {state.label}
              </Badge>
              {dashboardUrl && <Button asChild variant="outline" size="sm" className="h-7 gap-1.5 text-xs">
                <a href={dashboardUrl} target="_blank" rel="noreferrer">Open dashboard <ArrowUpRight className="size-3" /></a>
              </Button>}
            </div>
          </DataCardHeader>

          <div className="grid grid-cols-2 border-t border-border sm:grid-cols-4">
            <Metric label="Running" value={`${node.activeJobs.toLocaleString()} / ${node.maxConcurrency.toLocaleString()}`} />
            <Metric label="Queued" value={node.queuedJobs.toLocaleString()} />
            <Metric label="Function waits" value={node.waitingForFunction.toLocaleString()} />
            <Metric label="Retry delays" value={node.retryingJobs.toLocaleString()} />
          </div>

          <dl className="grid gap-x-5 gap-y-3 border-t border-border px-4 py-3 text-xs sm:grid-cols-2">
            <Meta label="Node" value={node.nodeIdentifier} mono />
            <Meta label="Instance" value={node.nodeKey.slice(0, 12)} mono />
            <Meta label="Version" value={formatFriendlyVersion(node.version) || "Unknown"} />
            <Meta label="Functions" value={node.functionCount.toLocaleString()} />
            <Meta label="Started" value={node.startedAt ? formatAbsolute(node.startedAt, timezone) : "Unknown"} />
            <Meta label="Last heartbeat" value={node.lastHeartbeat ? formatAbsolute(node.lastHeartbeat, timezone) : "Never"} />
          </dl>
        </DataCard>;
      })}
    </div>

    {nodes.length > PAGE_SIZE && <DataCard className="flex flex-col items-center justify-between gap-3 px-4 py-3 sm:flex-row">
      <p className="text-xs tabular-nums text-muted-foreground" role="status">
        Showing {(currentPage - 1) * PAGE_SIZE + 1}–{Math.min(currentPage * PAGE_SIZE, nodes.length)} of {nodes.length.toLocaleString()} nodes
      </p>
      <div className="flex items-center gap-2">
        <Button variant="outline" size="sm" disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}>Previous</Button>
        <span className="min-w-20 text-center text-xs tabular-nums text-muted-foreground">Page {currentPage} of {pageCount}</span>
        <Button variant="outline" size="sm" disabled={currentPage === pageCount} onClick={() => setPage(currentPage + 1)}>Next</Button>
      </div>
    </DataCard>}
  </div>;
}

function Metric({ label, value }: { label: string; value: string }) {
  return <div className="border-b border-r border-border px-4 py-3 last:border-r-0 sm:border-b-0">
    <p className="text-[11px] text-muted-foreground">{label}</p>
    <p className="mt-1 text-base font-semibold tabular-nums">{value}</p>
  </div>;
}

function Meta({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return <div className="min-w-0">
    <dt className="text-muted-foreground">{label}</dt>
    <dd className={cn("mt-0.5 truncate text-foreground", mono && "font-mono")}>{value}</dd>
  </div>;
}
