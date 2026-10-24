import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { ArrowLeft, ArrowRight, Check, CheckCircle2, ExternalLink, Layers3, Loader2, RotateCcw, Search, Trash2, X } from "lucide-react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { ChatMarkdown } from "./ChatMarkdown";
import { InvestigationCard } from "./InvestigationCard";
import { groupInvestigationsByFunction, unreadInvestigationCount } from "@/lib/assistant-investigation";
import { formatAbsolute } from "@/lib/cron/format";
import { getRuntimeConfig } from "@/lib/runtime-config";
import { useTimezone } from "@/lib/timezone";
import { deleteInvestigation, deleteInvestigationGroup, getInvestigation, listInvestigations, setInvestigationReviewState, type AssistantInvestigation, type AssistantInvestigationPage } from "@/services/assistant-api";

type DeleteTarget =
  | { kind: "single"; id: string; functionName: string }
  | { kind: "group"; functionName: string };

export function AssistantInvestigations({
  initialId,
  onContinue,
}: {
  initialId?: string | null;
  onContinue: (investigationId: string) => void;
}) {
  const available = getRuntimeConfig().assistant.investigations;
  const readOnly = getRuntimeConfig().readOnly;
  const { timezone } = useTimezone();
  const [search, setSearch] = useState("");
  const [reviewState, setReviewState] = useState("");
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<AssistantInvestigationPage | null>(null);
  const [selected, setSelected] = useState<AssistantInvestigation | null>(null);
  const [loading, setLoading] = useState(available);
  const [detailLoading, setDetailLoading] = useState(Boolean(available && initialId));
  const [error, setError] = useState<string | null>(null);
  const [updating, setUpdating] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<DeleteTarget | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionNotice, setActionNotice] = useState<string | null>(null);
  const markingRead = useRef(new Set<string>());

  const delayedSearch = useDebounced(search, 250);
  const investigationGroups = groupInvestigationsByFunction(result?.items ?? []);
  const selectedHistory = selected
    ? historyForFunction(result?.items ?? [], selected)
    : [];

  const replaceInvestigation = useCallback((updated: AssistantInvestigation) => {
    setSelected((current) => current?.id === updated.id ? updated : current);
    setResult((current) => current ? {
      ...current,
      items: current.items.map((item) => item.id === updated.id ? updated : item),
    } : current);
  }, []);

  const openInvestigation = useCallback((value: AssistantInvestigation) => {
    setActionError(null);
    setActionNotice(null);
    if (readOnly || value.reviewState !== "New") {
      setSelected(value);
      return;
    }

    const opened: AssistantInvestigation = { ...value, reviewState: "Reviewed" };
    setSelected(opened);
    setResult((current) => current ? {
      ...current,
      items: current.items.map((item) => item.id === opened.id ? opened : item),
    } : current);

    if (markingRead.current.has(value.id)) return;
    markingRead.current.add(value.id);
    void setInvestigationReviewState(value.id, "Reviewed")
      .then((updated) => {
        replaceInvestigation(updated);
        setReloadToken((token) => token + 1);
      })
      .catch((reason) => {
        setSelected((current) => current?.id === value.id && current.reviewState === "Reviewed" ? value : current);
        setResult((current) => current ? {
          ...current,
          items: current.items.map((item) => item.id === value.id && item.reviewState === "Reviewed" ? value : item),
        } : current);
        setActionError(reason instanceof Error ? reason.message : "Could not mark this investigation as read.");
      })
      .finally(() => markingRead.current.delete(value.id));
  }, [readOnly, replaceInvestigation]);

  useEffect(() => {
    if (!available) return;
    const controller = new AbortController();
    listInvestigations({ search: delayedSearch, reviewState, groupByFunction: true, page, pageSize: 20 }, controller.signal)
      .then((value) => {
        setResult(value);
        setSelected((current) => current
          ? value.items.find((item) => item.id === current.id) ?? current
          : current);
      })
      .catch((reason) => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "Could not load investigations."); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [available, delayedSearch, reviewState, page, reloadToken]);

  useEffect(() => {
    if (!available || !initialId) return;
    const controller = new AbortController();
    getInvestigation(initialId, controller.signal).then((value) => {
      if (!controller.signal.aborted) openInvestigation(value);
    }).catch(() => {}).finally(() => { if (!controller.signal.aborted) setDetailLoading(false); });
    return () => controller.abort();
  }, [available, initialId, openInvestigation]);

  async function updateReview(next: "Reviewed" | "Resolved") {
    if (!selected || updating) return;
    setUpdating(true);
    setActionError(null);
    setActionNotice(null);
    try {
      const updated = await setInvestigationReviewState(selected.id, next);
      replaceInvestigation(updated);
      setActionNotice(next === "Reviewed" ? "Marked as read." : "Investigation resolved.");
      setReloadToken((value) => value + 1);
    } catch (reason) {
      setActionError(reason instanceof Error ? reason.message : "Could not update this investigation.");
    } finally {
      setUpdating(false);
    }
  }

  async function reopen() {
    if (!selected || updating) return;
    setUpdating(true);
    setActionError(null);
    setActionNotice(null);
    try {
      const updated = await setInvestigationReviewState(selected.id, "New");
      replaceInvestigation(updated);
      setActionNotice("Moved back to unread.");
      setReloadToken((value) => value + 1);
    } catch (reason) {
      setActionError(reason instanceof Error ? reason.message : "Could not reopen this investigation.");
    } finally {
      setUpdating(false);
    }
  }

  async function confirmDelete() {
    const target = pendingDelete;
    if (!target || readOnly || deleting) return;
    setDeleting(true);
    try {
      if (target.kind === "single") await deleteInvestigation(target.id);
      else await deleteInvestigationGroup(target.functionName);

      setResult((current) => {
        if (!current) return current;
        const items = current.items.filter((item) => target.kind === "single"
          ? item.id !== target.id
          : item.functionName !== target.functionName);
        return {
          ...current,
          items,
          totalCount: Math.max(0, current.totalCount - (current.items.length - items.length)),
        };
      });
      setSelected((current) => current && (target.kind === "single"
        ? current.id === target.id
        : current.functionName === target.functionName) ? null : current);
      setPendingDelete(null);
      setPage(1);
      setReloadToken((value) => value + 1);
      toast.success(target.kind === "single" ? "Investigation deleted" : "Investigation group deleted");
    } catch (reason) {
      toast.error("Could not delete investigation", {
        description: reason instanceof Error ? reason.message : "Please refresh and try again.",
      });
    } finally {
      setDeleting(false);
    }
  }

  if (!available) return <div className="rounded-xl border border-dashed border-border py-16 text-center"><p className="text-sm font-medium">Investigation history is not configured</p><p className="mt-1 text-[11px] text-muted-foreground">Add a durable investigation store to search and review diagnoses across nodes.</p></div>;

  return (
    <>
    <div className="grid min-h-[34rem] overflow-hidden rounded-xl border border-border bg-card lg:grid-cols-[minmax(19rem,0.85fr)_minmax(24rem,1.15fr)]">
      <section className="min-w-0 border-b border-border lg:border-b-0 lg:border-r">
        <div className="flex gap-2 border-b border-border p-3">
          <label className="relative min-w-0 flex-1"><Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" /><input value={search} onChange={(e) => { setLoading(true); setError(null); setSearch(e.target.value); setPage(1); }} placeholder="Function or execution ID" className="h-9 w-full rounded-lg border border-border bg-surface-1 pl-8 pr-8 text-[11.5px] outline-none focus:border-primary/45" />{search ? <button type="button" onClick={() => { setLoading(true); setError(null); setSearch(""); setPage(1); }} className="absolute right-2.5 top-1/2 -translate-y-1/2 text-muted-foreground"><X className="h-3 w-3" /></button> : null}</label>
          <select value={reviewState} onChange={(e) => { setLoading(true); setError(null); setReviewState(e.target.value); setPage(1); }} className="h-9 rounded-lg border border-border bg-surface-1 px-2 text-[11px] outline-none">
            <option value="">All states</option><option value="New">Unread</option><option value="Reviewed">Read</option><option value="Resolved">Resolved</option>
          </select>
        </div>
        <div className="max-h-[calc(100vh-18rem)] space-y-2 overflow-y-auto p-3 scrollbar-thin">
          {loading ? <div className="grid place-items-center py-16"><Loader2 className="h-4 w-4 animate-spin text-muted-foreground" /></div> : error ? <p className="p-3 text-[11px] text-status-error">{error}</p> : investigationGroups.length ? investigationGroups.map((group) => {
            const [latest, ...earlier] = group;
            return <InvestigationCard key={latest.functionName} value={latest} stackCount={group.length} unreadCount={unreadInvestigationCount(group)} selected={selectedHistory.some((item) => item.id === latest.id) || earlier.some((item) => item.id === selected?.id)} onOpen={() => openInvestigation(latest)} onDeleteGroup={!readOnly && group.length > 1 ? () => setPendingDelete({ kind: "group", functionName: latest.functionName }) : undefined} />;
          }) : <p className="py-16 text-center text-[11px] text-muted-foreground">No investigations match these filters.</p>}
        </div>
        {result && result.totalPages > 1 ? <div className="flex items-center justify-between border-t border-border px-3 py-2"><button type="button" disabled={page <= 1} onClick={() => { setLoading(true); setError(null); setPage((x) => x - 1); }} className="rounded-md border border-border p-1.5 disabled:opacity-30"><ArrowLeft className="h-3 w-3" /></button><span className="text-[10px] text-muted-foreground">Page {page} of {result.totalPages}</span><button type="button" disabled={page >= result.totalPages} onClick={() => { setLoading(true); setError(null); setPage((x) => x + 1); }} className="rounded-md border border-border p-1.5 disabled:opacity-30"><ArrowRight className="h-3 w-3" /></button></div> : null}
      </section>

      <section className="min-w-0 bg-surface-0/30">
        {detailLoading ? <div className="grid h-full place-items-center"><Loader2 className="h-4 w-4 animate-spin text-muted-foreground" /></div> : selected ? (
          <div className="flex h-full flex-col">
            <div className="border-b border-border p-4 sm:p-5">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0"><p className="text-[10px] uppercase tracking-[0.12em] text-muted-foreground">{selected.classification}</p><h2 className="mt-1 truncate text-base font-semibold">{selected.functionName}</h2><p className="mt-1 font-mono text-[9.5px] text-muted-foreground">{selected.executionId}</p></div>
                <div className="flex gap-2"><Link to="/executions" className="inline-flex h-8 items-center gap-1 rounded-lg border border-border px-2.5 text-[10.5px] hover:bg-surface-2">Open executions <ExternalLink className="h-3 w-3" /></Link><button type="button" onClick={() => onContinue(selected.id)} className="h-8 rounded-lg bg-primary px-3 text-[10.5px] font-medium text-primary-foreground">Continue in chat</button></div>
              </div>
              <div className="mt-4 flex flex-wrap gap-2 text-[9.5px]"><Pill>{selected.state}</Pill><Pill>{selected.reviewState}</Pill><Pill>{selected.trigger}</Pill>{selected.recoveredAt ? <Pill success>Recovery detected</Pill> : null}{selected.sourcePath ? <Pill>{selected.sourcePath}{selected.sourceLine ? `:${selected.sourceLine}` : ""}</Pill> : null}</div>
              {selectedHistory.length > 1 ? (
                <label className="mt-4 flex min-w-0 items-center gap-2 rounded-lg border border-border bg-surface-1 px-3 py-2">
                  <Layers3 className="h-3.5 w-3.5 shrink-0 text-primary" />
                  <span className="shrink-0 text-[10px] font-medium text-muted-foreground">
                    {selectedHistory.length} investigations
                  </span>
                  <select
                    aria-label={`Investigations for ${selected.functionName}`}
                    value={selected.id}
                    onChange={(event) => {
                      const next = selectedHistory.find((item) => item.id === event.target.value);
                      if (!next) return;
                      openInvestigation(next);
                    }}
                    className="h-7 min-w-0 flex-1 rounded-md border border-border bg-card px-2 text-[10px] text-foreground outline-none focus:border-primary/45"
                  >
                    {selectedHistory.map((item, index) => (
                      <option key={item.id} value={item.id}>
                        {index === 0 ? "Latest · " : ""}{historyOptionLabel(item, timezone)}
                      </option>
                    ))}
                  </select>
                </label>
              ) : null}
            </div>
            <div className="flex-1 overflow-y-auto p-4 sm:p-5 scrollbar-thin">
              {selected.state === "Completed" ? <div className="rounded-xl border border-border bg-card p-4 text-[12px] leading-relaxed">{selected.summary ? <ChatMarkdown content={selected.summary} /> : <p className="text-muted-foreground">Investigation completed without a saved summary.</p>}</div> : <p className="text-[12px] text-muted-foreground">{selected.failureMessage || "This investigation is still processing."}</p>}
              {selected.recoveredAt ? <div className="mt-4 flex items-start gap-2 rounded-lg border border-status-success/20 bg-status-success/5 p-3"><CheckCircle2 className="mt-0.5 h-4 w-4 text-status-success" /><div><p className="text-[11px] font-medium text-status-success">A later execution succeeded</p><p className="mt-0.5 text-[10px] text-muted-foreground">Recovery is evidence of a successful later run; an operator can still decide whether the incident is resolved.</p></div></div> : null}
            </div>
            {!readOnly ? (
              <div className="border-t border-border p-3">
                <div className="flex min-h-5 items-center justify-between gap-3 pb-2 text-[10.5px]" aria-live="polite">
                  <span className={actionError ? "text-status-error" : "text-status-success"}>{actionError || actionNotice}</span>
                  {updating ? <span className="inline-flex items-center gap-1 text-muted-foreground"><Loader2 className="h-3 w-3 animate-spin" />Saving…</span> : null}
                </div>
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <button type="button" disabled={deleting} onClick={() => setPendingDelete({ kind: "single", id: selected.id, functionName: selected.functionName })} className="inline-flex h-8 items-center gap-1 rounded-lg border border-status-error/20 px-3 text-[10.5px] text-status-error disabled:opacity-40"><Trash2 className="h-3 w-3" />Delete investigation</button>
                  {selected.state === "Completed" ? <div className="flex flex-wrap justify-end gap-2">
                    {selected.reviewState === "Resolved" ? (
                      <button type="button" disabled={updating} onClick={() => void reopen()} className="inline-flex h-8 items-center gap-1 rounded-lg border border-border px-3 text-[10.5px] disabled:opacity-40"><RotateCcw className="h-3 w-3" />Reopen as unread</button>
                    ) : (
                      <>
                        {selected.reviewState === "New" ? <button type="button" disabled={updating} onClick={() => void updateReview("Reviewed")} className="inline-flex h-8 items-center gap-1 rounded-lg border border-border px-3 text-[10.5px] disabled:opacity-40"><Check className="h-3 w-3" />Mark as read</button> : <button type="button" disabled={updating} onClick={() => void reopen()} className="inline-flex h-8 items-center gap-1 rounded-lg border border-border px-3 text-[10.5px] disabled:opacity-40"><RotateCcw className="h-3 w-3" />Mark unread</button>}
                        <button type="button" disabled={updating} onClick={() => void updateReview("Resolved")} className="inline-flex h-8 items-center gap-1 rounded-lg bg-status-success/10 px-3 text-[10.5px] font-medium text-status-success disabled:opacity-40"><CheckCircle2 className="h-3 w-3" />Resolve</button>
                      </>
                    )}
                  </div> : null}
                </div>
              </div>
            ) : null}
          </div>
        ) : <div className="grid h-full place-items-center px-6 text-center"><div><Search className="mx-auto h-5 w-5 text-muted-foreground/40" /><p className="mt-2 text-[12px] font-medium">Select an investigation</p><p className="mt-1 text-[10.5px] text-muted-foreground">Review evidence, recovery state, and the saved diagnosis.</p></div></div>}
      </section>
    </div>
    <ConfirmDialog
      open={pendingDelete !== null}
      onOpenChange={(open) => { if (!open) setPendingDelete(null); }}
      title={pendingDelete?.kind === "group" ? "Delete investigation group?" : "Delete investigation?"}
      description={pendingDelete?.kind === "group"
        ? <>Permanently deletes every saved investigation for <strong>{pendingDelete.functionName}</strong> in this application namespace, including items not shown on this page. Job executions and chat history remain unchanged.</>
        : <>Permanently deletes this saved investigation for <strong>{pendingDelete?.functionName}</strong>. The job execution and chat history remain unchanged.</>}
      confirmLabel={pendingDelete?.kind === "group" ? "Delete group" : "Delete investigation"}
      confirmVariant="destructive"
      isPending={deleting}
      onConfirm={confirmDelete}
    />
    </>
  );
}

function Pill({ children, success = false }: { children: ReactNode; success?: boolean }) { return <span className={success ? "rounded-full bg-status-success/10 px-2 py-1 text-status-success" : "rounded-full border border-border bg-surface-2 px-2 py-1 text-muted-foreground"}>{children}</span>; }

function historyForFunction(
  items: readonly AssistantInvestigation[],
  selected: AssistantInvestigation,
): AssistantInvestigation[] {
  const history = items.filter((item) => item.functionName === selected.functionName);
  return history.some((item) => item.id === selected.id) ? history : [...history, selected];
}

function historyOptionLabel(item: AssistantInvestigation, timezone: string): string {
  return `${item.classification} · ${formatAbsolute(item.occurredAt || item.createdAt, timezone)}`;
}

function useDebounced<T>(value: T, delay: number): T {
  const [result, setResult] = useState(value);
  useEffect(() => { const timer = window.setTimeout(() => setResult(value), delay); return () => window.clearTimeout(timer); }, [value, delay]);
  return result;
}
