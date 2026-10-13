import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Copy, RotateCw, Timer } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/cron/PageHeader";
import { DataTable } from "@/components/cron/DataTable";
import { StatusBadge, TypeBadge } from "@/components/cron/StatusBadges";
import {
  FilterChip,
  FilterPopover,
  SearchInput,
} from "@/components/cron/TableToolbar";
import {
  ExecutionVolumeChart,
  bucketsToVolume,
  emptyVolumeData,
} from "@/components/cron/ExecutionVolumeChart";
import { LogsSidePanel } from "@/components/cron/LogsSidePanel";
import { BulkActionBar } from "@/components/cron/BulkActionBar";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import {
  useAllFunctions,
  useBulkRetryExecutions,
  useExecutions,
  useOverallStatuses,
  useRunCronTicker,
  useRunTimeTicker,
  useTimeTickersGraph,
} from "@/services/hooks";
import type {
  ExecutionFlatDto,
  ExecutionQueryFilter,
  TickerStatus,
} from "@/services/api-types";
import { formatDuration } from "@/lib/cron/format";
import { TimeCell } from "@/components/cron/TimeCell";
import { isReadOnly } from "@/lib/runtime-config";

const PAGE_SIZE = 25;

/** Terminal-failure statuses that offer the Retry affordance. */
const RETRYABLE_STATUSES: TickerStatus[] = ["Failed", "Cancelled", "Skipped"];

