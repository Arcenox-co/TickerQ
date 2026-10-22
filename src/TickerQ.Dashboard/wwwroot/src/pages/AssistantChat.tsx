import { useCallback, useEffect, useRef, useState } from "react";
import { Bot, History, LayoutDashboard, Loader2, MessageSquare, Plus, Search, SearchCheck, SendHorizonal, ShieldCheck, Sparkles, Trash2, X } from "lucide-react";
import { useSearchParams } from "react-router-dom";
import { cn } from "@/lib/utils";
import { toast } from "sonner";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { ChatMarkdown } from "@/components/assistant/ChatMarkdown";
import { ProposalCard, type ProposalStatus } from "@/components/assistant/ProposalCard";
import { getRuntimeConfig, isAssistantEnabled, resolveDashboardUrl } from "@/lib/runtime-config";
import {
  deleteConversation,
  getConversationMessages,
  getInvestigation,
  listConversations,
  setInvestigationReviewState,
  setAssistantProposalState,
  streamChat,
  type AssistantInvestigation,
  type ConversationSummary,
} from "@/services/assistant-api";

import { useAuth } from "@/lib/auth/auth-context";
import { assistantStorageKey, loadAssistantTranscript, storeAssistantTranscript, clearAssistantTranscript } from "@/lib/assistant-storage";
import { LatestRequest } from "@/lib/latest-request";
import { boundTranscript, chatItemsToTurns, chatMessage, historyMessagesToChatItems, MAX_CHAT_MESSAGES, MAX_CHAT_MESSAGE_CHARS, updateProposalStatus, type ChatItem } from "@/lib/assistant-transcript";
import { investigationPrompt } from "@/lib/assistant-investigation";
import { AssistantOverview } from "@/components/assistant/AssistantOverview";
import { AssistantInvestigations } from "@/components/assistant/AssistantInvestigations";
import { InvestigationCard } from "@/components/assistant/InvestigationCard";
import type { AssistantProposalApplyResult } from "@/lib/apply-assistant-proposal";

const SUGGESTIONS = [
  "Why are my jobs failing?",
  "Inspect the logs for the most recent failed execution",
  "Which registered functions can I schedule?",
  "Is the scheduler healthy right now?",
  "Show my integrations and monitor status",
];

