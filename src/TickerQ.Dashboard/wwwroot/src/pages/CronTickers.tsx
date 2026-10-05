import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Pencil, Plus, Power, PowerOff, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/cron/PageHeader";
import { DataTable } from "@/components/cron/DataTable";
import { BulkActionBar } from "@/components/cron/BulkActionBar";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { PriorityBadge, StatusBadge } from "@/components/cron/StatusBadges";
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
import { CreateCronTickerDialog } from "@/components/cron/CreateCronTickerDialog";
import { EditCronTickerDialog } from "@/components/cron/EditCronTickerDialog";
import { nextRunFromExpression } from "@/components/cron/CronExpressionPreview";
import { Button } from "@/components/ui/button";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";
import {
  useAddCronTicker,
  useAllFunctions,
  useCronTickers,
  useCronTickersGraph,
  useBulkDeleteCronTickers,
  useDeleteCronTicker,
  useToggleCronTicker,
  useUpdateCronTicker,
} from "@/services/hooks";
import type {
  CronTickerFlatDto,
  CronTickerQueryFilter,
  TickerStatus,
} from "@/services/api-types";
import { TimeCell } from "@/components/cron/TimeCell";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { isReadOnly } from "@/lib/runtime-config";
import { encodeRequestPayload } from "@/lib/request-payload";

const PAGE_SIZE = 25;

