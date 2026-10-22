import { ExecutionStatus } from "@/components/cron/ExecutionStatus";
import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Copy, RotateCw, Timer } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/cron/PageHeader";
import { DataTable } from "@/components/cron/DataTable";
import { LockHolder } from "@/components/cron/LockHolder";
import { TypeBadge } from "@/components/cron/StatusBadges";
import {
  FilterChip,
  FilterPopover,
  SearchInput,
} from "@/components/cron/TableToolbar";
import { ExecutionVolumeChart } from "@/components/cron/ExecutionVolumeChart";
import {
  bucketsToVolume,
  emptyVolumeData,
  mergeVolumeData,
} from "@/lib/cron/execution-volume";
import { LogsSidePanel } from "@/components/cron/LogsSidePanel";
import { BulkActionBar } from "@/components/cron/BulkActionBar";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import {
  useAllFunctions,
  useBulkRetryExecutions,
  useCronTickersGraph,
  useExecutions,
  useOverallStatuses,
  useRerunCronOccurrence,
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
import { formatStatusLabels } from "@/lib/cron/status-config";

const PAGE_SIZE = 25;

/** Terminal-failure statuses that offer the Retry affordance. */
const RETRYABLE_STATUSES: TickerStatus[] = ["Failed", "Cancelled", "Skipped"];

// A recurring-chain trigger and its time-ticker root intentionally share an ID.
const executionKey = (row: ExecutionFlatDto) => `${row.type}:${row.id}`;

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

  const listQuery = useExecutions(filter, activeRow != null);
  const data = listQuery.data;
  const readOnly = isReadOnly();
  const runTimeMutation = useRunTimeTicker();
  const rerunCronOccurrenceMutation = useRerunCronOccurrence();
  const { data: timeGraph } = useTimeTickersGraph(7, 0);
  const { data: cronGraph } = useCronTickersGraph(7, 0);
  const { data: allFns } = useAllFunctions();
  const { data: overall } = useOverallStatuses();
  const volumeData = useMemo(
    () => mergeVolumeData(
      timeGraph ? bucketsToVolume(timeGraph) : emptyVolumeData(),
      cronGraph ? bucketsToVolume(cronGraph) : emptyVolumeData()
    ),
    [timeGraph, cronGraph]
  );

  const items = data?.items ?? [];
  const displayedRow = activeRow
    ? items.find(item => executionKey(item) === executionKey(activeRow)) ?? activeRow
    : null;

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

  // Retry revives the selected persisted execution. Both time tickers and cron
  // occurrences keep their original IDs instead of adding duplicate records.
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
    else if (r.cronTickerId) rerunCronOccurrenceMutation.mutate(r.id, opts);
  }

  const bulkRetryMutation = useBulkRetryExecutions();

  // Of the selection, only terminal-failure rows can actually be retried.
  const selectedRetryable = useMemo(
    () =>
      items.filter(
        (r) =>
          selected.has(executionKey(r)) &&
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
            chainChildCount={row.original.type === "TimeTicker" ? row.original.childCount : undefined}
            isChain={row.original.type === "CronOccurrence" ? !!row.original.chainRootId : row.original.childCount > 0}
            onChainClick={
              row.original.type === "CronOccurrence" && row.original.chainRootId
                ? () => navigate(`/time-tickers/${row.original.chainRootId}/flowchart?from=executions`)
                : row.original.type === "TimeTicker" && row.original.childCount > 0
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
        cell: ({ row }) => (
          <ExecutionStatus status={row.original.status} summary={row.original.chainSummary}
            isChain={!!row.original.chainRootId || row.original.childCount > 0}
            progressPercent={row.original.progressPercent}
            progressMessage={row.original.progressMessage}
            displayIdleAsWaiting />
        ),
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
        header: "Node",
        cell: ({ row }) => <LockHolder value={row.original.lockHolder} />,
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
                        : "Retry this occurrence using the same record"
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
          statusLabel="Root / trigger status"
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
              label="Root / trigger status"
              value={formatStatusLabels(statusFilter, true)}
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
        getRowId={executionKey}
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
          displayedRow
            ? {
                id: displayedRow.id,
                function: displayedRow.functionName,
                status: displayedRow.status,
                scheduledAt: displayedRow.scheduledFor,
                durationMs: displayedRow.executedAt ? displayedRow.elapsedTime : null,
                retries:
                  displayedRow.retries > 0
                    ? { current: displayedRow.retryCount, max: displayedRow.retries }
                    : null,
                exceptionMessage: displayedRow.exceptionMessage,
                skippedReason: displayedRow.skippedReason,
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
            now. Each existing ticker or occurrence record is reused, so this does
            not create duplicate execution rows.
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
              Runs cron occurrence{" "}
              <span className="font-mono font-semibold text-foreground">
                {retryTarget.functionName}
              </span>{" "}
              again using its existing ID. Its previous status, result and progress
              are replaced; no new occurrence is created.
            </>
          ))
        }
        confirmLabel="Retry"
        isPending={runTimeMutation.isPending || rerunCronOccurrenceMutation.isPending}
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
