import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { GitBranch, Minus, Plus, Trash2, Workflow } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/cron/PageHeader";
import { DataTable } from "@/components/cron/DataTable";
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
import { TimeTickerDetailPanel } from "@/components/cron/TimeTickerDetailPanel";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  useAddTimeTicker,
  useAllFunctions,
  useBulkDeleteTimeTickers,
  useTimeTickers,
  useTimeTickersGraph,
} from "@/services/hooks";
import type {
  TickerStatus,
  TimeTickerFlatDto,
  TimeTickerQueryFilter,
} from "@/services/api-types";
import { formatDuration } from "@/lib/cron/format";
import { encodeRequestPayload } from "@/lib/request-payload";
import { TimeCell } from "@/components/cron/TimeCell";
import { CreateTimeTickerDialog } from "@/components/cron/CreateTimeTickerDialog";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { isReadOnly } from "@/lib/runtime-config";

const PAGE_SIZE = 25;

export default function TimeTickersPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<TickerStatus[]>([]);
  const [functionFilter, setFunctionFilter] = useState("");
  const [createOpen, setCreateOpen] = useState(false);
  const [page, setPage] = useState(1);
  const [deletingIds, setDeletingIds] = useState<Set<string>>(new Set());
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [activeRow, setActiveRow] = useState<TimeTickerFlatDto | null>(null);
  const [recentlyCreatedId, setRecentlyCreatedId] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<"all" | "chains" | "standalone">("all");
  const [scheduledFrom, setScheduledFrom] = useState("");
  const [scheduledTo, setScheduledTo] = useState("");

  useEffect(() => {
    setPage(1);
  }, [search, statusFilter, functionFilter, viewMode, scheduledFrom, scheduledTo]);

  const filter: TimeTickerQueryFilter = useMemo(
    () => ({
      statuses: statusFilter.length > 0 ? statusFilter : null,
      functionName: functionFilter || null,
      search: search || null,
      hasChildren:
        viewMode === "chains" ? true : viewMode === "standalone" ? false : null,
      parentOnly: viewMode === "standalone" ? true : null,
      scheduledFrom: scheduledFrom ? new Date(scheduledFrom).toISOString() : null,
      scheduledTo: scheduledTo ? new Date(scheduledTo).toISOString() : null,
      pageNumber: page,
      pageSize: PAGE_SIZE,
      sortDescending: true,
    }),
    [search, statusFilter, functionFilter, viewMode, scheduledFrom, scheduledTo, page]
  );

  const readOnly = isReadOnly();
  const listQuery = useTimeTickers(filter);
  const data = listQuery.data;
  const { data: graph } = useTimeTickersGraph(7, 0);
  const { data: allFns } = useAllFunctions();
  const addMutation = useAddTimeTicker();
  const bulkDeleteMutation = useBulkDeleteTimeTickers();

  const items = data?.items ?? [];
  const volumeData = useMemo(
    () => (graph ? bucketsToVolume(graph) : emptyVolumeData()),
    [graph]
  );
  const distinctFunctions = useMemo(
    () => (allFns ?? []).map((f) => f.functionName),
    [allFns]
  );

  // Highlight + scroll the just-created row, then clear after the 5s animation.
  useEffect(() => {
    if (!recentlyCreatedId) return;
    const el = document.querySelector(`[data-row-id="${recentlyCreatedId}"]`);
    el?.scrollIntoView({ behavior: "smooth", block: "center" });
    const t = window.setTimeout(() => setRecentlyCreatedId(null), 5000);
    return () => window.clearTimeout(t);
  }, [recentlyCreatedId, data?.items]);

  function toggleOne(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }
  function toggleAll() {
    setSelected((prev) =>
      prev.size === items.length ? new Set() : new Set(items.map((t) => t.id))
    );
  }

  async function handleBulkDelete() {
    if (selected.size === 0) return;
    const ids = Array.from(selected);
    setDeletingIds((prev) => new Set([...prev, ...ids]));
    try {
      // One round-trip regardless of selection size.
      const res = await bulkDeleteMutation.mutateAsync(ids);
      toast.success(
        res.affected === 1 ? "Time ticker deleted" : `${res.affected} time tickers deleted`
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
    }
  }

  const allSelected = items.length > 0 && selected.size === items.length;

  const columns = useMemo<ColumnDef<TimeTickerFlatDto>[]>(
    () => [
      // Selection only exists to feed bulk delete — pointless in read-only mode.
      ...(!readOnly
        ? [
            {
              id: "select",
              header: () => (
                <Checkbox
                  checked={allSelected}
                  onCheckedChange={toggleAll}
                  onClick={(e) => e.stopPropagation()}
                  className="h-3.5 w-3.5"
                />
              ),
              cell: ({ row }: { row: { original: TimeTickerFlatDto } }) => (
                <Checkbox
                  checked={selected.has(row.original.id)}
                  onCheckedChange={() => toggleOne(row.original.id)}
                  onClick={(e) => e.stopPropagation()}
                  className="h-3.5 w-3.5"
                />
              ),
            } satisfies ColumnDef<TimeTickerFlatDto>,
          ]
        : []),
      {
        accessorKey: "functionName",
        header: "Function",
        cell: ({ row }) => {
          const isChain = row.original.childCount > 0;
          const Icon = isChain ? Workflow : Minus;
          return (
            <div className="flex items-start gap-2">
              <Icon
                className={
                  isChain
                    ? "h-3.5 w-3.5 shrink-0 mt-[2px] text-primary/60"
                    : "h-3.5 w-3.5 shrink-0 mt-[2px] text-muted-foreground/40"
                }
              />
              <div className="flex flex-col">
                <span className="font-mono text-[11px] font-medium">
                  {row.original.functionName}
                </span>
                <span className="font-mono text-[10px] tabular-nums text-muted-foreground/70">
                  {row.original.id.slice(0, 12)}
                </span>
              </div>
            </div>
          );
        },
      },
      {
        accessorKey: "type",
        header: "Type",
        cell: ({ row }) =>
          row.original.childCount > 0 ? (
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation();
                navigate(`/time-tickers/${row.original.id}/flowchart`);
              }}
              title="View chain flowchart"
              className="inline-flex items-center gap-1 rounded-md bg-primary/10 px-2 py-0.5 text-[11px] font-medium text-primary uppercase tracking-wide transition-colors hover:bg-primary/20"
            >
              <Workflow className="h-2.5 w-2.5" />
              Chain
              <span className="text-[9px] tabular-nums">{row.original.childCount}</span>
            </button>
          ) : (
            <span className="text-[11px] text-muted-foreground">Standalone</span>
          ),
      },
      {
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) => <StatusBadge status={row.original.status} />,
      },
      {
        accessorKey: "scheduledFor",
        header: "Scheduled For",
        cell: ({ row }) => <TimeCell value={row.original.scheduledFor} />,
      },
      {
        accessorKey: "elapsedTime",
        header: "Duration",
        cell: ({ row }) => (
          <span className="tabular-nums text-[11px]">
            {row.original.executedAt ? formatDuration(row.original.elapsedTime) : "—"}
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
        accessorKey: "priority",
        header: "Priority",
        cell: ({ row }) => <PriorityBadge priority={row.original.priority} />,
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
      {
        accessorKey: "createdAt",
        header: "Created",
        cell: ({ row }) => <TimeCell value={row.original.createdAt} muted />,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [selected, items, allSelected, readOnly]
  );

  const toolbar = (
    <div className="space-y-2">
      <div className="flex items-center gap-2 flex-wrap">
        {/* View segment (All / Chains / Standalone) */}
        <div className="inline-flex rounded-lg bg-surface-1 p-0.5">
          {(["all", "chains", "standalone"] as const).map((v) => {
            const isActive = viewMode === v;
            return (
              <button
                key={v}
                type="button"
                onClick={() => setViewMode(v)}
                className={
                  "px-2.5 py-1 rounded-md text-xs font-medium transition-all " +
                  (isActive
                    ? "bg-surface-2 text-foreground shadow-sm"
                    : "text-muted-foreground hover:text-foreground")
                }
              >
                {v === "all" ? "All" : v === "chains" ? "Chains" : "Standalone"}
              </button>
            );
          })}
        </div>
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
        {!readOnly && (
          <div className="ml-auto flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              className="h-8 text-xs rounded-lg gap-1.5 border-status-error/40 text-status-error hover:bg-status-error/10"
              disabled={selected.size === 0 || bulkDeleteMutation.isPending}
              onClick={handleBulkDelete}
            >
              <Trash2 className="h-3 w-3" />
              {selected.size <= 1 ? "Delete" : `Delete (${selected.size})`}
            </Button>
          </div>
        )}
      </div>

      {/* Scheduled date range */}
      <div className="flex items-center gap-2 text-[11px]">
        <span className="text-muted-foreground/60 uppercase tracking-wider">Scheduled</span>
        <input
          type="datetime-local"
          value={scheduledFrom}
          onChange={(e) => setScheduledFrom(e.target.value)}
          className="h-7 rounded-md border border-border bg-surface-1 px-2 text-[11px]"
        />
        <span className="text-muted-foreground/40">→</span>
        <input
          type="datetime-local"
          value={scheduledTo}
          onChange={(e) => setScheduledTo(e.target.value)}
          className="h-7 rounded-md border border-border bg-surface-1 px-2 text-[11px]"
        />
        {(scheduledFrom || scheduledTo) && (
          <button
            type="button"
            onClick={() => {
              setScheduledFrom("");
              setScheduledTo("");
            }}
            className="text-[10px] text-muted-foreground/60 hover:text-foreground"
          >
            Clear
          </button>
        )}
      </div>

      {(statusFilter.length > 0 || functionFilter) && (
        <div className="flex items-center gap-2 flex-wrap">
          {statusFilter.length > 0 && (
            <FilterChip label="Status" value={statusFilter.join(", ")} onRemove={() => setStatusFilter([])} />
          )}
          {functionFilter && (
            <FilterChip label="Function" value={functionFilter} onRemove={() => setFunctionFilter("")} />
          )}
        </div>
      )}
    </div>
  );

  return (
    <div className="space-y-5">
      <ExecutionVolumeChart data={volumeData} title="Time Ticker Executions (7d)" gradientPrefix="tt" />

      <PageHeader
        title="Time Tickers"
        count={data?.totalCount ?? items.length}
        description="One-time scheduled job executions with chain workflow support."
      >
        {!readOnly && (
          <>
            <Button
              variant="outline"
              size="sm"
              className="h-8 text-xs rounded-lg border-border text-muted-foreground"
              onClick={() => navigate("/time-tickers/new-chain")}
            >
              <GitBranch className="h-3 w-3 mr-1.5" />
              Chain Job
            </Button>
            <Button
              variant="gradient"
              size="sm"
              className="h-8 text-xs rounded-lg"
              onClick={() => setCreateOpen(true)}
            >
              <Plus className="h-3 w-3 mr-1.5" />
              Create Ticker
            </Button>
          </>
        )}
      </PageHeader>

      {listQuery.isError && (
        <QueryErrorNotice
          message="Failed to load time tickers."
          onRetry={() => void listQuery.refetch()}
        />
      )}

      <DataTable
        columns={columns}
        data={items}
        toolbar={toolbar}
        emptyMessage="No time tickers match the current filters."
        onRowClick={(row) => setActiveRow(row)}
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

      <CreateTimeTickerDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        functionOptions={distinctFunctions.map((fn) => ({ value: fn, label: fn }))}
        functions={allFns ?? []}
        onSubmit={async (values) => {
          const res = await addMutation.mutateAsync({
            function: values.function,
            executionTime: values.executionTime || null,
            description: values.description || null,
            retries: values.retries ?? null,
            retryIntervalsSeconds: parseIntervals(values.retryIntervalsSeconds),
            request: encodeRequestPayload(values.requestJson),
            onStale: values.onStale,
            timeoutSeconds:
              values.timeoutSeconds && values.timeoutSeconds > 0 ? values.timeoutSeconds : null,
          });
          setPage(1);
          setRecentlyCreatedId(res.id);
        }}
      />

      <TimeTickerDetailPanel
        ticker={activeRow}
        onClose={() => setActiveRow(null)}
        onChanged={() => listQuery.refetch()}
        onDuplicated={(newId) => {
          setPage(1);
          setRecentlyCreatedId(newId);
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