export default function ExecutionsPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<TickerStatus[]>([]);
  const [functionFilter, setFunctionFilter] = useState("");
  const [page, setPage] = useState(1);
  const [activeRow, setActiveRow] = useState<ExecutionFlatDto | null>(null);
  const [retryTarget, setRetryTarget] = useState<ExecutionFlatDto | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [confirmBulkRetry, setConfirmBulkRetry] = useState(false);

  useEffect(() => {
    setPage(1);
  }, [search, statusFilter, functionFilter]);

  // Selection is page-scoped: navigating or refiltering drops it so bulk
  // actions never operate on rows that are no longer visible.
  useEffect(() => {
    setSelected(new Set());
  }, [page, search, statusFilter, functionFilter]);

  const filter: ExecutionQueryFilter = useMemo(
    () => ({
      statuses: statusFilter.length > 0 ? statusFilter : null,
      functionName: functionFilter || null,
      search: search || null,
      pageNumber: page,
      pageSize: PAGE_SIZE,
      sortDescending: true,
    }),
    [search, statusFilter, functionFilter, page]
  );

  const listQuery = useExecutions(filter);
  const data = listQuery.data;
  const readOnly = isReadOnly();
  const runTimeMutation = useRunTimeTicker();
  const runCronMutation = useRunCronTicker();
  const { data: graph } = useTimeTickersGraph(7, 0);
  const { data: allFns } = useAllFunctions();
  const { data: overall } = useOverallStatuses();
  const volumeData = useMemo(
    () => (graph ? bucketsToVolume(graph) : emptyVolumeData()),
    [graph]
  );

  const items = data?.items ?? [];

  // Aggregate stats (Total/Success/Failed/Active across ALL executions) +
  // page-local Avg Duration. Hub renders this strip above the table.
  const stats = useMemo(() => {
    const map = new Map((overall ?? []).map((s) => [s.status, s.count] as const));
    const sum = (...ks: TickerStatus[]) => ks.reduce((s, k) => s + (map.get(k) ?? 0), 0);
    const total = (overall ?? []).reduce((s, x) => s + x.count, 0);
    const finished = items.filter((r) => r.executedAt && r.elapsedTime > 0);
    const avg = finished.length === 0 ? null : finished.reduce((s, r) => s + r.elapsedTime, 0) / finished.length;
    return {
      total,
      success: sum("Done", "DueDone"),
      failed: sum("Failed"),
      active: sum("Idle", "Queued", "InProgress"),
      avg,
    };
  }, [overall, items]);

  const distinctFunctions = useMemo(
    () => (allFns ?? []).map((f) => f.functionName),
    [allFns]
  );

  // Retry = run again on demand. Time executions re-run the ticker itself;
  // cron occurrences re-run the parent schedule (a fresh occurrence is
  // created — the failed row stays in history, matching how Hangfire/Sidekiq
  // requeue works). The button opens a confirm dialog; this fires on confirm.
  function handleRetryConfirm() {
    const r = retryTarget;
    if (!r) return;
    const opts = {
      onSuccess: () =>
        toast.success("Retry queued", { description: r.functionName }),
      onError: (err: unknown) =>
        toast.error("Failed to retry", {
          description: err instanceof Error ? err.message : String(err),
        }),
      onSettled: () => setRetryTarget(null),
    };
    if (r.type === "TimeTicker") runTimeMutation.mutate(r.id, opts);
    else if (r.cronTickerId) runCronMutation.mutate(r.cronTickerId, opts);
  }

  const bulkRetryMutation = useBulkRetryExecutions();

  // Of the selection, only terminal-failure rows can actually be retried.
  const selectedRetryable = useMemo(
    () =>
      items.filter(
        (r) =>
          selected.has(r.id) &&
          RETRYABLE_STATUSES.includes(r.status) &&
          (r.type === "TimeTicker" || r.cronTickerId != null)
      ),
    [items, selected]
  );

  async function handleBulkRetryConfirm() {
    if (selectedRetryable.length === 0) return;
    try {
      const res = await bulkRetryMutation.mutateAsync(
        selectedRetryable.map((r) => ({
          id: r.id,
          type: r.type,
          cronTickerId: r.cronTickerId,
        }))
      );
      toast.success(`Retry queued for ${res.affected} item(s)`);
      setSelected(new Set());
    } catch (err) {
      toast.error("Failed to retry", {
        description: err instanceof Error ? err.message : String(err),
      });
    } finally {
      setConfirmBulkRetry(false);
    }
  }

  const columns = useMemo<ColumnDef<ExecutionFlatDto>[]>(
    () => [
      {
        accessorKey: "id",
        header: "ID",
        cell: ({ row }) => (
          <div className="flex items-center gap-1">
            <span className="font-mono text-[11px]">
              {row.original.id.slice(0, 12)}
            </span>
            <button
              type="button"
              className="text-muted-foreground/60 hover:text-foreground"
              onClick={(e) => {
                e.stopPropagation();
                void navigator.clipboard.writeText(row.original.id);
                toast.success("Copied");
              }}
              title="Copy ID"
            >
              <Copy className="h-3 w-3" />
            </button>
          </div>
        ),
      },
      {
        accessorKey: "type",
        header: "Type",
        cell: ({ row }) => (
          <TypeBadge
            type={row.original.type === "CronOccurrence" ? "Cron" : "Time"}
            chainChildCount={row.original.childCount}
            onChainClick={
              row.original.type === "TimeTicker" && row.original.childCount > 0
                ? () =>
                    navigate(
                      `/time-tickers/${row.original.id}/flowchart?from=executions`
                    )
                : undefined
            }
          />
        ),
      },
      {
        accessorKey: "functionName",
        header: "Function",
        cell: ({ row }) => (
          <span className="font-mono text-[11px] font-medium">
            {row.original.functionName}
          </span>
        ),
      },
      {
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) => <StatusBadge status={row.original.status} />,
      },
      {
        accessorKey: "scheduledFor",
        header: "Scheduled",
        cell: ({ row }) => <TimeCell value={row.original.scheduledFor} />,
      },
      {
        accessorKey: "elapsedTime",
        header: "Duration",
        cell: ({ row }) => (
          <span className="tabular-nums text-[11px]">
            {row.original.executedAt
              ? formatDuration(row.original.elapsedTime)
              : "—"}
          </span>
        ),
      },
      {
        accessorKey: "retryCount",
        header: "Retries",
        cell: ({ row }) =>
          row.original.retries > 0 ? (
            <span className="tabular-nums text-[11px]">
              {row.original.retryCount}/{row.original.retries}
            </span>
          ) : (
            <span className="text-muted-foreground text-[11px]">—</span>
          ),
      },
      {
        id: "lockHolder",
        header: "Lock Holder",
        cell: ({ row }) => (
          <span className="text-[11px] font-mono text-muted-foreground">
            {row.original.lockHolder ?? "—"}
          </span>
        ),
      },
      ...(!readOnly
        ? [
            {
              id: "actions",
              header: "",
              cell: ({ row }: { row: { original: ExecutionFlatDto } }) => {
                const r = row.original;
                const canRetry =
                  RETRYABLE_STATUSES.includes(r.status) &&
                  (r.type === "TimeTicker" || r.cronTickerId != null);
                if (!canRetry) return null;
                return (
                  <button
                    type="button"
                    title={
                      r.type === "TimeTicker"
                        ? "Retry: run this ticker again now"
                        : "Retry: run the parent cron schedule now"
                    }
                    className="inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[11px] text-muted-foreground hover:text-foreground hover:bg-surface-2 transition-colors"
                    onClick={(e) => {
                      e.stopPropagation();
                      setRetryTarget(r);
                    }}
                  >
                    <RotateCw className="h-3 w-3" /> Retry
                  </button>
                );
              },
            } satisfies ColumnDef<ExecutionFlatDto>,
          ]
        : []),
    ],
    [navigate, readOnly]
  );

  const toolbar = (
    <div className="space-y-2">
      <div className="flex items-center gap-2 flex-wrap">
        <SearchInput
          value={search}
          onChange={setSearch}
          placeholder="Search by function or ID…"
        />
        <FilterPopover
          statusFilter={statusFilter}
          onStatusChange={setStatusFilter}
          functionFilter={functionFilter}
          onFunctionChange={setFunctionFilter}
          functions={distinctFunctions}
          onClear={() => {
            setStatusFilter([]);
            setFunctionFilter("");
          }}
        />
      </div>

      {(statusFilter.length > 0 || functionFilter) && (
        <div className="flex items-center gap-2 flex-wrap">
          {statusFilter.length > 0 && (
            <FilterChip
              label="Status"
              value={statusFilter.join(", ")}
              onRemove={() => setStatusFilter([])}
            />
          )}
          {functionFilter && (
            <FilterChip
              label="Function"
              value={functionFilter}
              onRemove={() => setFunctionFilter("")}
            />
          )}
        </div>
      )}
    </div>
  );

  return (
    <div className="space-y-5">
      <ExecutionVolumeChart
        data={volumeData}
        title="Executions (7d)"
        gradientPrefix="ex"
      />

      <PageHeader
        title="Executions"
        count={data?.totalCount ?? items.length}
        description="Recent ticker executions, retries, and outcomes."
      />

      <div className="flex flex-wrap items-center gap-4 rounded-lg border border-border bg-surface-1/40 px-3 py-2">
        <StatTile icon={<span className="text-muted-foreground/60 text-[10px] uppercase tracking-wider">Total</span>} value={stats.total} />
        <StatTile dotClass="bg-status-healthy" label="Success" value={stats.success} />
        <StatTile dotClass="bg-status-error" label="Failed" value={stats.failed} />
        <StatTile dotClass="bg-status-warning" label="Active" value={stats.active} />
        <div className="ml-auto flex items-center gap-1.5">
          <Timer className="h-3 w-3 text-muted-foreground/60" />
          <span className="text-[10px] uppercase tracking-wider text-muted-foreground/60">Avg Duration</span>
          <span className="tabular-nums text-[12px] font-medium">
            {stats.avg != null ? formatDuration(stats.avg) : "—"}
          </span>
        </div>
      </div>

      {listQuery.isError && (
        <QueryErrorNotice
          message="Failed to load executions."
          onRetry={() => void listQuery.refetch()}
        />
      )}

      {!readOnly && selected.size > 0 && (
        <BulkActionBar count={selected.size} onClear={() => setSelected(new Set())}>
          <Button
            type="button"
            variant="gradient"
            size="sm"
            className="h-7 gap-1 text-xs"
            disabled={selectedRetryable.length === 0 || bulkRetryMutation.isPending}
            onClick={() => setConfirmBulkRetry(true)}
          >
            <RotateCw className="h-3 w-3" />
            Retry {selectedRetryable.length > 0 ? `(${selectedRetryable.length})` : ""}
          </Button>
        </BulkActionBar>
      )}

      <DataTable
        columns={columns}
        data={items}
        toolbar={toolbar}
        emptyMessage="No executions match the current filters."
        onRowClick={(row) => setActiveRow(row)}
        selectedIds={!readOnly ? selected : undefined}
        onSelectionChange={!readOnly ? setSelected : undefined}
        page={page}
        totalPages={data?.totalPages ?? 1}
        totalCount={data?.totalCount ?? items.length}
        onPageChange={setPage}
        isLoading={listQuery.isLoading}
        isFetching={listQuery.isFetching}
        skeletonRows={6}
      />

      <LogsSidePanel
        target={
          activeRow
            ? {
                id: activeRow.id,
                function: activeRow.functionName,
                status: activeRow.status,
                scheduledAt: activeRow.scheduledFor,
                durationMs: activeRow.executedAt ? activeRow.elapsedTime : null,
                retries:
                  activeRow.retries > 0
                    ? { current: activeRow.retryCount, max: activeRow.retries }
                    : null,
                exceptionMessage: activeRow.exceptionMessage,
                skippedReason: activeRow.skippedReason,
              }
            : null
        }
        onClose={() => setActiveRow(null)}
      />

      <ConfirmDialog
        open={confirmBulkRetry}
        onOpenChange={(open) => {
          if (!open) setConfirmBulkRetry(false);
        }}
        title="Retry selected executions?"
        description={
          <>
            Queues {selectedRetryable.length} retryable execution(s) to run again
            now. Cron occurrences re-run their parent schedule (deduplicated);
            the original rows stay in history.
          </>
        }
        confirmLabel="Retry all"
        isPending={bulkRetryMutation.isPending}
        onConfirm={handleBulkRetryConfirm}
      />

      <ConfirmDialog
        open={!!retryTarget}
        onOpenChange={(open) => {
          if (!open) setRetryTarget(null);
        }}
        title="Retry execution?"
        description={
          retryTarget &&
          (retryTarget.type === "TimeTicker" ? (
            <>
              Runs{" "}
              <span className="font-mono font-semibold text-foreground">
                {retryTarget.functionName}
              </span>{" "}
              again now.
            </>
          ) : (
            <>
              Runs the parent cron schedule of{" "}
              <span className="font-mono font-semibold text-foreground">
                {retryTarget.functionName}
              </span>{" "}
              now. A fresh occurrence is created — this{" "}
              {retryTarget.status.toLowerCase()} row stays in history.
            </>
          ))
        }
        confirmLabel="Retry"
        isPending={runTimeMutation.isPending || runCronMutation.isPending}
        onConfirm={handleRetryConfirm}
      />
    </div>
  );
}

function StatTile({
  dotClass,
  label,
  icon,
  value,
}: {
  dotClass?: string;
  label?: string;
  icon?: React.ReactNode;
  value: number;
}) {
  return (
    <div className="flex items-center gap-1.5">
      {dotClass ? <span className={`h-1.5 w-1.5 rounded-full ${dotClass}`} /> : null}
      {icon}
      {label && (
        <span className="text-[10px] uppercase tracking-wider text-muted-foreground/60">
          {label}
        </span>
      )}
      <span className="tabular-nums text-[12px] font-medium">{value}</span>
    </div>
  );
}
