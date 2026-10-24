import { useCallback, useEffect, useRef, useState } from "react";
import {
  Activity,
  AlertTriangle,
  Bot,
  Check,
  Cloud,
  Copy,
  History,
  Laptop,
  LayoutDashboard,
  ListChecks,
  Loader2,
  MessageSquare,
  Plug,
  Plus,
  ScrollText,
  Search,
  SearchCheck,
  SendHorizonal,
  ShieldCheck,
  Sparkles,
  Square,
  Trash2,
  X,
} from "lucide-react";
import { useSearchParams } from "react-router-dom";
import { cn } from "@/lib/utils";
import { toast } from "sonner";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { ChatMarkdown } from "@/components/assistant/ChatMarkdown";
import { ProposalCard, type ProposalStatus } from "@/components/assistant/ProposalCard";
import { getRuntimeConfig, resolveDashboardUrl } from "@/lib/runtime-config";
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
import { describeAssistantConnection, type AssistantConnection } from "@/lib/assistant-connection";
import { AssistantOverview } from "@/components/assistant/AssistantOverview";
import { AssistantInvestigations } from "@/components/assistant/AssistantInvestigations";
import { AssistantSetup } from "@/components/assistant/AssistantSetup";
import { InvestigationCard } from "@/components/assistant/InvestigationCard";
import type { AssistantProposalApplyResult } from "@/lib/apply-assistant-proposal";

const SUGGESTIONS = [
  { icon: AlertTriangle, title: "Failing jobs", prompt: "Why are my jobs failing?" },
  { icon: ScrollText, title: "Latest failure logs", prompt: "Inspect the logs for the most recent failed execution" },
  { icon: ListChecks, title: "Registered functions", prompt: "Which registered functions can I schedule?" },
  { icon: Activity, title: "Scheduler health", prompt: "Is the scheduler healthy right now?" },
  { icon: Plug, title: "Integrations", prompt: "Show my integrations and monitor status" },
];

