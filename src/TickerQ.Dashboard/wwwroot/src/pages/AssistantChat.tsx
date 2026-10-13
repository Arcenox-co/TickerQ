import { useCallback, useEffect, useRef, useState } from "react";
import { Bot, Loader2, MessageSquare, Plus, SendHorizonal, Sparkles, Trash2 } from "lucide-react";
import { cn } from "@/lib/utils";
import { ChatMarkdown } from "@/components/assistant/ChatMarkdown";
import { ProposalCard, type ProposalStatus } from "@/components/assistant/ProposalCard";
import { getRuntimeConfig, isAssistantEnabled } from "@/lib/runtime-config";
import {
  deleteConversation,
  getConversationMessages,
  listConversations,
  streamChat,
  type AssistantProposal,
  type ChatTurn,
  type ConversationSummary,
} from "@/services/assistant-api";

// The transcript interleaves plain chat turns with proposal cards (mutations
// the model drafted, awaiting the user's confirmation).
type ChatItem =
  | { type: "message"; role: "user" | "assistant"; content: string }
  | { type: "proposal"; proposal: AssistantProposal; status: ProposalStatus };

const SUGGESTIONS = [
  "Why are my jobs failing?",
  "Show me the recent failures",
  "Schedule CleanupTempFiles every day at 03:00",
  "Is the scheduler healthy right now?",
];

// Fallback persistence when the server has NO history store: the chat
// survives refreshes via localStorage (per browser). With a store registered
// (assistant.history), the server is the source of truth instead. Proposals
// are ephemeral UI state and are not persisted in either mode.
const STORAGE_KEY = "tickerq-assistant-chat";
const MAX_STORED_MESSAGES = 40;

function loadStoredItems(): ChatItem[] {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as { role: "user" | "assistant"; content: string }[];
    return Array.isArray(parsed)
      ? parsed
          .filter((m) => (m.role === "user" || m.role === "assistant") && typeof m.content === "string")
          .map((m) => ({ type: "message" as const, role: m.role, content: m.content }))
      : [];
  } catch {
    return [];
  }
}

function storeItems(items: ChatItem[]) {
  try {
    const messages = items
      .filter((i): i is Extract<ChatItem, { type: "message" }> => i.type === "message" && i.content !== "")
      .map((m) => ({ role: m.role, content: m.content }));
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(messages.slice(-MAX_STORED_MESSAGES)));
  } catch {
    /* storage unavailable — chat still works in-memory */
  }
}