export default function CronTickersPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<TickerStatus[]>([]);
  const [functionFilter, setFunctionFilter] = useState("");
  const [page, setPage] = useState(1);
  const [deletingIds, setDeletingIds] = useState<Set<string>>(new Set());
  const [createOpen, setCreateOpen] = useState(false);
  const [editingCron, setEditingCron] = useState<CronTickerFlatDto | null>(null);
  const [recentlyCreatedId, setRecentlyCreatedId] = useState<string | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [confirmBulkDelete, setConfirmBulkDelete] = useState(false);

  useEffect(() => {
    setPage(1);
  }, [search, statusFilter, functionFilter]);

  // Selection is page-scoped — see Executions page.
  useEffect(() => {
    setSelected(new Set());
  }, [page, search, statusFilter, functionFilter]);

  const filter: CronTickerQueryFilter = useMemo(
    () => ({
      lastRunStatuses: statusFilter.length > 0 ? statusFilter : null,
      functionName: functionFilter || null,
      search: search || null,
      pageNumber: page,
      pageSize: PAGE_SIZE,
      sortDescending: true,
    }),
    [search, statusFilter, functionFilter, page]
  );

  const readOnly = isReadOnly();
  const listQuery = useCronTickers(filter);
  const data = listQuery.data;
  const { data: graph } = useCronTickersGraph(7, 0);
  const { data: allFns } = useAllFunctions();
  const addMutation = useAddCronTicker();
  const toggleMutation = useToggleCronTicker();
  const deleteMutation = useDeleteCronTicker();
  const bulkDeleteMutation = useBulkDeleteCronTickers();
  const updateMutation = useUpdateCronTicker();

  const items = data?.items ?? [];
  const volumeData = useMemo(
    () => (graph ? bucketsToVolume(graph) : emptyVolumeData()),
    [graph]
  );
  const distinctFunctions = useMemo(
    () => (allFns ?? []).map((f) => f.functionName),
    [allFns]
  );

  useEffect(() => {
    if (!recentlyCreatedId) return;
    const el = document.querySelector(`[data-row-id="${recentlyCreatedId}"]`);
    el?.scrollIntoView({ behavior: "smooth", block: "center" });
    const t = window.setTimeout(() => setRecentlyCreatedId(null), 5000);
    return () => window.clearTimeout(t);
  }, [recentlyCreatedId, data?.items]);

  async function handleToggle(row: CronTickerFlatDto) {
    const next = !row.isEnabled;
    try {
      await toggleMutation.mutateAsync({ id: row.id, body: { isEnabled: next } });
      toast.success(next ? "Cron enabled" : "Cron disabled");
    } catch (err) {
      toast.error(next ? "Failed to enable" : "Failed to disable", {
        description: err instanceof Error ? err.message : "An error occurred",
      });
    }
  }

  async function handleDelete(row: CronTickerFlatDto) {
    setDeletingIds((prev) => new Set(prev).add(row.id));
    try {
      await deleteMutation.mutateAsync(row.id);
      toast.success("Cron ticker deleted");
    } catch (err) {
      toast.error("Failed to delete", {
        description: err instanceof Error ? err.message : "An error occurred",
      });
    } finally {
      setDeletingIds((prev) => {
        const nextSet = new Set(prev);
        nextSet.delete(row.id);
        return nextSet;
      });
    }
  }

  async function handleBulkDelete() {
    if (selected.size === 0) return;
    const ids = Array.from(selected);
    setDeletingIds((prev) => new Set([...prev, ...ids]));
    try {
      const res = await bulkDeleteMutation.mutateAsync(ids);
      toast.success(
        res.affected === 1 ? "Cron ticker deleted" : `${res.affected} cron tickers deleted`
      );
      setSelected(new Set());
    } catch (err) {
      toast.error("Failed to delete", {
        description: err instanceof Error ? err.message : "An error occurred",
      });
    } finally {
      setDeletingIds((prev) => {
        const next = new Set(prev);
        for (const id of ids) next.delete(id);
        return next;
      });
      setConfirmBulkDelete(false);
    }
  }

  const columns = useMemo<ColumnDef<CronTickerFlatDto>[]>(
    () => [
      {
        accessorKey: "functionName",
        header: "Function",
        cell: ({ row }) => (
          <span className="font-mono text-[11px] font-medium text-foreground">
            {row.original.functionName}
          </span>
        ),
      },
      {
        accessorKey: "expression",
        header: "Expression",
        cell: ({ row }) => (
          <div>
            <span className="font-mono text-[11px] text-foreground">
              {row.original.expression}
            </span>
            {row.original.description && (
              <p className="text-[10px] text-muted-foreground/50 mt-0.5">
                {row.original.description}
              </p>
            )}
          </div>
        ),
      },
      {
        accessorKey: "isEnabled",
        header: "State",
        cell: ({ row }) => {
          const r = row.original;
          const Icon = !r.isEnabled || r.isSystemPaused ? PowerOff : Power;
          return (
            <button
              type="button"
              disabled={readOnly}
              onClick={(e) => {
                e.stopPropagation();
                if (!readOnly) handleToggle(r);
              }}
              title={
                readOnly
                  ? "Read-only mode — state cannot be changed"
                  : !r.isEnabled
                    ? "User-disabled — toggle to re-enable"
                    : r.isSystemPaused
                      ? "Paused: SDK node is offline. Auto-resumes on reconnect."
                      : "Enabled — toggle to disable"
              }
              className={cn(
                "inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] font-medium transition-colors",
                readOnly && "cursor-default",
                !r.isEnabled
                  ? "bg-status-cancelled/10 text-status-cancelled hover:bg-status-cancelled/20"
                  : r.isSystemPaused
                    ? "bg-status-warning/10 text-status-warning hover:bg-status-warning/20"
                    : "bg-status-healthy/10 text-status-healthy hover:bg-status-healthy/20"
              )}
            >
              <Icon className="h-2.5 w-2.5" />
              {!r.isEnabled ? "Disabled" : r.isSystemPaused ? "Paused" : "Enabled"}
            </button>
          );
        },
      },
      {
        accessorKey: "lastRunStatus",
        header: "Last Status",
        cell: ({ row }) =>
          row.original.lastRunStatus ? (
            <StatusBadge status={row.original.lastRunStatus} />
          ) : (
            <span className="text-[11px] text-muted-foreground/40">—</span>
          ),
      },
      {
        accessorKey: "lastRunAt",
        header: "Last Run",
        cell: ({ row }) => <TimeCell value={row.original.lastRunAt} />,
      },
      {
        id: "nextRun",
        header: "Next Run",
        cell: ({ row }) => {
          const nextRun = row.original.isEnabled
            ? nextRunFromExpression(row.original.expression)
            : null;
          return <TimeCell value={nextRun} />;
        },
      },
      {
        accessorKey: "occurrenceCount",
        header: "Occurrences",
        cell: ({ row }) => (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              navigate(`/cron-tickers/${row.original.id}`);
            }}
            className="text-[11px] text-primary tabular-nums hover:underline"
            title="View occurrences"
          >
            {row.original.occurrenceCount}
          </button>
        ),
      },
      {
        accessorKey: "priority",
        header: "Priority",
        cell: ({ row }) => <PriorityBadge priority={row.original.priority} />,
      },
      ...(readOnly
        ? []
        : [
      {
        id: "actions",
        header: () => <span className="block text-right">Actions</span>,
        cell: ({ row }: { row: { original: CronTickerFlatDto } }) => {
          const r = row.original;
          return (
            <div
              className="flex items-center justify-end gap-0.5"
              onClick={(e) => e.stopPropagation()}
            >
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button
                    variant="ghost"
                    size="sm"
                    className={cn(
                      "h-7 w-7 p-0 rounded-lg",
                      r.isEnabled
                        ? "text-muted-foreground/50 hover:text-status-cancelled"
                        : "text-muted-foreground/50 hover:text-status-healthy"
                    )}
                    onClick={() => handleToggle(r)}
                  >
                    {r.isEnabled ? (
                      <PowerOff className="h-3 w-3" />
                    ) : (
                      <Power className="h-3 w-3" />
                    )}
                  </Button>
                </TooltipTrigger>
                <TooltipContent>{r.isEnabled ? "Disable" : "Enable"}</TooltipContent>
              </Tooltip>

              <Tooltip>
                <TooltipTrigger asChild>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="h-7 w-7 p-0 text-muted-foreground/50 hover:text-primary rounded-lg"
                    onClick={() => setEditingCron(r)}
                  >
                    <Pencil className="h-3 w-3" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>Edit</TooltipContent>
              </Tooltip>

              <Tooltip>
                <TooltipTrigger asChild>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="h-7 w-7 p-0 text-muted-foreground/50 hover:text-status-error rounded-lg"
                    onClick={() => handleDelete(r)}
                  >
                    <Trash2 className="h-3 w-3" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>Delete</TooltipContent>
              </Tooltip>
            </div>
          );
        },
      } satisfies ColumnDef<CronTickerFlatDto>,
          ]),
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [readOnly]
  );

  const toolbar = (
    <div className="space-y-2">
      <div className="flex items-center gap-2 flex-wrap">
        <SearchInput value={search} onChange={setSearch} placeholder="Search by function…" />
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
              label="Last Status"
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
        title="Cron Ticker Executions (7d)"
        gradientPrefix="ct"
      />

      <PageHeader
        title="Cron Tickers"
        count={data?.totalCount ?? items.length}
        description="Recurring scheduled job definitions."
      >
        {!readOnly && (
          <Button
            variant="gradient"
            size="sm"
            className="h-8 text-xs rounded-lg"
            onClick={() => setCreateOpen(true)}
          >
            <Plus className="h-3 w-3 mr-1.5" />
            Create Cron Ticker
          </Button>
        )}
      </PageHeader>

      {listQuery.isError && (
        <QueryErrorNotice
          message="Failed to load cron tickers."
          onRetry={() => void listQuery.refetch()}
        />
      )}

      {!readOnly && selected.size > 0 && (
        <BulkActionBar count={selected.size} onClear={() => setSelected(new Set())}>
          <Button
            type="button"
            variant="destructive"
            size="sm"
            className="h-7 gap-1 text-xs"
            disabled={bulkDeleteMutation.isPending}
            onClick={() => setConfirmBulkDelete(true)}
          >
            <Trash2 className="h-3 w-3" />
            Delete ({selected.size})
          </Button>
        </BulkActionBar>
      )}

      <DataTable
        columns={columns}
        data={items}
        toolbar={toolbar}
        emptyMessage="No cron tickers match the current filters."
        onRowClick={(row) => navigate(`/cron-tickers/${row.id}`)}
        selectedIds={!readOnly ? selected : undefined}
        onSelectionChange={!readOnly ? setSelected : undefined}
        page={page}
        totalPages={data?.totalPages ?? 1}
        totalCount={data?.totalCount ?? items.length}
        onPageChange={setPage}
        isLoading={listQuery.isLoading}
        isFetching={listQuery.isFetching}
        skeletonRows={6}
        highlightedIds={recentlyCreatedId ? new Set([recentlyCreatedId]) : undefined}
        deletingIds={deletingIds}
      />

      <CreateCronTickerDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        functionOptions={distinctFunctions.map((fn) => ({ value: fn, label: fn }))}
        functions={allFns ?? []}
        onSubmit={async (values) => {
          const res = await addMutation.mutateAsync({
            function: values.function,
            expression: values.expression,
            description: values.description || null,
            retries: values.retries ?? null,
            retryIntervalsSeconds: parseIntervals(values.retryIntervalsSeconds),
            request: encodeRequestPayload(values.requestJson),
            isEnabled: values.isEnabled,
            onStale: values.onStale,
            timeoutSeconds:
              values.timeoutSeconds && values.timeoutSeconds > 0 ? values.timeoutSeconds : null,
          });
          setPage(1);
          setRecentlyCreatedId(res.id);
        }}
      />

      <ConfirmDialog
        open={confirmBulkDelete}
        onOpenChange={(open) => {
          if (!open) setConfirmBulkDelete(false);
        }}
        title="Delete selected cron tickers?"
        description={
          <>
            Permanently deletes {selected.size} cron ticker(s) and their
            occurrence history. This cannot be undone.
          </>
        }
        confirmLabel="Delete all"
        confirmVariant="destructive"
        isPending={bulkDeleteMutation.isPending}
        onConfirm={handleBulkDelete}
      />

      <EditCronTickerDialog
        cron={editingCron}
        open={!!editingCron}
        onOpenChange={(o) => {
          if (!o) setEditingCron(null);
        }}
        onSubmit={async (values) => {
          if (!editingCron) return;
          await updateMutation.mutateAsync({
            id: editingCron.id,
            body: {
              expression: values.expression,
              description: values.description || null,
              retries: values.retries ?? null,
              retryIntervalsSeconds: parseIntervals(values.retryIntervalsSeconds),
              isEnabled: values.isEnabled,
              onStale: values.onStale,
              timeoutSeconds: values.timeoutSeconds ?? 0,
            },
          });
        }}
      />
    </div>
  );
}

function parseIntervals(raw: string | undefined): number[] | null {
  if (!raw || !raw.trim()) return null;
  const parts = raw
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean)
    .map((s) => Number(s))
    .filter((n) => Number.isFinite(n));
  return parts.length > 0 ? parts : null;
}
