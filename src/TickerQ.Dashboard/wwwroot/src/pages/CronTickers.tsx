import { ExecutionStatus } from "@/components/cron/ExecutionStatus";
import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { FolderOpen, GitBranch, Minus, Pencil, Plus, Power, PowerOff, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/cron/PageHeader";
import { DataTable } from "@/components/cron/DataTable";
import { BulkActionBar } from "@/components/cron/BulkActionBar";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { ChainTypeBadge, PriorityBadge } from "@/components/cron/StatusBadges";
import {
  FilterChip,
  FilterPopover,
  SearchInput,
} from "@/components/cron/TableToolbar";
import { ExecutionVolumeChart } from "@/components/cron/ExecutionVolumeChart";
import {
  bucketsToVolume,
  emptyVolumeData,
} from "@/lib/cron/execution-volume";
import { CreateCronTickerDialog } from "@/components/cron/CreateCronTickerDialog";
import { SavedDefinitionsPanel } from "@/components/cron/SavedDefinitionsPanel";
import { EditCronTickerDialog } from "@/components/cron/EditCronTickerDialog";
import { nextRunFromExpression } from "@/components/cron/CronExpressionPreview";
import { describeCronExpression } from "@/lib/cron/description";
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
import { formatStatusLabels } from "@/lib/cron/status-config";

const PAGE_SIZE = 25;

export default function CronTickersPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<TickerStatus[]>([]);
  const [functionFilter, setFunctionFilter] = useState("");
  const [page, setPage] = useState(1);
  const [deletingIds, setDeletingIds] = useState<Set<string>>(new Set());
  const [createOpen, setCreateOpen] = useState(false);
  const [savedOpen, setSavedOpen] = useState(false);
  const [editingCron, setEditingCron] = useState<CronTickerFlatDto | null>(null);
  const [recentlyCreatedId, setRecentlyCreatedId] = useState<string | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [confirmBulkDelete, setConfirmBulkDelete] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<CronTickerFlatDto | null>(null);
  const [pendingToggle, setPendingToggle] = useState<{
    cron: CronTickerFlatDto;
    isEnabled: boolean;
  } | null>(null);

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

  async function handleToggle() {
    if (!pendingToggle || readOnly || toggleMutation.isPending) return;
    const { cron, isEnabled: next } = pendingToggle;
    try {
      await toggleMutation.mutateAsync({ id: cron.id, body: { isEnabled: next } });
      toast.success(next ? "Cron enabled" : "Cron disabled");
      setPendingToggle(null);
    } catch (err) {
      toast.error(next ? "Failed to enable" : "Failed to disable", {
        description: err instanceof Error ? err.message : "An error occurred",
      });
    }
  }

  async function handleDelete() {
    if (!pendingDelete || readOnly || deleteMutation.isPending) return;
    const row = pendingDelete;
    setDeletingIds((prev) => new Set(prev).add(row.id));
    try {
      await deleteMutation.mutateAsync(row.id);
      toast.success("Cron ticker deleted");
      setPendingDelete(null);
      setSelected((prev) => {
        const next = new Set(prev);
        next.delete(row.id);
        return next;
      });
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
    if (selected.size === 0 || readOnly || bulkDeleteMutation.isPending) return;
    const ids = Array.from(selected);
    setDeletingIds((prev) => new Set([...prev, ...ids]));
    try {
      const res = await bulkDeleteMutation.mutateAsync(ids);
      toast.success(
        res.affected === 1 ? "Cron ticker deleted" : `${res.affected} cron tickers deleted`
      );
      setSelected(new Set());
      setConfirmBulkDelete(false);
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
    }
  }

  const columns = useMemo<ColumnDef<CronTickerFlatDto>[]>(
    () => [
      {
        accessorKey: "functionName",
        header: "Function",
        cell: ({ row }) => (
          <div className="flex items-center gap-2">
            {row.original.chain
              ? <GitBranch aria-hidden="true" className="h-3.5 w-3.5 shrink-0 text-primary/60" />
              : <Minus aria-hidden="true" className="h-3.5 w-3.5 shrink-0 text-muted-foreground/40" />}
            <span className="font-mono text-[11px] font-medium text-foreground">
              {row.original.functionName}
            </span>
          </div>
        ),
      },
      {
        id: "type",
        header: "Type",
        cell: ({ row }) => (
          <ChainTypeBadge isChain={!!row.original.chain}
            childCount={row.original.chain?.children.length}
            onClick={() => navigate(`/cron-tickers/${row.original.id}`)} />
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
            <p className="mt-0.5 max-w-xs whitespace-normal text-[10px] leading-4 text-muted-foreground">
              {describeCronExpression(row.original.expression) ?? "Invalid cron expression"}
            </p>
          </div>
        ),
      },
      {
        accessorKey: "timeZoneId",
        header: "Time zone",
        cell: ({ row }) => (
          <span
            className="block max-w-40 truncate font-mono text-[10px] text-muted-foreground"
            title={row.original.timeZoneId || "Server local time zone"}
          >
            {row.original.timeZoneId || "Server local"}
          </span>
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
              disabled={readOnly || toggleMutation.isPending}
              aria-label={`${r.isEnabled ? "Disable" : "Enable"} ${r.functionName}`}
              onClick={(e) => {
                e.stopPropagation();
                if (!readOnly && !toggleMutation.isPending)
                  setPendingToggle({ cron: r, isEnabled: !r.isEnabled });
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
            <ExecutionStatus status={row.original.lastRunStatus} summary={row.original.lastRunChainSummary} isChain={!!row.original.lastRunChainRootId} displayIdleAsWaiting />
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
                    disabled={toggleMutation.isPending}
                    aria-label={`${r.isEnabled ? "Disable" : "Enable"} ${r.functionName}`}
                    onClick={() => {
                      if (!readOnly && !toggleMutation.isPending)
                        setPendingToggle({ cron: r, isEnabled: !r.isEnabled });
                    }}
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
                    aria-label={`Edit ${r.functionName}`}
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
                    aria-label={`Delete ${r.functionName}`}
                    disabled={deleteMutation.isPending || bulkDeleteMutation.isPending}
                    onClick={() => setPendingDelete(r)}
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
    [readOnly, toggleMutation.isPending, deleteMutation.isPending, bulkDeleteMutation.isPending]
  );

  const toolbar = (
    <div className="space-y-2">
      <div className="flex items-center gap-2 flex-wrap">
        <SearchInput value={search} onChange={setSearch} placeholder="Search by function…" />
        <FilterPopover
          statusLabel="Last trigger status"
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
        title="Cron Ticker Executions (7d)"
        gradientPrefix="ct"
      />

      <PageHeader
        title="Cron Tickers"
        count={data?.totalCount ?? items.length}
        description="Recurring scheduled job definitions."
      >
        {!readOnly && <Button variant="outline" size="sm" className="h-8 text-xs rounded-lg" onClick={() => setSavedOpen(true)}><FolderOpen className="mr-1.5 h-3 w-3" />Templates</Button>}
        {!readOnly && <Button variant="outline" size="sm" className="h-8 text-xs rounded-lg" onClick={() => navigate("/cron-tickers/new-chain?cron=new")}>Create recurring chain</Button>}
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
            skipIfAlreadyRunning: values.skipIfAlreadyRunning,
            onStale: values.onStale,
            timeoutSeconds:
              values.timeoutSeconds && values.timeoutSeconds > 0 ? values.timeoutSeconds : null,
            timeZoneId: values.timeZoneId || null,
            schedulePolicyName: values.schedulePolicyName || null,
            missedExecutionAction: values.missedExecutionAction,
            maximumLatenessSeconds: values.missedExecutionAction === "Skip"
              ? values.maximumLatenessSeconds ?? 0
              : null,
            catchUpMode: values.catchUpMode,
          });
          setPage(1);
          setRecentlyCreatedId(res.id);
        }}
      />

      <SavedDefinitionsPanel open={savedOpen} target="CronTicker" onOpenChange={setSavedOpen} />

      <ConfirmDialog
        open={!!pendingToggle}
        onOpenChange={(open) => {
          if (!open && !toggleMutation.isPending) setPendingToggle(null);
        }}
        title={pendingToggle?.isEnabled ? "Enable cron ticker?" : "Disable cron ticker?"}
        description={pendingToggle?.isEnabled
          ? "Enable scheduled runs for this cron ticker using its current cron expression."
          : "Disable scheduled runs for this cron ticker. You can enable it again later."}
        confirmLabel={pendingToggle?.isEnabled ? "Enable" : "Disable"}
        confirmVariant={pendingToggle?.isEnabled ? "gradient" : "default"}
        isPending={toggleMutation.isPending}
        onConfirm={handleToggle}
      >
        {pendingToggle && (
          <div className="space-y-3">
            <dl className="space-y-2 rounded-lg border border-border bg-card p-3 text-xs">
              <div className="space-y-1">
                <dt className="text-muted-foreground">Function</dt>
                <dd className="break-all font-mono font-medium">{pendingToggle.cron.functionName}</dd>
              </div>
              <div className="space-y-1">
                <dt className="text-muted-foreground">Cron expression</dt>
                <dd className="break-all font-mono">{pendingToggle.cron.expression}</dd>
              </div>
              <div className="space-y-1">
                <dt className="text-muted-foreground">Ticker ID</dt>
                <dd className="break-all font-mono text-muted-foreground">{pendingToggle.cron.id}</dd>
              </div>
            </dl>
            {pendingToggle.isEnabled && pendingToggle.cron.isSystemPaused && (
              <p className="text-xs leading-relaxed text-status-warning">The SDK node is offline. This ticker will remain system-paused until the node reconnects.</p>
            )}
          </div>
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={!!pendingDelete}
        onOpenChange={(open) => { if (!open) setPendingDelete(null); }}
        title="Delete cron ticker?"
        description="Permanently deletes this cron ticker and its occurrence history. This cannot be undone."
        confirmLabel="Delete ticker"
        confirmVariant="destructive"
        isPending={deleteMutation.isPending}
        onConfirm={handleDelete}
      >
        {pendingDelete && (
          <dl className="space-y-2 rounded-lg border border-border bg-card p-3 text-xs">
            <div className="space-y-1">
              <dt className="text-muted-foreground">Function</dt>
              <dd className="break-all font-mono font-medium">{pendingDelete.functionName}</dd>
            </div>
            <div className="space-y-1">
              <dt className="text-muted-foreground">Cron expression</dt>
              <dd className="break-all font-mono">{pendingDelete.expression}</dd>
            </div>
            <div className="space-y-1">
              <dt className="text-muted-foreground">Ticker ID</dt>
              <dd className="break-all font-mono text-muted-foreground">{pendingDelete.id}</dd>
            </div>
          </dl>
        )}
      </ConfirmDialog>

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
              function: values.function,
              expression: values.expression,
              description: values.description || null,
              retries: values.retries ?? null,
              retryIntervalsSeconds: parseIntervals(values.retryIntervalsSeconds),
              request: values.requestJson?.trim()
                ? encodeRequestPayload(values.requestJson)
                : "",
              isEnabled: values.isEnabled,
              skipIfAlreadyRunning: values.skipIfAlreadyRunning,
              onStale: values.onStale,
              timeoutSeconds: values.timeoutSeconds ?? 0,
              timeZoneId: values.timeZoneId,
              schedulePolicyName: values.schedulePolicyName || null,
              replaceSchedulePolicy: true,
              missedExecutionAction: values.missedExecutionAction,
              maximumLatenessSeconds: values.missedExecutionAction === "Skip"
                ? values.maximumLatenessSeconds ?? 0
                : null,
              catchUpMode: values.catchUpMode,
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