export default function AssistantChatPage() {
  const auth = useAuth();
  const runtime = getRuntimeConfig();
  const config = runtime.assistant;
  const dashboardUrl = new URL(resolveDashboardUrl("/"), window.location.origin).href;
  const storageKey = assistantStorageKey(auth.userId, dashboardUrl);
  const [searchParams, setSearchParams] = useSearchParams();
  const executionToInvestigate = searchParams.get("investigate");
  const savedInvestigationId = searchParams.get("investigation");
  const requestedView = searchParams.get("view");
  const view = executionToInvestigate || savedInvestigationId
    ? "chat"
    : requestedView === "chat" || requestedView === "investigations" ? requestedView : "overview";
  const [draftPrompt, setDraftPrompt] = useState("");

  function openChat(prompt = "") {
    setDraftPrompt(prompt);
    setSearchParams({ view: "chat" });
  }

  function continueInvestigation(investigationId: string) {
    setDraftPrompt("");
    setSearchParams({ view: "chat", investigation: investigationId });
  }

  if (!isAssistantEnabled()) {
    return (
      <div className="flex flex-col items-center justify-center py-24 gap-2 text-center">
        <Sparkles className="h-6 w-6 text-muted-foreground/40" />
        <p className="text-sm font-medium">AI assistant is not configured</p>
        <p className="text-[12px] text-muted-foreground max-w-md">
          The operator hasn&apos;t provided an AI provider. Configure it server-side with
          <code className="mx-1 rounded bg-surface-2 px-1 py-0.5 text-[11px]">AddAssistant(a =&gt; a.UseChatClient(…))</code>.
        </p>
      </div>
    );
  }

  return (
    <div className="mx-auto flex h-[calc(100vh-7.5rem)] max-w-7xl flex-col">
      <header className="flex flex-wrap items-start justify-between gap-4 pb-4">
        <div className="flex min-w-0 items-start gap-3">
          <div className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-primary/15 text-primary"><Bot className="h-5 w-5" /></div>
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-lg font-semibold tracking-tight">TickerQ Assistant</h1>
              <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-500/20 bg-emerald-500/8 px-2 py-0.5 text-[9.5px] font-medium text-emerald-400">
                <span className="h-1.5 w-1.5 rounded-full bg-emerald-400" />Connected
              </span>
            </div>
            <p className="mt-0.5 text-[11px] text-muted-foreground">Investigate runs, inspect live scheduler data, and prepare changes for approval.</p>
            <div className="mt-2 flex flex-wrap gap-1.5 text-[9.5px] text-muted-foreground">
              {config.model ? <AssistantCapability label="Model" value={config.model} /> : null}
              <AssistantCapability value="Live scheduler context" />
              {config.history ? <AssistantCapability value="Private chat history" /> : null}
              {config.automaticInvestigations ? <AssistantCapability value="Automatic investigations" /> : config.investigations ? <AssistantCapability value="Run investigations" /> : null}
              {config.sourceContext ? <AssistantCapability value="Source context" /> : null}
              <AssistantCapability value={runtime.readOnly ? "Read-only" : "Actions require approval"} />
            </div>
          </div>
        </div>
        <nav className="flex rounded-xl border border-border bg-surface-1 p-1" aria-label="AI workspace">
          <ViewTab active={view === "overview"} icon={LayoutDashboard} label="Overview" onClick={() => { setDraftPrompt(""); setSearchParams({ view: "overview" }); }} />
          <ViewTab active={view === "chat"} icon={MessageSquare} label="Chat" onClick={() => { setDraftPrompt(""); setSearchParams({ view: "chat" }); }} />
          <ViewTab active={view === "investigations"} icon={SearchCheck} label="Investigations" disabled={!config.investigations} onClick={() => setSearchParams({ view: "investigations" })} />
        </nav>
      </header>

      <main className="min-h-0 flex-1 overflow-y-auto scrollbar-thin">
        {view === "overview" ? (
          <AssistantOverview
            onViewAll={() => setSearchParams({ view: "investigations" })}
            onOpen={(id) => setSearchParams({ view: "investigations", id })}
            onAsk={openChat}
            onContinue={continueInvestigation}
          />
        ) : view === "investigations" ? (
          <AssistantInvestigations
            key={searchParams.get("id") ?? "investigations"}
            initialId={searchParams.get("id")}
            onContinue={continueInvestigation}
          />
        ) : (
          <AssistantChat
            key={`${auth.status}:${storageKey ?? "memory"}:${executionToInvestigate ?? savedInvestigationId ?? draftPrompt}`}
            storageKey={storageKey}
            initialPrompt={draftPrompt || investigationPrompt(executionToInvestigate)}
            investigationId={savedInvestigationId}
            onClearInvestigation={() => setSearchParams({ view: "chat" })}
            onContinueInvestigation={continueInvestigation}
          />
        )}
      </main>
    </div>
  );
}

function AssistantCapability({ label, value }: { label?: string; value: string }) {
  return (
    <span className="rounded-md border border-border/70 bg-surface-1/70 px-2 py-1">
      {label ? <span className="mr-1 text-muted-foreground/65">{label}</span> : null}
      <span className="font-medium text-foreground/80">{value}</span>
    </span>
  );
}

function ViewTab({ active, icon: Icon, label, disabled = false, onClick }: { active: boolean; icon: typeof Bot; label: string; disabled?: boolean; onClick: () => void }) {
  return <button type="button" disabled={disabled} onClick={onClick} className={cn("inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-[11px] font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-35", active ? "bg-card text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground")}><Icon className="h-3.5 w-3.5" />{label}</button>;
}