export default function AssistantChatPage() {
  const auth = useAuth();
  const runtime = getRuntimeConfig();
  const config = runtime.assistant;
  const connection = describeAssistantConnection(config);
  const dashboardUrl = new URL(resolveDashboardUrl("/"), window.location.origin).href;
  const storageKey = assistantStorageKey(auth.userId, dashboardUrl);
  const [searchParams, setSearchParams] = useSearchParams();
  const executionToInvestigate = searchParams.get("investigate");
  const savedInvestigationId = searchParams.get("investigation");
  const requestedView = searchParams.get("view");
  const view = !connection.connected || executionToInvestigate || savedInvestigationId
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

  return (
    <div className="mx-auto flex h-[calc(100vh-7.5rem)] max-w-7xl flex-col gap-3">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <div className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-primary/25 to-primary/5 text-primary ring-1 ring-primary/20"><Sparkles className="h-5 w-5" /></div>
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-lg font-semibold tracking-tight">TickerQ Assistant</h1>
              <ConnectionPill connection={connection} />
            </div>
            <p className="mt-0.5 text-[11.5px] text-muted-foreground">Investigate runs, inspect live scheduler data, and prepare changes for approval.</p>
          </div>
        </div>
        <nav className="flex rounded-xl border border-border bg-surface-1 p-1" aria-label="AI workspace">
          <ViewTab active={view === "overview"} icon={LayoutDashboard} label="Overview" disabled={!connection.connected} onClick={() => { setDraftPrompt(""); setSearchParams({ view: "overview" }); }} />
          <ViewTab active={view === "chat"} icon={MessageSquare} label="Chat" onClick={() => { setDraftPrompt(""); setSearchParams({ view: "chat" }); }} />
          <ViewTab active={view === "investigations"} icon={SearchCheck} label="Investigations" disabled={!connection.connected || !config.investigations} onClick={() => setSearchParams({ view: "investigations" })} />
        </nav>
      </header>

      <ModelStrip connection={connection} />

      <main className="min-h-0 flex-1 overflow-y-auto scrollbar-thin">
        {!connection.connected ? (
          <NotConnectedChat connection={connection} />
        ) : view === "overview" ? (
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
            connection={connection}
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

function ConnectionPill({ connection }: { connection: AssistantConnection }) {
  const tone = connection.connected ? "connected" : connection.state === "failed" ? "failed" : "offline";
  return (
    <span className={cn(
      "inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-[10px] font-medium",
      tone === "connected" && "border-status-healthy/30 bg-status-healthy/10 text-status-healthy",
      tone === "failed" && "border-status-error/30 bg-status-error/10 text-status-error",
      tone === "offline" && "border-status-warning/30 bg-status-warning/10 text-status-warning",
    )}>
      <span className={cn(
        "h-1.5 w-1.5 rounded-full",
        tone === "connected" && "bg-status-healthy",
        tone === "failed" && "bg-status-error",
        tone === "offline" && "bg-status-warning",
      )} />
      {connection.status}
    </span>
  );
}

/** One line that says which model answers and where messages go. */
function ModelStrip({ connection }: { connection: AssistantConnection }) {
  const runtime = getRuntimeConfig();
  const config = runtime.assistant;
  const PlacementIcon = connection.state === "local" ? Laptop : connection.state === "remote" ? Cloud : connection.connected ? ShieldCheck : Plug;

  return (
    <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 rounded-xl border border-border bg-card px-3 py-2">
      <div className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 text-[11px]">
        <span className={cn(
          "inline-flex items-center gap-1.5 font-medium",
          connection.state === "local" ? "text-status-healthy" : connection.connected ? "text-foreground" : "text-status-warning",
          connection.state === "failed" && "text-status-error",
        )}>
          <PlacementIcon className="h-3.5 w-3.5" />
          {connection.connected ? connection.placement || "Model" : connection.state === "failed" ? "Model unavailable" : "No model connected"}
        </span>
        {connection.connected && connection.provider ? <StripValue label="Provider" value={connection.provider} /> : null}
        {connection.connected && connection.model ? <StripValue label="Model" value={connection.model} mono /> : null}
        {connection.connected && connection.endpoint ? <StripValue label="Endpoint" value={connection.endpoint} mono /> : null}
        {!connection.connected ? <span className="text-muted-foreground">Chat, overview, and investigations are unavailable until the host connects a model.</span> : null}
      </div>
      {connection.connected ? (
        <div className="flex flex-wrap gap-1.5 text-[10px] text-muted-foreground">
          <Capability value="Live scheduler context" />
          {config.history ? <Capability value="Private history" /> : <Capability value="Browser-only history" />}
          {config.automaticInvestigations ? <Capability value="Automatic investigations" /> : config.investigations ? <Capability value="Investigations" /> : null}
          {config.sourceContext ? <Capability value="Source context" /> : null}
          <Capability value={runtime.readOnly ? "Read-only" : "Actions need approval"} />
        </div>
      ) : null}
    </div>
  );
}

function StripValue({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <span className="inline-flex min-w-0 items-center gap-1 text-muted-foreground">
      <span className="text-muted-foreground/70">{label}</span>
      <span className={cn("truncate text-foreground/85", mono && "font-mono text-[10.5px]")} title={value}>{value}</span>
    </span>
  );
}

function Capability({ value }: { value: string }) {
  return <span className="rounded-md border border-border/70 bg-surface-1/70 px-1.5 py-0.5">{value}</span>;
}

function ViewTab({ active, icon: Icon, label, disabled = false, onClick }: { active: boolean; icon: typeof Bot; label: string; disabled?: boolean; onClick: () => void }) {
  return <button type="button" disabled={disabled} onClick={onClick} className={cn("inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-[11px] font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-35", active ? "bg-card text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground")}><Icon className="h-3.5 w-3.5" />{label}</button>;
}

/** The chat frame when no model can answer: setup guidance and a disabled composer. */
function NotConnectedChat({ connection }: { connection: AssistantConnection }) {
  return (
    <div className="flex h-full min-h-0 flex-col overflow-hidden rounded-xl border border-border bg-card">
      <div className="flex min-h-14 items-center justify-between gap-2 border-b border-border px-4 py-2.5">
        <div className="min-w-0">
          <p className="truncate text-[12.5px] font-semibold">New conversation</p>
          <p className="mt-0.5 text-[10px] text-muted-foreground">The assistant is offline until a model is connected.</p>
        </div>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto bg-surface-0/35 px-4 scrollbar-thin">
        <AssistantSetup connection={connection} />
      </div>
      <Composer
        connection={connection}
        value=""
        onChange={() => {}}
        onSubmit={() => {}}
        disabled
        busy={false}
        placeholder="Connect a model to start chatting"
      />
    </div>
  );
}

function Composer({ connection, value, onChange, onSubmit, onStop, disabled, busy, placeholder }: {
  connection: AssistantConnection;
  value: string;
  onChange: (value: string) => void;
  onSubmit: () => void;
  onStop?: () => void;
  disabled: boolean;
  busy: boolean;
  placeholder: string;
}) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const PrivacyIcon = connection.state === "local" ? Laptop : connection.state === "remote" ? Cloud : connection.connected ? ShieldCheck : Plug;

  // Grow with the text up to a limit, then scroll.
  useEffect(() => {
    const element = textareaRef.current;
    if (!element) return;
    element.style.height = "auto";
    element.style.height = `${Math.min(element.scrollHeight, 192)}px`;
  }, [value]);

  return (
    <form
      className="border-t border-border bg-card px-3 py-3 sm:px-4"
      onSubmit={(event) => { event.preventDefault(); onSubmit(); }}
    >
      <div className="mx-auto w-full max-w-3xl">
        <div className={cn(
          "flex items-end gap-2 rounded-2xl border border-border bg-surface-1 p-2 transition-colors focus-within:border-primary/45 focus-within:ring-2 focus-within:ring-primary/10",
          disabled && "opacity-60",
        )}>
          <textarea
            ref={textareaRef}
            disabled={disabled}
            value={value}
            onChange={(event) => onChange(event.target.value)}
            onKeyDown={(event) => { if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); onSubmit(); } }}
            rows={1}
            placeholder={placeholder}
            aria-label="Message"
            className="max-h-48 min-h-[40px] flex-1 resize-none bg-transparent px-2 py-2 text-[13px] leading-relaxed outline-none disabled:cursor-not-allowed"
          />
          {busy && onStop ? (
            <button type="button" onClick={onStop} aria-label="Stop generating" className="inline-flex h-9 shrink-0 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-[11px] font-medium text-foreground transition-colors hover:bg-surface-2">
              <Square className="h-3 w-3 fill-current" />Stop
            </button>
          ) : (
            <button type="submit" aria-label="Send message" disabled={disabled || !value.trim()} className="inline-flex h-9 shrink-0 items-center gap-1.5 rounded-xl bg-primary px-3.5 text-[11px] font-medium text-primary-foreground transition-opacity disabled:opacity-40">
              <SendHorizonal className="h-3.5 w-3.5" />Send
            </button>
          )}
        </div>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-x-3 gap-y-1 px-1 text-[10px] text-muted-foreground">
          <span className={cn("inline-flex items-center gap-1.5", connection.state === "local" && "text-status-healthy")}>
            <PrivacyIcon className="h-3 w-3 shrink-0" />{connection.privacyNote}
          </span>
          {connection.connected ? <span className="hidden sm:inline">Enter to send · Shift + Enter for a new line</span> : null}
        </div>
      </div>
    </form>
  );
}

function AssistantChat({ connection, storageKey, initialPrompt, investigationId, onClearInvestigation, onContinueInvestigation }: {
  connection: AssistantConnection;
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
  const composerDisabled = loadingThread || investigationLoading || Boolean(investigationError);

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
      // Stopping keeps whatever already streamed and marks the answer as stopped.
      const stopped = ac.signal.aborted;
      const msg = err instanceof Error ? err.message : "Something went wrong.";
      setItems((prev) => {
        const next = [...prev];
        const last = next[next.length - 1];
        if (last?.type === "message" && last.role === "assistant") {
          const suffix = stopped ? "_Stopped._" : `⚠️ ${msg}`;
          next[next.length - 1] = { ...last, content: `${last.content}${last.content ? "\n\n" : ""}${suffix}`.slice(0, MAX_CHAT_MESSAGE_CHARS) };
        }
        return next;
      });
    } finally {
      setBusy(false);
      abortRef.current = null;
    }
  }

  return (
    <div className="mx-auto flex h-full max-w-7xl gap-3">
      {/* Conversation rail — only with server-side history */}
      {serverHistory && !investigationId && (
        <aside className="hidden w-64 shrink-0 flex-col overflow-hidden rounded-xl border border-border bg-card md:flex">
          <div className="space-y-3 border-b border-border p-3">
            <button type="button" onClick={startNewChat} disabled={busy} className="inline-flex h-9 w-full items-center justify-center gap-1.5 rounded-lg bg-primary text-[11.5px] font-medium text-primary-foreground transition-opacity hover:opacity-90 disabled:opacity-40"><Plus className="h-3.5 w-3.5" />New chat</button>
            <label className="relative block"><Search className="pointer-events-none absolute left-2.5 top-1/2 h-3 w-3 -translate-y-1/2 text-muted-foreground" /><input value={historyQuery} onChange={(event) => setHistoryQuery(event.target.value)} placeholder="Search conversations" className="h-8 w-full rounded-lg border border-border bg-surface-1 pl-7 pr-7 text-[11px] outline-none focus:border-primary/40" />{historyQuery ? <button type="button" onClick={() => setHistoryQuery("")} aria-label="Clear conversation search" className="absolute right-2.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"><X className="h-3 w-3" /></button> : null}</label>
          </div>
          <p className="flex items-center gap-1.5 px-4 pb-1 pt-3 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground"><History className="h-3 w-3" />History</p>
          <div className="flex-1 space-y-0.5 overflow-y-auto p-2 pt-1 scrollbar-thin">
            {visibleConversations.length === 0 && (
              <div className="px-3 py-8 text-center"><MessageSquare className="mx-auto h-4 w-4 text-muted-foreground/35" /><p className="mt-2 text-[10.5px] text-muted-foreground/70">{conversations.length ? "No matching conversations." : "Your conversations will appear here."}</p></div>
            )}
            {visibleConversations.map((c) => (
              <div
                key={c.id}
                className={cn(
                  "group flex items-center gap-1.5 rounded-lg px-2.5 py-2 transition-colors",
                  c.id === activeId ? "bg-primary/[0.09]" : "hover:bg-surface-2/60"
                )}
              >
                <button
                  type="button"
                  onClick={() => void openConversation(c.id)}
                  className="min-w-0 flex-1 text-left"
                  title={c.title}
                >
                  <span className={cn("block truncate text-[11.5px]", c.id === activeId ? "font-medium text-foreground" : "text-foreground/80")}>{c.title || "Untitled"}</span>
                  <span className="mt-0.5 block text-[9.5px] text-muted-foreground/70">{new Date(c.updatedAt).toLocaleDateString()}</span>
                </button>
                <button
                  type="button"
                  onClick={() => { setDeleteError(null); setPendingDelete(c); }}
                  disabled={busy || loadingThread || deleting}
                  title="Delete conversation"
                  aria-label={`Delete conversation ${c.title || "Untitled"}`}
                  className="shrink-0 rounded p-1 text-muted-foreground/50 opacity-0 hover:text-status-error group-hover:opacity-100 focus:opacity-100 disabled:opacity-40"
                >
                  <Trash2 className="h-3 w-3" />
                </button>
              </div>
            ))}
          </div>
          <p className="border-t border-border px-4 py-2.5 text-[9.5px] text-muted-foreground">Private to your dashboard identity.</p>
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
          <div className="min-w-0"><p className="truncate text-[12.5px] font-semibold">{investigation ? `Investigation · ${investigation.functionName}` : activeConversation?.title || (items.length ? "Current conversation" : "New conversation")}</p><p className="mt-0.5 flex items-center gap-1 text-[10px] text-muted-foreground"><ShieldCheck className="h-3 w-3" />{investigation ? "Scoped to one saved investigation" : serverHistory ? "Saved privately in your operational store" : "Saved only in this browser"}</p></div>
          <div className="flex items-center gap-2">
            {investigationDraft && items.length === 0 ? <span className="rounded-full border border-primary/20 bg-primary/10 px-2 py-1 text-[9.5px] font-medium text-primary">Investigation prepared</span> : null}
            <button type="button" onClick={startNewChat} disabled={busy || loadingThread} className={cn("h-8 items-center gap-1.5 rounded-lg border border-border px-2.5 text-[10.5px] text-muted-foreground transition-colors hover:bg-surface-2 hover:text-foreground disabled:opacity-40", serverHistory && !investigationId ? "inline-flex md:hidden" : "inline-flex")}><Plus className="h-3 w-3" />New chat</button>
          </div>
        </div>

        {serverHistory && !investigationId && conversations.length > 0 ? <select value={activeId ?? ""} onChange={(event) => event.target.value ? void openConversation(event.target.value) : startNewChat()} className="mx-3 mt-3 h-9 rounded-lg border border-border bg-surface-1 px-3 text-[11px] outline-none md:hidden"><option value="">New conversation</option>{conversations.map((conversation) => <option key={conversation.id} value={conversation.id}>{conversation.title || "Untitled"}</option>)}</select> : null}

        {/* Messages */}
        <div ref={scrollRef} className="flex-1 overflow-y-auto bg-surface-0/35 px-3 py-5 scrollbar-thin sm:px-5">
          <div className="mx-auto w-full max-w-3xl space-y-6">
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
              <div className="flex flex-col items-center gap-6 pt-6 text-center sm:pt-10">
                <div className="grid h-14 w-14 place-items-center rounded-2xl bg-gradient-to-br from-primary/25 to-primary/5 text-primary ring-1 ring-primary/20"><Sparkles className="h-6 w-6" /></div>
                <div>
                  <p className="text-base font-semibold tracking-tight">What do you want to know about this scheduler?</p>
                  <p className="mx-auto mt-1.5 max-w-lg text-[12px] leading-relaxed text-muted-foreground">
                    Ask about failures, logs, functions, nodes, or integrations. Answers use live scheduler data, and changes always need your approval.
                  </p>
                </div>
                <div className="grid w-full grid-cols-1 gap-2.5 text-left sm:grid-cols-2">
                  {SUGGESTIONS.map((suggestion, index) => {
                    const Icon = suggestion.icon;
                    return (
                      <button
                        key={suggestion.prompt}
                        type="button"
                        disabled={composerDisabled}
                        onClick={() => void send(suggestion.prompt)}
                        className={cn(
                          "group flex items-start gap-3 rounded-xl border border-border bg-card p-3 text-left transition-colors hover:border-primary/40 hover:bg-primary/[0.03] disabled:opacity-50",
                          index === SUGGESTIONS.length - 1 && SUGGESTIONS.length % 2 === 1 && "sm:col-span-2",
                        )}
                      >
                        <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-surface-2 text-muted-foreground transition-colors group-hover:bg-primary/10 group-hover:text-primary"><Icon className="h-4 w-4" /></span>
                        <span className="min-w-0">
                          <span className="block text-[12px] font-medium text-foreground">{suggestion.title}</span>
                          <span className="mt-0.5 block text-[11px] text-muted-foreground">{suggestion.prompt}</span>
                        </span>
                      </button>
                    );
                  })}
                </div>
              </div>
            )}

            {!loadingThread &&
              items.map((item, i) =>
                item.type === "proposal" ? (
                  <div key={item.id} className="flex justify-start pl-10">
                    <ProposalCard
                      proposal={item.proposal}
                      status={item.status}
                      result={item.result}
                      streamBusy={busy}
                      onStatusChange={(status, result) => setProposalStatus(item.id, status, result)}
                    />
                  </div>
                ) : item.type === "investigations" ? (
                  <section key={item.id} className="ml-10 rounded-2xl border border-primary/15 bg-primary/[0.025] p-3.5">
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
                ) : item.role === "user" ? (
                  <div key={item.id} className="flex justify-end">
                    <div className="max-w-[85%] whitespace-pre-wrap rounded-2xl rounded-br-md bg-primary px-4 py-2.5 text-[13px] leading-relaxed text-primary-foreground shadow-sm">
                      {item.content}
                    </div>
                  </div>
                ) : (
                  <AssistantMessage
                    key={item.id}
                    content={item.content}
                    streaming={busy && i === items.length - 1}
                    model={connection.model}
                  />
                )
              )}
          </div>
        </div>

        <Composer
          connection={connection}
          value={input}
          onChange={setInput}
          onSubmit={() => void send(input)}
          onStop={() => abortRef.current?.abort()}
          disabled={composerDisabled}
          busy={busy}
          placeholder={investigation ? "Ask a follow-up about this investigation…" : "Ask about failures, schedules, nodes, logs, or monitoring…"}
        />
      </div>
    </div>
  );
}

