import { useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { ArrowLeft, Ban, Loader2, Play, Trash2, X } from "lucide-react";
import { toast } from "sonner";

import { DataTable } from "@/components/cron/DataTable";
import { StatusBadge, PriorityBadge } from "@/components/cron/StatusBadges";
import {
  ExecutionVolumeChart,
  bucketsToVolume,
  emptyVolumeData,
} from "@/components/cron/ExecutionVolumeChart";
import { CronExpressionPreview } from "@/components/cron/CronExpressionPreview";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { ResizableSidePanel } from "@/components/cron/ResizableSidePanel";
import { PayloadPreview } from "@/components/cron/PayloadPreview";
import { LogsPanel } from "@/components/cron/LogsPanel";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  useCancelTicker,
  useCronOccurrences,
  useCronTicker,
  useCronTickerGraphById,
  useDeleteCronOccurrence,
  useRunCronTicker,
} from "@/services/hooks";
import { useRowHighlight } from "@/lib/cron/useRowHighlight";
import { joinCronGroup, leaveCronGroup } from "@/services/ticker-hub";
import { formatDuration, relativeTime } from "@/lib/cron/format";
import { TimeCell } from "@/components/cron/TimeCell";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { isReadOnly } from "@/lib/runtime-config";
import type { CronOccurrenceFlatDto } from "@/services/api-types";

const PAGE_SIZE = 25;