function AssistantChat({ storageKey, initialPrompt, investigationId, onClearInvestigation, onContinueInvestigation }: {
  storageKey: string | null;
  initialPrompt: string;
  investigationId: string | null;
  onClearInvestigation: () => void;
  onContinueInvestigation: (investigationId: string) => void;
}) {
  const runtime = getRuntimeConfig();
  const { history } = runtime.assistant;
  const readOnly = runtime.readOnly;
  const serverHistory = history && storageKey !== null;

  const [input, setInput] = useState(() => initialPrompt);
  const [items, setItems] = useState<ChatItem[]>(() => {
    try { return serverHistory ? [] : loadAssistantTranscript(window.localStorage, storageKey); } catch { return []; }
  });
  const [transcriptTrimmed, setTranscriptTrimmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [loadingThread, setLoadingThread] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<ConversationSummary | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [historyQuery, setHistoryQuery] = useState("");
  const scrollRef = useRef<HTMLDivElement>(null);
  const abortRef = useRef<AbortController | null>(null);
  const threadRequest = useRef(new LatestRequest());
  const listRequest = useRef(new LatestRequest());
  const [threadError, setThreadError] = useState<string | null>(null);
  const [investigation, setInvestigation] = useState<AssistantInvestigation | null>(null);
  const [investigationLoading, setInvestigationLoading] = useState(Boolean(investigationId));
  const [investigationError, setInvestigationError] = useState<string | null>(null);

  const refreshConversations = useCallback(() => {
    if (!serverHistory) return;
    const request = listRequest.current.begin();
    listConversations(request.signal)
      .then((rows) => { if (request.isCurrent()) setConversations(rows); })
      .catch(() => { if (request.isCurrent()) setConversations([]); });
  }, [serverHistory]);

  useEffect(() => refreshConversations(), [refreshConversations]);

  useEffect(() => {
    if (!investigationId) return;
    const controller = new AbortController();
    getInvestigation(investigationId, controller.signal)
      .then((value) => {
        if (controller.signal.aborted) return;
        setInvestigationError(null);
        if (readOnly || value.reviewState !== "New") {
          setInvestigation(value);
          return;
        }

        setInvestigation({ ...value, reviewState: "Reviewed" });
        void setInvestigationReviewState(value.id, "Reviewed")
          .then((updated) => { if (!controller.signal.aborted) setInvestigation(updated); })
          .catch(() => { if (!controller.signal.aborted) setInvestigation(value); });
      })
      .catch((reason) => {
        if (!controller.signal.aborted)
          setInvestigationError(reason instanceof Error ? reason.message : "Could not load this investigation.");
      })
      .finally(() => { if (!controller.signal.aborted) setInvestigationLoading(false); });
    return () => controller.abort();
  }, [investigationId, readOnly]);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
  }, [items]);

  // localStorage persistence only applies in fallback mode.
  useEffect(() => {
    try { if (!serverHistory && !busy) storeAssistantTranscript(window.localStorage, storageKey, items); } catch { /* Storage is optional. */ }
  }, [items, busy, serverHistory, storageKey]);

  useEffect(() => {
    const threads = threadRequest.current;
    const list = listRequest.current;
    return () => { abortRef.current?.abort(); threads.cancel(); list.cancel(); };
  }, []);

  function startNewChat() {
    abortRef.current?.abort();
    threadRequest.current.cancel();
    setLoadingThread(false);
    setThreadError(null);
    setItems([]);
    setTranscriptTrimmed(false);
    setActiveId(null);
    if (investigationId) onClearInvestigation();
    if (!serverHistory) {
      try {
        clearAssistantTranscript(window.localStorage, storageKey);
      } catch {
        /* ignore */
      }
    }
  }

  async function openConversation(id: string) {
    if (busy || id === activeId) return;
    const request = threadRequest.current.begin();
    setLoadingThread(true);
    setThreadError(null);
    setItems([]);
    setActiveId(id);
    try {
      const history = await getConversationMessages(id, request.signal);
      if (!request.isCurrent()) return;
      setItems(historyMessagesToChatItems(history));
      setTranscriptTrimmed(history.filter((message) => message.role !== "proposal").length > MAX_CHAT_MESSAGES);
    } catch {
      if (!request.isCurrent()) return;
      setItems([]);
      setActiveId(null);
      setTranscriptTrimmed(false);
      setThreadError("Could not load this conversation. Please select it again to retry.");
    } finally {
      if (request.isCurrent()) setLoadingThread(false);
    }
  }

  async function removeConversation() {
    if (!pendingDelete || deleting || busy || loadingThread) return;
    const { id } = pendingDelete;
    setDeleting(true);
    setDeleteError(null);
    try {
      await deleteConversation(id);
      if (id === activeId) startNewChat();
      setPendingDelete(null);
      refreshConversations();
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : "Failed to delete conversation. Please try again.");
    } finally {
      setDeleting(false);
    }
  }

  async function setProposalStatus(id: string, status: ProposalStatus, result?: AssistantProposalApplyResult) {
    setItems((prev) => updateProposalStatus(prev, id, status, result));
    if (!serverHistory || status === "pending") return;
    try {
      await setAssistantProposalState(id, status, result);
    } catch {
      // The draft/action has already succeeded. Keep that truthful local state and warn
      // only about history synchronization so the operator does not apply it twice.
      toast.warning("The action succeeded, but its card state was not saved to chat history.");
    }
  }

  const visibleConversations = conversations.filter((conversation) =>
    (conversation.title || "Untitled").toLocaleLowerCase().includes(historyQuery.trim().toLocaleLowerCase()));
  const activeConversation = conversations.find((conversation) => conversation.id === activeId);
  const investigationDraft = Boolean(investigationId) || initialPrompt.startsWith("Investigate execution ");

  async function send(text: string) {
    const question = text.trim().slice(0, MAX_CHAT_MESSAGE_CHARS);
    if (!question || busy || loadingThread || investigationLoading || investigationError) return;
    if (items.filter((item) => item.type === "message").length + 2 > MAX_CHAT_MESSAGES)
      setTranscriptTrimmed(true);

    const turns = chatItemsToTurns([
      ...items,
      chatMessage("user", question),
    ]);

    setItems((prev) => boundTranscript([
      ...prev,
      chatMessage("user", question),
      chatMessage("assistant", ""),
    ]));
    setInput("");
    setBusy(true);

    const ac = new AbortController();
    abortRef.current = ac;

    try {
      const conversationId = await streamChat(
        turns,
        (delta) =>
          setItems((prev) => {
            const next = [...prev];
            const last = next[next.length - 1];
            if (last?.type === "message" && last.role === "assistant") {
              next[next.length - 1] = { ...last, content: (last.content + delta).slice(0, MAX_CHAT_MESSAGE_CHARS) };
            }
            return next;
          }),
        {
          conversationId: activeId,
          // Proposals arrive during the tool round, before the final answer
          // streams — insert the card just above the trailing placeholder so
          // the model's explanation lands underneath it.
          onProposal: (proposal) =>
            setItems((prev) => {
              const next = [...prev];
              const id = proposal.proposalId || crypto.randomUUID();
              next.splice(next.length - 1, 0, { id, type: "proposal", proposal: { ...proposal, proposalId: id }, status: "pending" });
              return boundTranscript(next);
            }),
          onInvestigations: (investigations) =>
            setItems((prev) => boundTranscript([
              ...prev,
              {
                id: crypto.randomUUID(),
                type: "investigations",
                investigations: investigations.slice(0, 3),
              },
            ])),
          signal: ac.signal,
          investigationId,
        }
      );
      if (serverHistory && conversationId) {
        setActiveId(conversationId);
        refreshConversations();
      }
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Something went wrong.";
      setItems((prev) => {
        const next = [...prev];
        const last = next[next.length - 1];
        if (last?.type === "message" && last.role === "assistant") {
          next[next.length - 1] = { ...last, content: `${last.content}${last.content ? "\n\n" : ""}⚠️ ${msg}`.slice(0, MAX_CHAT_MESSAGE_CHARS) };
        }
        return next;
      });
    } finally {
      setBusy(false);
      abortRef.current = null;
    }
  }

  return (
    <div className="mx-auto flex h-full max-w-6xl gap-4">
      {/* Conversation rail — only with server-side history */}
      {serverHistory && !investigationId && (
        <aside className="hidden w-64 shrink-0 flex-col overflow-hidden rounded-xl border border-border bg-card md:flex">
          <div className="border-b border-border p-3">
            <div className="flex items-center justify-between gap-2">
              <div><p className="flex items-center gap-1.5 text-[11.5px] font-semibold"><History className="h-3.5 w-3.5 text-primary" />Conversation history</p><p className="mt-0.5 text-[9.5px] text-muted-foreground">Private to your dashboard identity</p></div>
              <button type="button" onClick={startNewChat} disabled={busy} title="New chat" aria-label="New chat" className="grid h-7 w-7 shrink-0 place-items-center rounded-lg border border-border text-muted-foreground transition-colors hover:bg-surface-2 hover:text-foreground disabled:opacity-40"><Plus className="h-3.5 w-3.5" /></button>
            </div>
            <label className="relative mt-3 block"><Search className="pointer-events-none absolute left-2.5 top-1/2 h-3 w-3 -translate-y-1/2 text-muted-foreground" /><input value={historyQuery} onChange={(event) => setHistoryQuery(event.target.value)} placeholder="Search conversations" className="h-8 w-full rounded-lg border border-border bg-surface-1 pl-7 pr-7 text-[10.5px] outline-none focus:border-primary/40" />{historyQuery ? <button type="button" onClick={() => setHistoryQuery("")} aria-label="Clear conversation search" className="absolute right-2.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"><X className="h-3 w-3" /></button> : null}</label>
          </div>
          <div className="flex-1 space-y-1 overflow-y-auto p-2 scrollbar-thin">
            {visibleConversations.length === 0 && (
              <div className="px-3 py-8 text-center"><MessageSquare className="mx-auto h-4 w-4 text-muted-foreground/35" /><p className="mt-2 text-[10.5px] text-muted-foreground/70">{conversations.length ? "No matching conversations." : "Your conversations will appear here."}</p></div>
            )}
            {visibleConversations.map((c) => (
              <div
                key={c.id}
                className={cn(
                  "group flex items-center gap-1.5 rounded-lg border px-2 py-2 transition-colors",
                  c.id === activeId ? "border-primary/20 bg-primary/[0.07]" : "border-transparent hover:border-border hover:bg-surface-2/60"
                )}
              >
                <button
                  type="button"
                  onClick={() => void openConversation(c.id)}
                  className="flex min-w-0 flex-1 items-center gap-1.5 text-left"
                  title={c.title}
                >
                  <MessageSquare
                    className={cn(
                      "h-3 w-3 shrink-0",
                      c.id === activeId ? "text-primary" : "text-muted-foreground/50"
                    )}
                  />
                  <span className="min-w-0"><span className={cn("block truncate text-[11px]", c.id === activeId ? "font-medium text-foreground" : "text-muted-foreground")}>{c.title || "Untitled"}</span><span className="mt-0.5 block text-[9px] text-muted-foreground/55">{new Date(c.updatedAt).toLocaleDateString()}</span></span>
                </button>
                <button
                  type="button"
                  onClick={() => { setDeleteError(null); setPendingDelete(c); }}
                  disabled={busy || loadingThread || deleting}
                  title="Delete conversation"
                  aria-label={`Delete conversation ${c.title || "Untitled"}`}
                  className="shrink-0 text-muted-foreground/40 opacity-0 hover:text-status-error group-hover:opacity-100 focus:opacity-100 disabled:opacity-40"
                >
                  <Trash2 className="h-3 w-3" />
                </button>
              </div>
            ))}
          </div>
        </aside>
      )}

      <ConfirmDialog
        open={!!pendingDelete}
        onOpenChange={(open) => { if (!open) setPendingDelete(null); }}
        title="Delete conversation?"
        description="Permanently deletes this conversation and its messages. This cannot be undone."
        confirmLabel="Delete conversation"
        confirmVariant="destructive"
        isPending={deleting}
        confirmDisabled={busy || loadingThread}
        onConfirm={removeConversation}
      >
        <p className="break-words rounded-lg border border-border bg-card p-3 text-sm">{pendingDelete?.title || "Untitled"}</p>
        {deleteError && <p role="alert" className="text-xs text-status-error">{deleteError}</p>}
      </ConfirmDialog>

      {/* Chat column */}
      <div className="flex min-w-0 flex-1 flex-col overflow-hidden rounded-xl border border-border bg-card">
        <div className="flex min-h-14 flex-wrap items-center justify-between gap-2 border-b border-border px-3 py-2.5 sm:px-4">
          <div className="min-w-0"><p className="truncate text-[12px] font-semibold">{investigation ? `Investigation · ${investigation.functionName}` : activeConversation?.title || (items.length ? "Current conversation" : "New conversation")}</p><p className="mt-0.5 flex items-center gap-1 text-[9.5px] text-muted-foreground"><ShieldCheck className="h-3 w-3" />{investigation ? "Scoped to one saved investigation" : serverHistory ? "Saved privately in your operational store" : "Saved only in this browser"}</p></div>
          <div className="flex items-center gap-2">
            {investigationDraft && items.length === 0 ? <span className="rounded-full border border-primary/20 bg-primary/10 px-2 py-1 text-[9.5px] font-medium text-primary">Investigation prepared</span> : null}
            <button type="button" onClick={startNewChat} disabled={busy || loadingThread} className="inline-flex h-8 items-center gap-1.5 rounded-lg border border-border px-2.5 text-[10.5px] text-muted-foreground transition-colors hover:bg-surface-2 hover:text-foreground disabled:opacity-40"><Plus className="h-3 w-3" />New chat</button>
          </div>
        </div>

        {serverHistory && !investigationId && conversations.length > 0 ? <select value={activeId ?? ""} onChange={(event) => event.target.value ? void openConversation(event.target.value) : startNewChat()} className="mx-3 mt-3 h-9 rounded-lg border border-border bg-surface-1 px-3 text-[11px] outline-none md:hidden"><option value="">New conversation</option>{conversations.map((conversation) => <option key={conversation.id} value={conversation.id}>{conversation.title || "Untitled"}</option>)}</select> : null}

        {/* Messages */}
        <div
          ref={scrollRef}
          className="flex-1 space-y-5 overflow-y-auto bg-surface-0/35 p-3 scrollbar-thin sm:p-5"
        >
          {loadingThread && (
            <div className="flex items-center justify-center py-12 text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />
            </div>
          )}

          {threadError && <p role="alert" className="text-sm text-status-error">{threadError}</p>}

          {investigationLoading ? (
            <div className="flex items-center justify-center gap-2 rounded-xl border border-border bg-card p-5 text-[11px] text-muted-foreground"><Loader2 className="h-3.5 w-3.5 animate-spin" />Loading saved investigation…</div>
          ) : investigationError ? (
            <p role="alert" className="rounded-xl border border-status-error/20 bg-status-error/5 p-4 text-[11px] text-status-error">{investigationError}</p>
          ) : investigation ? (
            <section className="rounded-2xl border border-primary/20 bg-gradient-to-br from-primary/[0.08] to-card p-4 shadow-sm">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0"><p className="flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-[0.12em] text-primary"><SearchCheck className="h-3.5 w-3.5" />Saved investigation context</p><h2 className="mt-1 truncate text-[13px] font-semibold">{investigation.functionName}</h2><p className="mt-0.5 font-mono text-[9px] text-muted-foreground">{investigation.executionId}</p></div>
                <div className="flex items-center gap-1.5"><span className="rounded-full border border-border bg-background/50 px-2 py-1 text-[9.5px]">{investigation.classification}</span><span className="rounded-full border border-status-error/20 bg-status-error/10 px-2 py-1 text-[9.5px] text-status-error">{investigation.executionStatus}</span></div>
              </div>
              <div className="mt-3 border-t border-border/70 pt-3 text-[12px] leading-relaxed"><ChatMarkdown content={investigation.summary || "This investigation has no saved diagnosis."} /></div>
              <p className="mt-3 text-[9.5px] text-muted-foreground">Your next message stays scoped to this exact execution. Live evidence is checked again when needed.</p>
            </section>
          ) : null}

          {!loadingThread && transcriptTrimmed && (
            <p className="text-center text-[11px] text-muted-foreground">
              Showing the latest {MAX_CHAT_MESSAGES} messages.
              {serverHistory ? " Earlier messages remain in saved conversation history." : ""}
            </p>
          )}

          {!loadingThread && !investigationLoading && items.length === 0 && !investigation && (
            <div className="mx-auto mt-10 flex max-w-xl flex-col items-center gap-4 rounded-2xl border border-primary/10 bg-gradient-to-b from-primary/[0.06] to-transparent px-5 py-8 text-center">
              <div className="grid h-11 w-11 place-items-center rounded-2xl bg-primary/10 text-primary"><Sparkles className="h-5 w-5" /></div>
              <div>
                <p className="text-sm font-semibold">Ask TickerQ about this runtime</p>
                <p className="mt-1 max-w-md text-[11.5px] leading-relaxed text-muted-foreground">
                  Investigate failures, inspect scheduler health, and prepare reviewable actions from current operational evidence.
                </p>
              </div>
              <div className="mt-2 grid w-full max-w-md grid-cols-1 gap-2 sm:grid-cols-2">
                {SUGGESTIONS.map((s) => (
                  <button
                    key={s}
                    type="button"
                    onClick={() => void send(s)}
                    className="rounded-lg border border-border px-3 py-2 text-[12px] text-foreground/80 transition-colors hover:bg-surface-2"
                  >
                    {s}
                  </button>
                ))}
              </div>
            </div>
          )}

          {!loadingThread &&
            items.map((item, i) =>
              item.type === "proposal" ? (
                <div key={item.id} className="flex justify-start">
                  <ProposalCard
                    proposal={item.proposal}
                    status={item.status}
                    result={item.result}
                    streamBusy={busy}
                    onStatusChange={(status, result) => setProposalStatus(item.id, status, result)}
                  />
                </div>
              ) : item.type === "investigations" ? (
                <section key={item.id} className="ml-9 max-w-[calc(100%-2.25rem)] rounded-2xl border border-primary/15 bg-primary/[0.025] p-3.5">
                  <div className="mb-2.5">
                    <p className="flex items-center gap-1.5 text-[11px] font-semibold text-foreground"><SearchCheck className="h-3.5 w-3.5 text-primary" />Suggested investigations</p>
                    <p className="mt-0.5 text-[10px] text-muted-foreground">Open a saved diagnosis to continue with that exact execution.</p>
                  </div>
                  <div className="grid gap-2 lg:grid-cols-2">
                    {item.investigations.map((value) => (
                      <InvestigationCard
                        key={value.id}
                        value={value}
                        onContinue={() => onContinueInvestigation(value.id)}
                      />
                    ))}
                  </div>
                </section>
              ) : (
                <div key={item.id} className={cn("flex items-end gap-2", item.role === "user" ? "justify-end" : "justify-start")}>
                  {item.role === "assistant" ? <span className="mb-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-lg border border-primary/15 bg-primary/10 text-primary"><Bot className="h-3.5 w-3.5" /></span> : null}
                  <div
                    className={cn(
                      "max-w-[84%] rounded-2xl px-4 py-2.5 text-[12.5px] leading-relaxed shadow-sm",
                      item.role === "user"
                        ? "whitespace-pre-wrap rounded-br-md bg-primary text-primary-foreground"
                        : "rounded-bl-md border border-border bg-card text-foreground"
                    )}
                  >
                    {item.content ? (
                      item.role === "assistant" ? (
                        <ChatMarkdown content={item.content} />
                      ) : (
                        item.content
                      )
                    ) : busy && i === items.length - 1 ? (
                      <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
                    ) : (
                      ""
                    )}
                  </div>
                </div>
              )
            )}
        </div>

        {/* Composer */}
        <form
          className="border-t border-border bg-card p-3 sm:p-4"
          onSubmit={(e) => {
            e.preventDefault();
            void send(input);
          }}
        >
          <div className="rounded-xl border border-border bg-surface-1 p-2 focus-within:border-primary/45 focus-within:ring-1 focus-within:ring-primary/10">
            <textarea disabled={loadingThread || investigationLoading || Boolean(investigationError)} value={input} onChange={(e) => setInput(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); void send(input); } }} rows={1} placeholder={investigation ? "Ask a follow-up about this investigation…" : "Ask about failures, schedules, nodes, logs, or monitoring…"} className="max-h-32 min-h-[48px] w-full resize-none bg-transparent px-2 py-1.5 text-[12.5px] outline-none" />
            <div className="flex items-center justify-between gap-3 px-1"><span className="text-[9.5px] text-muted-foreground">Enter to send · Shift + Enter for a new line</span><button type="submit" aria-label="Send message" disabled={busy || loadingThread || investigationLoading || Boolean(investigationError) || !input.trim()} className="inline-flex h-8 items-center gap-1.5 rounded-lg bg-primary px-3 text-[10.5px] font-medium text-primary-foreground disabled:opacity-40">{busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <SendHorizonal className="h-3.5 w-3.5" />}{busy ? "Thinking" : "Send"}</button></div>
          </div>
        </form>
      </div>
    </div>
  );
}
