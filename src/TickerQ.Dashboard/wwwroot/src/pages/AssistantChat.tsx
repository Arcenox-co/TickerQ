import { useCallback, useEffect, useRef, useState } from "react";
import { Bot, Loader2, MessageSquare, Plus, SendHorizonal, Sparkles, Trash2 } from "lucide-react";
import { cn } from "@/lib/utils";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { ChatMarkdown } from "@/components/assistant/ChatMarkdown";
import { ProposalCard, type ProposalStatus } from "@/components/assistant/ProposalCard";
import { getRuntimeConfig, isAssistantEnabled, resolveDashboardUrl } from "@/lib/runtime-config";
import {
  deleteConversation,
  getConversationMessages,
  listConversations,
  streamChat,
  type ChatTurn,
  type ConversationSummary,
} from "@/services/assistant-api";

import { useAuth } from "@/lib/auth/auth-context";
import { assistantStorageKey, loadAssistantTranscript, storeAssistantTranscript, clearAssistantTranscript } from "@/lib/assistant-storage";
import { LatestRequest } from "@/lib/latest-request";
import { boundTranscript, chatMessage, MAX_CHAT_MESSAGES, MAX_CHAT_MESSAGE_CHARS, updateProposalStatus, type ChatItem } from "@/lib/assistant-transcript";

const SUGGESTIONS = [
  "Why are my jobs failing?",
  "Inspect the logs for the most recent failed execution",
  "Which registered functions can I schedule?",
  "Is the scheduler healthy right now?",
  "Show my integrations and monitor status",
];

export default function AssistantChatPage() {
  const auth = useAuth();
  const dashboardUrl = new URL(resolveDashboardUrl("/"), window.location.origin).href;
  const storageKey = assistantStorageKey(auth.userId, dashboardUrl);
  // A changed account remounts all transcript and in-flight state before rendering.
  return <AssistantChat key={`${auth.status}:${storageKey ?? "memory"}`} storageKey={storageKey} />;
}

