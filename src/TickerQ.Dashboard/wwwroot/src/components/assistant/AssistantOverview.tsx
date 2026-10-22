import { useEffect, useState } from "react";
import { AlertCircle, ArrowRight, CheckCircle2, History, Loader2, MessageSquareText, Radar, SearchCheck, Sparkles } from "lucide-react";
import { cn } from "@/lib/utils";
import { groupInvestigationsByFunction, unreadInvestigationCount } from "@/lib/assistant-investigation";
import { getRuntimeConfig } from "@/lib/runtime-config";
import { getInvestigationOverview, type AssistantInvestigationOverview } from "@/services/assistant-api";
import { InvestigationCard } from "./InvestigationCard";

const ASK = [
  ["Failure patterns", "Which functions are failing repeatedly, and what evidence connects those failures?"],
  ["Scheduler health", "Review scheduler health, active nodes, queued work, and recent failures."],
  ["Monitoring gaps", "Review my integrations and monitors. What important failures are not covered?"],
] as const;

export function AssistantOverview({
  onViewAll,
  onOpen,
  onAsk,
  onContinue,
}: {
  onViewAll: () => void;
  onOpen: (id: string) => void;
  onAsk: (prompt: string) => void;
  onContinue: (investigationId: string) => void;
}) {
  const config = getRuntimeConfig().assistant;
  const [data, setData] = useState<AssistantInvestigationOverview | null>(null);
  const [loading, setLoading] = useState(config.investigations);
  const [error, setError] = useState<string | null>(null);
  const recentGroups = groupInvestigationsByFunction(data?.recent ?? []).slice(0, 6);

  useEffect(() => {
    if (!config.investigations) return;
    let controller: AbortController | null = null;
    let disposed = false;
    const refresh = (showLoading: boolean) => {
      controller?.abort();
      const request = new AbortController();
      controller = request;
      if (showLoading) setLoading(true);
      getInvestigationOverview(request.signal)
        .then((value) => { if (!disposed) { setData(value); setError(null); } })
        .catch((reason) => {
          if (!disposed && !request.signal.aborted)
            setError(reason instanceof Error ? reason.message : "Could not load investigations.");
        })
        .finally(() => { if (!disposed && showLoading) setLoading(false); });
    };
    refresh(true);
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") refresh(false);
    }, 15_000);
    return () => { disposed = true; controller?.abort(); window.clearInterval(timer); };
  }, [config.investigations]);

  return (
    <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_20rem]">
      <section className="space-y-5">
        <div className="overflow-hidden rounded-2xl border border-primary/15 bg-gradient-to-br from-primary/[0.09] via-card to-card p-5 sm:p-6">
          <div className="flex items-start justify-between gap-4">
            <div>
              <div className="mb-3 inline-flex items-center gap-1.5 rounded-full border border-primary/20 bg-primary/10 px-2.5 py-1 text-[10px] font-medium text-primary">
                <Sparkles className="h-3 w-3" /> AI operations
              </div>
              <h2 className="max-w-xl text-xl font-semibold tracking-tight text-foreground">Understand failures before deciding what to run again.</h2>
              <p className="mt-2 max-w-2xl text-[12px] leading-relaxed text-muted-foreground">
                TickerQ combines execution state, retries, logs, workflow context and your configured source provider into a bounded diagnosis.
              </p>
            </div>
            <div className="hidden h-12 w-12 shrink-0 place-items-center rounded-2xl bg-primary/10 text-primary sm:grid">
              <SearchCheck className="h-6 w-6" />
            </div>
          </div>
          <div className="mt-5 flex flex-wrap gap-2 text-[10.5px]">
            <span className="rounded-md border border-border bg-background/60 px-2 py-1">{config.history ? "Conversation history on" : "Browser-local chat"}</span>
            <span className="rounded-md border border-border bg-background/60 px-2 py-1">{config.sourceContext ? "Source context on" : "Source context off"}</span>
            <span className="rounded-md border border-border bg-background/60 px-2 py-1">{config.automaticInvestigations ? "Automatic investigation on" : "Manual investigation"}</span>
          </div>
        </div>

        {config.investigations ? (
          <>
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
              <Metric label="Unread findings" value={data?.awaitingReview} icon={AlertCircle} tone="warning" loading={loading} />
              <Metric label="Completed today" value={data?.completedToday} icon={SearchCheck} tone="primary" loading={loading} />
              <Metric label="Recoveries" value={data?.recovered} icon={CheckCircle2} tone="success" loading={loading} />
              <Metric label="All investigations" value={data?.total} icon={History} tone="muted" loading={loading} />
            </div>

            <div>
              <div className="mb-3 flex items-center justify-between">
                <div>
                  <h2 className="text-sm font-semibold">Recent investigations</h2>
                  <p className="mt-0.5 text-[10.5px] text-muted-foreground">Shared within this application namespace.</p>
                </div>
                <button type="button" onClick={onViewAll} className="inline-flex items-center gap-1 text-[11px] font-medium text-primary">
                  View all <ArrowRight className="h-3 w-3" />
                </button>
              </div>
              {loading ? <Loading /> : error ? <ErrorText value={error} /> : recentGroups.length ? (
                <div className="grid gap-3 md:grid-cols-2">
                  {recentGroups.map((group) => {
                    const [latest, ...earlier] = group;
                    return (
                    <InvestigationCard
                      key={latest.functionName}
                      value={latest}
                      stackCount={earlier.length + 1}
                      unreadCount={unreadInvestigationCount(group)}
                      onOpen={() => onOpen(latest.id)}
                      onContinue={() => onContinue(latest.id)}
                    />
                    );
                  })}
                </div>
              ) : (
                <div className="rounded-xl border border-dashed border-border py-10 text-center">
                  <Radar className="mx-auto h-5 w-5 text-muted-foreground/40" />
                  <p className="mt-2 text-[12px] font-medium">No saved investigations yet</p>
                  <p className="mt-1 text-[10.5px] text-muted-foreground">Open a failed execution and choose Investigate with AI.</p>
                </div>
              )}
            </div>
          </>
        ) : (
          <div className="rounded-xl border border-dashed border-border p-5">
            <div className="flex items-start gap-3">
              <History className="mt-0.5 h-4 w-4 text-muted-foreground" />
              <div>
                <p className="text-[12px] font-medium">Structured investigation history is not configured</p>
                <p className="mt-1 text-[11px] leading-relaxed text-muted-foreground">
                  Chat remains available and is kept in this browser. Configure an investigation store, or call EF Core&apos;s AddAssistantHistory(), to get the shared overview and automatic deduplication.
                </p>
              </div>
            </div>
          </div>
        )}
      </section>

      <aside className="space-y-5">
        {data?.patterns.length ? (
          <div className="rounded-xl border border-border bg-card p-4">
            <h2 className="text-[12px] font-semibold">Repeated patterns</h2>
            <div className="mt-3 space-y-3">
              {data.patterns.map((pattern) => (
                <div key={`${pattern.functionName}:${pattern.classification}`} className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <p className="truncate text-[11px] font-medium">{pattern.functionName}</p>
                    <p className="text-[9.5px] text-muted-foreground">{pattern.classification}</p>
                  </div>
                  <span className="rounded-full bg-status-error/10 px-2 py-0.5 text-[9.5px] font-medium text-status-error">{pattern.count}×</span>
                </div>
              ))}
            </div>
          </div>
        ) : null}

        <div className="rounded-xl border border-border bg-card p-4">
          <h2 className="text-[12px] font-semibold">Ask about this runtime</h2>
          <div className="mt-3 space-y-2">
            {ASK.map(([title, prompt]) => (
              <button key={title} type="button" onClick={() => onAsk(prompt)}
                className="group flex w-full items-center justify-between rounded-lg border border-border px-3 py-2.5 text-left transition-colors hover:border-primary/25 hover:bg-primary/[0.035]">
                <span>
                  <span className="block text-[11px] font-medium">{title}</span>
                  <span className="mt-0.5 block text-[9.5px] text-muted-foreground">Open in chat</span>
                </span>
                <MessageSquareText className="h-3.5 w-3.5 text-muted-foreground group-hover:text-primary" />
              </button>
            ))}
          </div>
        </div>
      </aside>
    </div>
  );
}

function Metric({ label, value, icon: Icon, tone, loading }: { label: string; value?: number; icon: typeof AlertCircle; tone: "warning" | "primary" | "success" | "muted"; loading: boolean }) {
  const colors = { warning: "text-status-warning bg-status-warning/10", primary: "text-primary bg-primary/10", success: "text-status-success bg-status-success/10", muted: "text-muted-foreground bg-surface-2" };
  return <div className="rounded-xl border border-border bg-card p-3.5"><div className={cn("grid h-7 w-7 place-items-center rounded-lg", colors[tone])}><Icon className="h-3.5 w-3.5" /></div><p className="mt-3 text-xl font-semibold tabular-nums">{loading ? "—" : (value ?? 0).toLocaleString()}</p><p className="mt-0.5 text-[10px] text-muted-foreground">{label}</p></div>;
}

function Loading() { return <div className="grid place-items-center py-12"><Loader2 className="h-4 w-4 animate-spin text-muted-foreground" /></div>; }
function ErrorText({ value }: { value: string }) { return <p role="alert" className="rounded-lg border border-status-error/20 bg-status-error/5 p-3 text-[11px] text-status-error">{value}</p>; }