export default function CronTickerDetailPage() {
  const { id = "" } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [occPage, setOccPage] = useState(1);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [deletingIds, setDeletingIds] = useState<Set<string>>(new Set());
  const [activeOcc, setActiveOcc] = useState<CronOccurrenceFlatDto | null>(null);
  const [runConfirm, setRunConfirm] = useState(false);
  const [bulkConfirm, setBulkConfirm] = useState(false);

  const readOnly = isReadOnly();
  const cronQuery = useCronTicker(id);
  const cron = cronQuery.data;
  const occFilter = useMemo(
    () => ({ pageNumber: occPage, pageSize: PAGE_SIZE, sortDescending: true }),
    [occPage]
  );
  const occQuery = useCronOccurrences(id, occFilter);
  const { data: graph } = useCronTickerGraphById(id, 7, 0);

  const runMutation = useRunCronTicker();
  const deleteOccMutation = useDeleteCronOccurrence();
  const cancelMutation = useCancelTicker();

  const occItems = occQuery.data?.items ?? [];
  const occHighlight = useRowHighlight(
    occQuery.data?.items,
    (o) => o.id,
    (o) => o.createdAt,
    id
  );
  const volumeData = useMemo(
    () => (graph ? bucketsToVolume(graph) : emptyVolumeData()),
    [graph]
  );

  // Reset page + selection when navigating between crons.
  useEffect(() => {
    setOccPage(1);
    setSelected(new Set());
    setActiveOcc(null);
  }, [id]);

  // Subscribe to this cron's occurrence broadcasts (server fires to a Group
  // keyed by cronTickerId). Live row arrivals + status changes flow through
  // the central invalidation in ticker-hub.ts.
  useEffect(() => {
    if (!id) return;
    joinCronGroup(id);
    return () => {
      leaveCronGroup(id);
    };
  }, [id]);

  // Redirect if the cron no longer exists.
  useEffect(() => {
    if (!cronQuery.isLoading && cronQuery.isFetched && !cron) {
      navigate("/cron-tickers");
    }
  }, [cronQuery.isLoading, cronQuery.isFetched, cron, navigate]);

  function toggleOne(occId: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(occId)) next.delete(occId);
      else next.add(occId);
      return next;
    });
  }
  function toggleAll() {
    setSelected((prev) =>
      prev.size === occItems.length ? new Set() : new Set(occItems.map((o) => o.id))
    );
  }

  async function handleRun() {
    try {
      await runMutation.mutateAsync(id);
      toast.success("Cron queued for immediate run");
      setRunConfirm(false);
    } catch (err) {
      toast.error("Failed to run", { description: errMsg(err) });
    }
  }

  async function handleDeleteOne(occId: string) {
    setDeletingIds((prev) => new Set(prev).add(occId));
    try {
      await deleteOccMutation.mutateAsync(occId);
      toast.success("Occurrence deleted");
      if (activeOcc?.id === occId) setActiveOcc(null);
    } catch (err) {
      toast.error("Failed to delete", { description: errMsg(err) });
    } finally {
      setDeletingIds((prev) => {
        const next = new Set(prev);
        next.delete(occId);
        return next;
      });
    }
  }

  async function handleBulkDelete() {
    const ids = Array.from(selected);
    if (ids.length === 0) return;
    setDeletingIds((prev) => new Set([...prev, ...ids]));
    try {
      await Promise.all(ids.map((occId) => deleteOccMutation.mutateAsync(occId)));
      toast.success(ids.length === 1 ? "Occurrence deleted" : `${ids.length} occurrences deleted`);
      setSelected(new Set());
      setBulkConfirm(false);
    } catch (err) {
      toast.error("Failed to delete", { description: errMsg(err) });
    } finally {
      setDeletingIds((prev) => {
        const next = new Set(prev);
        for (const occId of ids) next.delete(occId);
        return next;
      });
    }
  }

  async function handleCancelOcc() {
    if (!activeOcc) return;
    try {
      await cancelMutation.mutateAsync(activeOcc.id);
      setActiveOcc(null);
    } catch (err) {
      toast.error("Cancel failed", { description: errMsg(err) });
    }
  }

  const allSelected = occItems.length > 0 && selected.size === occItems.length;

  const columns = useMemo<ColumnDef<CronOccurrenceFlatDto>[]>(
    () => [
      // Selection only feeds bulk delete — hidden in read-only mode.
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
              cell: ({ row }: { row: { original: CronOccurrenceFlatDto } }) => (
                <Checkbox
                  checked={selected.has(row.original.id)}
                  onCheckedChange={() => toggleOne(row.original.id)}
                  onClick={(e) => e.stopPropagation()}
                  className="h-3.5 w-3.5"
                />
              ),
            } satisfies ColumnDef<CronOccurrenceFlatDto>,
          ]
        : []),
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
        accessorKey: "executedAt",
        header: "Executed At",
        cell: ({ row }) => <TimeCell value={row.original.executedAt} />,
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
        cell: ({ row }) => (
          <span className="tabular-nums text-[11px] text-muted-foreground">
            {row.original.retryCount}
          </span>
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
      {
        accessorKey: "createdAt",
        header: "Created",
        cell: ({ row }) => <TimeCell value={row.original.createdAt} muted />,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [selected, occItems, allSelected, readOnly]
  );

  if (cronQuery.isLoading || !cron) {
    return (
      <div className="flex items-center justify-center py-24 text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin mr-2" /> Loading…
      </div>
    );
  }

  const bare = cron.functionName;
  const stateLabel = !cron.isEnabled
    ? "Disabled"
    : cron.isSystemPaused
      ? "Paused (SDK offline)"
      : "Enabled";

  const bulkBar =
    selected.size > 0 ? (
      <div className="flex items-center justify-between">
        <span className="text-[11px] text-muted-foreground">{selected.size} selected</span>
        <Button
          variant="outline"
          size="sm"
          className="h-8 text-xs rounded-lg gap-1.5 border-status-error/40 text-status-error hover:bg-status-error/10"
          disabled={deleteOccMutation.isPending}
          onClick={() => setBulkConfirm(true)}
        >
          <Trash2 className="h-3 w-3" />
          Delete ({selected.size})
        </Button>
      </div>
    ) : null;

  return (
    <div className="space-y-5">
      {/* Header strip */}
      <div className="rounded-xl border border-border bg-card px-4 py-3 flex flex-wrap items-center gap-x-4 gap-y-2">
        <Button
          variant="ghost"
          size="sm"
          className="h-7 text-xs gap-1"
          onClick={() => navigate("/cron-tickers")}
        >
          <ArrowLeft className="h-3 w-3" /> Cron Tickers
        </Button>
        <div className="h-4 w-px bg-border" />
        <div className="flex flex-col">
          <span className="font-mono text-sm font-semibold">{bare}</span>
        </div>
        <div className="h-4 w-px bg-border" />
        <span className="text-[11px] text-muted-foreground">{stateLabel}</span>
        <PriorityBadge priority={cron.priority} />
        <span className="text-[11px] text-muted-foreground tabular-nums">
          {cron.occurrenceCount} occurrences
        </span>
        <div className="h-4 w-px bg-border" />
        <div className="flex items-center gap-2">
          <span className="font-mono text-[11px] text-foreground">{cron.expression}</span>
          <CronExpressionPreview expression={cron.expression} />
        </div>
        {!readOnly && (
          <Button
            variant="gradient"
            size="sm"
            className="h-8 text-xs rounded-lg ml-auto"
            onClick={() => setRunConfirm(true)}
          >
            <Play className="h-3 w-3 mr-1.5" /> Run now
          </Button>
        )}
      </div>
      {cron.description && (
        <p className="text-[12px] text-muted-foreground -mt-2 px-1">{cron.description}</p>
      )}

      <ExecutionVolumeChart
        data={volumeData}
        title={`${bare} Executions (7d)`}
        gradientPrefix="cronDetail"
      />

      {bulkBar}

      {occQuery.isError && (
        <QueryErrorNotice
          message="Failed to load occurrences."
          onRetry={() => void occQuery.refetch()}
        />
      )}

      <DataTable
        columns={columns}
        data={occItems}
        emptyMessage="No occurrences yet."
        onRowClick={(row) => setActiveOcc(row)}
        page={occPage}
        totalPages={occQuery.data?.totalPages ?? 1}
        totalCount={occQuery.data?.totalCount ?? occItems.length}
        onPageChange={setOccPage}
        isLoading={occQuery.isLoading}
        isFetching={occQuery.isFetching}
        skeletonRows={6}
        highlightedIds={occHighlight}
        deletingIds={deletingIds}
      />

      <ConfirmDialog
        open={runConfirm}
        onOpenChange={setRunConfirm}
        title="Run cron now?"
        description={
          <>
            This queues an immediate run of{" "}
            <span className="font-mono font-semibold text-foreground">{bare}</span> in
            addition to the next scheduled tick (
            <span className="font-mono text-foreground">{cron.expression}</span>). It
            appears as a new occurrence — it does not replace the next scheduled run.
          </>
        }
        confirmLabel="Run now"
        isPending={runMutation.isPending}
        onConfirm={handleRun}
      />

      <ConfirmDialog
        open={bulkConfirm}
        onOpenChange={setBulkConfirm}
        title={`Delete ${selected.size} occurrence${selected.size === 1 ? "" : "s"}?`}
        description="This permanently removes the selected occurrence records. It does not affect the cron schedule itself."
        confirmLabel="Delete"
        confirmVariant="destructive"
        isPending={deleteOccMutation.isPending}
        onConfirm={handleBulkDelete}
      />

      {/* Occurrence detail panel */}
      <ResizableSidePanel open={!!activeOcc} onClose={() => setActiveOcc(null)}>
        {activeOcc && (
          <div className="flex flex-col">
            <div className="px-5 py-4 border-b border-border flex items-start justify-between gap-3">
              <div className="font-mono text-sm font-semibold truncate">
                {activeOcc.functionName}
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <StatusBadge status={activeOcc.status} />
                <button
                  type="button"
                  onClick={() => setActiveOcc(null)}
                  className="text-muted-foreground/60 hover:text-foreground"
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
            </div>

            {!readOnly && (
              <div className="px-5 py-3 border-b border-border flex items-center gap-2">
                <Button
                  variant="destructive"
                  size="sm"
                  className="h-7 text-xs"
                  disabled={activeOcc.status !== "InProgress" || cancelMutation.isPending}
                  onClick={handleCancelOcc}
                >
                  <Ban className="h-3 w-3 mr-1" /> Cancel
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-7 text-xs text-muted-foreground hover:text-status-error"
                  disabled={deletingIds.has(activeOcc.id)}
                  onClick={() => handleDeleteOne(activeOcc.id)}
                >
                  <Trash2 className="h-3 w-3 mr-1" /> Delete
                </Button>
              </div>
            )}

            <div className="px-5 py-3 border-b border-border grid grid-cols-3 gap-3 text-[11px]">
              <Meta label="Scheduled" value={relativeTime(activeOcc.scheduledFor)} />
              <Meta
                label="Duration"
                value={activeOcc.executedAt ? formatDuration(activeOcc.elapsedTime) : "—"}
              />
              <Meta label="Lock Holder" value={activeOcc.lockHolder ?? "—"} />
            </div>

            <div className="p-4 space-y-3">
              <PayloadPreview kind="cron-occurrence" id={activeOcc.id} />
              <LogsPanel
                functionName={activeOcc.functionName}
                status={activeOcc.status}
                tickerId={activeOcc.id}
                exceptionMessage={activeOcc.exceptionMessage}
                skippedReason={activeOcc.skippedReason}
                executedAt={activeOcc.executedAt}
              />
            </div>
          </div>
        )}
      </ResizableSidePanel>
    </div>
  );
}

function Meta({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-muted-foreground/60 uppercase tracking-wider mb-1">{label}</p>
      <p className="tabular-nums">{value}</p>
    </div>
  );
}

function errMsg(err: unknown) {
  return err instanceof Error ? err.message : "An error occurred";
}