function AssistantChat({ storageKey }: { storageKey: string | null }) {
  const { model, history } = getRuntimeConfig().assistant;
  const serverHistory = history && storageKey !== null;

  const [input, setInput] = useState("");
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
  const scrollRef = useRef<HTMLDivElement>(null);
  const abortRef = useRef<AbortController | null>(null);
  const threadRequest = useRef(new LatestRequest());
  const listRequest = useRef(new LatestRequest());
  const [threadError, setThreadError] = useState<string | null>(null);

  const refreshConversations = useCallback(() => {
    if (!serverHistory) return;
    const request = listRequest.current.begin();
    listConversations(request.signal)
      .then((rows) => { if (request.isCurrent()) setConversations(rows); })
      .catch(() => { if (request.isCurrent()) setConversations([]); });
  }, [serverHistory]);

  useEffect(() => refreshConversations(), [refreshConversations]);

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

  if (!isAssistantEnabled()) {
    return (
      <div className="flex flex-col items-center justify-center py-24 gap-2 text-center">
        <Sparkles className="h-6 w-6 text-muted-foreground/40" />
        <p className="text-sm font-medium">AI assistant is not configured</p>
        <p className="text-[12px] text-muted-foreground max-w-md">
          The operator hasn't provided an AI provider key. Configure it server-side with
          <code className="mx-1 rounded bg-surface-2 px-1 py-0.5 text-[11px]">AddAssistant(a =&gt; a.UseChatClient(…))</code>
          and this page comes alive.
        </p>
      </div>
    );
  }

  function startNewChat() {
    abortRef.current?.abort();
    threadRequest.current.cancel();
    setLoadingThread(false);
    setThreadError(null);
    setItems([]);
    setTranscriptTrimmed(false);
    setActiveId(null);
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
      setItems(boundTranscript(history.slice(-MAX_CHAT_MESSAGES).map((m) => chatMessage(m.role, m.content))));
      setTranscriptTrimmed(history.length > MAX_CHAT_MESSAGES);
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

  function setProposalStatus(id: string, status: ProposalStatus) {
    setItems((prev) => updateProposalStatus(prev, id, status));
  }

  async function send(text: string) {
    const question = text.trim().slice(0, MAX_CHAT_MESSAGE_CHARS);
    if (!question || busy || loadingThread) return;
    if (items.filter((item) => item.type === "message").length + 2 > MAX_CHAT_MESSAGES)
      setTranscriptTrimmed(true);

    const turns: ChatTurn[] = [
      ...items.filter((i): i is Extract<ChatItem, { type: "message" }> => i.type === "message"),
      { type: "message" as const, role: "user" as const, content: question },
    ].map((m) => ({ role: m.role, content: m.content }));

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
              next.splice(next.length - 1, 0, { id: crypto.randomUUID(), type: "proposal", proposal, status: "pending" });
              return boundTranscript(next);
            }),
          signal: ac.signal,
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
    <div className="mx-auto flex h-[calc(100vh-7.5rem)] max-w-5xl gap-4">
      {/* Conversation rail — only with server-side history */}
      {serverHistory && (
        <aside className="hidden w-56 shrink-0 flex-col md:flex">
          <button
            type="button"
            onClick={startNewChat}
            disabled={busy}
            className="mb-2 inline-flex items-center justify-center gap-1.5 rounded-lg border border-border px-3 py-2 text-[12px] font-medium text-foreground transition-colors hover:bg-surface-2 disabled:opacity-40"
          >
            <Plus className="h-3.5 w-3.5" />
            New chat
          </button>
          <div className="flex-1 space-y-0.5 overflow-y-auto scrollbar-thin">
            {conversations.length === 0 && (
              <p className="px-2 py-4 text-center text-[11px] text-muted-foreground/60">
                No conversations yet.
              </p>
            )}
            {conversations.map((c) => (
              <div
                key={c.id}
                className={cn(
                  "group flex items-center gap-1.5 rounded-lg px-2 py-1.5 transition-colors",
                  c.id === activeId ? "bg-primary/8" : "hover:bg-surface-2/60"
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
                  <span
                    className={cn(
                      "truncate text-[11.5px]",
                      c.id === activeId ? "text-foreground font-medium" : "text-muted-foreground"
                    )}
                  >
                    {c.title || "Untitled"}
                  </span>
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
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Header */}
        <div className="flex items-center justify-between pb-4">
          <div className="flex items-center gap-3">
            <div className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-primary/15 text-primary">
              <Bot className="h-5 w-5" />
            </div>
            <div className="leading-tight">
              <h1 className="text-lg font-semibold tracking-tight">Chat AI</h1>
              <p className="text-[11px] text-muted-foreground">
                Jobs, logs, workflows and monitoring
                {model ? ` · ${model}` : ""}
              </p>
            </div>
          </div>
          {!serverHistory && items.length > 0 && (
            <button
              type="button"
              onClick={startNewChat}
              disabled={busy}
              className="inline-flex shrink-0 items-center gap-1.5 rounded-lg border border-border px-2.5 py-1.5 text-[11px] text-muted-foreground transition-colors hover:text-foreground hover:bg-surface-2 disabled:opacity-40"
            >
              <Trash2 className="h-3 w-3" />
              New chat
            </button>
          )}
        </div>

        {/* Messages */}
        <div
          ref={scrollRef}
          className="flex-1 space-y-4 overflow-y-auto scrollbar-thin rounded-xl border border-border bg-surface-0/50 p-3 sm:p-5"
        >
          {loadingThread && (
            <div className="flex items-center justify-center py-12 text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />
            </div>
          )}

          {threadError && <p role="alert" className="text-sm text-status-error">{threadError}</p>}

          {!loadingThread && transcriptTrimmed && (
            <p className="text-center text-[11px] text-muted-foreground">
              Showing the latest {MAX_CHAT_MESSAGES} messages.
              {serverHistory ? " Earlier messages remain in saved conversation history." : ""}
            </p>
          )}

          {!loadingThread && items.length === 0 && (
            <div className="mt-16 flex flex-col items-center gap-4 text-center">
              <Sparkles className="h-7 w-7 text-primary/50" />
              <div>
                <p className="text-sm font-medium">What do you want to know?</p>
                <p className="mt-1 text-[12px] text-muted-foreground">
                  Inspect jobs, logs and monitors. Draft schedules, workflows and changes
                  for you to review and confirm.
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
                    onStatusChange={(status) => setProposalStatus(item.id, status)}
                  />
                </div>
              ) : (
                <div key={item.id} className={cn("flex", item.role === "user" ? "justify-end" : "justify-start")}>
                  <div
                    className={cn(
                      "max-w-[80%] rounded-2xl px-4 py-2.5 text-[13px] leading-relaxed",
                      item.role === "user"
                        ? "whitespace-pre-wrap bg-primary text-primary-foreground"
                        : "bg-surface-2 text-foreground"
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
          className="mt-3 flex items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            void send(input);
          }}
        >
          <textarea
            disabled={loadingThread}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                void send(input);
              }
            }}
            rows={1}
            placeholder="Ask about your tickers, or describe a schedule to create…"
            className="max-h-32 min-h-[44px] flex-1 resize-none rounded-xl border border-border bg-surface-1 px-4 py-3 text-[13px] outline-none focus:border-primary/50"
          />
          <button
            type="submit"
            aria-label="Send message"
            disabled={busy || loadingThread || !input.trim()}
            className="grid h-[44px] w-[44px] shrink-0 place-items-center rounded-xl bg-primary text-primary-foreground disabled:opacity-40"
          >
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <SendHorizonal className="h-4 w-4" />}
          </button>
        </form>
      </div>
    </div>
  );
}