export default function AssistantChatPage() {
  const { model, history: serverHistory } = getRuntimeConfig().assistant;

  const [input, setInput] = useState("");
  const [items, setItems] = useState<ChatItem[]>(() => (serverHistory ? [] : loadStoredItems()));
  const [busy, setBusy] = useState(false);
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [loadingThread, setLoadingThread] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const abortRef = useRef<AbortController | null>(null);

  const refreshConversations = useCallback(() => {
    if (!serverHistory) return;
    listConversations()
      .then(setConversations)
      .catch(() => setConversations([]));
  }, [serverHistory]);

  useEffect(() => refreshConversations(), [refreshConversations]);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
  }, [items]);

  // localStorage persistence only applies in fallback mode.
  useEffect(() => {
    if (!serverHistory && !busy) storeItems(items);
  }, [items, busy, serverHistory]);

  useEffect(() => () => abortRef.current?.abort(), []);

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
    setItems([]);
    setActiveId(null);
    if (!serverHistory) {
      try {
        window.localStorage.removeItem(STORAGE_KEY);
      } catch {
        /* ignore */
      }
    }
  }

  async function openConversation(id: string) {
    if (busy || id === activeId) return;
    setLoadingThread(true);
    setActiveId(id);
    try {
      const history = await getConversationMessages(id);
      setItems(history.map((m) => ({ type: "message" as const, role: m.role, content: m.content })));
    } catch {
      setItems([]);
    } finally {
      setLoadingThread(false);
    }
  }

  async function removeConversation(id: string) {
    try {
      await deleteConversation(id);
    } catch {
      /* best-effort */
    }
    if (id === activeId) startNewChat();
    refreshConversations();
  }

  function setProposalStatus(index: number, status: ProposalStatus) {
    setItems((prev) =>
      prev.map((item, i) => (i === index && item.type === "proposal" ? { ...item, status } : item))
    );
  }

  async function send(text: string) {
    const question = text.trim();
    if (!question || busy) return;

    const turns: ChatTurn[] = [
      ...items.filter((i): i is Extract<ChatItem, { type: "message" }> => i.type === "message"),
      { type: "message" as const, role: "user" as const, content: question },
    ].map((m) => ({ role: m.role, content: m.content }));

    setItems((prev) => [
      ...prev,
      { type: "message", role: "user", content: question },
      { type: "message", role: "assistant", content: "" },
    ]);
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
              next[next.length - 1] = { ...last, content: last.content + delta };
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
              next.splice(next.length - 1, 0, { type: "proposal", proposal, status: "pending" });
              return next;
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
          next[next.length - 1] = { ...last, content: last.content || `⚠️ ${msg}` };
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
                  onClick={() => void removeConversation(c.id)}
                  title="Delete conversation"
                  className="hidden shrink-0 text-muted-foreground/40 hover:text-status-error group-hover:block"
                >
                  <Trash2 className="h-3 w-3" />
                </button>
              </div>
            ))}
          </div>
        </aside>
      )}

      {/* Chat column */}
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Header */}
        <div className="flex items-center justify-between pb-4">
          <div className="flex items-center gap-3">
            <div className="grid h-9 w-9 place-items-center rounded-xl bg-primary/15 text-primary">
              <Bot className="h-5 w-5" />
            </div>
            <div className="leading-tight">
              <h1 className="text-lg font-semibold tracking-tight">Chat AI</h1>
              <p className="text-[11px] text-muted-foreground">
                Ask about failures and health, or draft new schedules
                {model ? ` · ${model}` : ""}
              </p>
            </div>
          </div>
          {!serverHistory && items.length > 0 && (
            <button
              type="button"
              onClick={startNewChat}
              disabled={busy}
              className="inline-flex items-center gap-1.5 rounded-lg border border-border px-2.5 py-1.5 text-[11px] text-muted-foreground transition-colors hover:text-foreground hover:bg-surface-2 disabled:opacity-40"
            >
              <Trash2 className="h-3 w-3" />
              New chat
            </button>
          )}
        </div>

        {/* Messages */}
        <div
          ref={scrollRef}
          className="flex-1 space-y-4 overflow-y-auto scrollbar-thin rounded-xl border border-border bg-surface-0/50 p-5"
        >
          {loadingThread && (
            <div className="flex items-center justify-center py-12 text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />
            </div>
          )}

          {!loadingThread && items.length === 0 && (
            <div className="mt-16 flex flex-col items-center gap-4 text-center">
              <Sparkles className="h-7 w-7 text-primary/50" />
              <div>
                <p className="text-sm font-medium">What do you want to know?</p>
                <p className="mt-1 text-[12px] text-muted-foreground">
                  I read your live ticker data and explain what I find — and I can draft
                  new schedules for you to confirm.
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
                <div key={i} className="flex justify-start">
                  <ProposalCard
                    proposal={item.proposal}
                    status={item.status}
                    onStatusChange={(status) => setProposalStatus(i, status)}
                  />
                </div>
              ) : (
                <div key={i} className={cn("flex", item.role === "user" ? "justify-end" : "justify-start")}>
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
            disabled={busy || !input.trim()}
            className="grid h-[44px] w-[44px] shrink-0 place-items-center rounded-xl bg-primary text-primary-foreground disabled:opacity-40"
          >
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <SendHorizonal className="h-4 w-4" />}
          </button>
        </form>
      </div>
    </div>
  );
}