function AssistantMessage({ content, streaming, model }: { content: string; streaming: boolean; model: string }) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(content);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      toast.error("Could not copy the answer");
    }
  }

  return (
    <div className="group flex items-start gap-2.5">
      <span className="mt-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-gradient-to-br from-primary/25 to-primary/5 text-primary ring-1 ring-primary/20"><Bot className="h-3.5 w-3.5" /></span>
      <div className="min-w-0 flex-1">
        <p className="mb-1 flex items-center gap-1.5 text-[10.5px] font-medium text-muted-foreground">
          Assistant{model ? <span className="font-mono text-[10px] text-muted-foreground/60">{model}</span> : null}
        </p>
        <div className="rounded-2xl rounded-tl-md border border-border bg-card px-4 py-3 text-[13px] leading-relaxed text-foreground shadow-sm">
          {content ? (
            <ChatMarkdown content={content} />
          ) : streaming ? (
            <span className="inline-flex items-center gap-2 text-[12px] text-muted-foreground">
              <span className="flex gap-1">
                <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-primary/70 [animation-delay:-0.3s]" />
                <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-primary/70 [animation-delay:-0.15s]" />
                <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-primary/70" />
              </span>
              Thinking…
            </span>
          ) : null}
        </div>
        {content && !streaming ? (
          <div className="mt-1 flex opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
            <button type="button" onClick={() => void copy()} className="inline-flex items-center gap-1 rounded-md px-1.5 py-1 text-[10px] text-muted-foreground hover:bg-surface-2 hover:text-foreground">
              {copied ? <Check className="h-3 w-3 text-status-healthy" /> : <Copy className="h-3 w-3" />}{copied ? "Copied" : "Copy"}
            </button>
          </div>
        ) : null}
      </div>
    </div>
  );
}
