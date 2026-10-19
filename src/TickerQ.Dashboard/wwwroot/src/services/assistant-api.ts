import { parseAssistantEvent } from "../lib/assistant-stream.ts";
import type { IntegrationRule, IntegrationMonitor } from "./integration-types";
import { getRuntimeConfig, normalizeBasePath } from "@/lib/runtime-config";
import { tokenStore } from "@/lib/auth/token-store";

export interface ChatTurn {
  role: "user" | "assistant";
  content: string;
}

export interface ConversationSummary {
  id: string;
  title: string;
  model: string;
  createdAt: string;
  updatedAt: string;
}

export interface HistoryMessage {
  role: "user" | "assistant";
  content: string;
  createdAt: string;
}

/**
 * A mutation drafted by the model, awaiting human confirmation. Confirming
 * executes through the dashboard's normal REST endpoints — the model itself
 * never writes anything.
 */
export interface AssistantProposal {
  kind: "createTimeTicker" | "createCronTicker" | "updateCronTicker" | "createChain"
    | "createCronChain" | "retryExecution" | "cancelExecution" | "deleteExecution"
    | "createAlertRule" | "createMonitor";
  function: string;
  expression: string;
  currentExpression: string;
  executionTime: string;
  retries: number | null;
  description: string;
  requestJson: string;
  targetId: string | null;
  isEnabled: boolean | null;
  /** Normalized chain tree as JSON (createChain only) — a single root ProposalChainNode. */
  chainJson: string;
  skipIfAlreadyRunning?: boolean | null;
  targetType?: "TimeTicker" | "CronOccurrence" | "CronTicker";
  expectedStatus?: string;
  expectedExecutedAt?: string | null;
  expectedLockedAt?: string | null;
  cronTickerId?: string | null;
  warning?: string;
  configurationVersion?: string | null;
  rule?: IntegrationRule | null;
  monitor?: IntegrationMonitor | null;
  destinationNames?: string[];
}

/** One step of a proposed chain (parsed from AssistantProposal.chainJson). */
export interface ProposalChainNode {
  function: string;
  retries: number | null;
  description: string;
  requestJson: string;
  runCondition: string | null;
  children: ProposalChainNode[] | null;
}

function apiUrl(path: string): string {
  const root = normalizeBasePath(getRuntimeConfig().basePath);
  const prefix = root === "/" ? "" : root;
  return `${prefix}/api/assistant${path}`;
}

function authHeaders(): Record<string, string> {
  const token = tokenStore.get();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

// ── Server-side history (available when assistant.history is true) ──

export async function listConversations(signal?: AbortSignal): Promise<ConversationSummary[]> {
  const res = await fetch(apiUrl("/conversations"), {
    headers: authHeaders(),
    credentials: "include",
    signal,
  });
  if (!res.ok) throw new Error(`Failed to load conversations (${res.status})`);
  return (await res.json()) as ConversationSummary[];
}

export async function getConversationMessages(
  id: string,
  signal?: AbortSignal
): Promise<HistoryMessage[]> {
  const res = await fetch(apiUrl(`/conversations/${id}`), {
    headers: authHeaders(),
    credentials: "include",
    signal,
  });
  if (!res.ok) throw new Error(`Failed to load conversation (${res.status})`);
  return (await res.json()) as HistoryMessage[];
}

export async function deleteConversation(id: string): Promise<void> {
  const res = await fetch(apiUrl(`/conversations/${id}`), {
    method: "DELETE",
    headers: authHeaders(),
    credentials: "include",
  });
  if (!res.ok && res.status !== 404) throw new Error(`Failed to delete conversation (${res.status})`);
}

/**
 * Stream a chat turn from the dashboard's assistant endpoint (SSE). Calls
 * `onDelta` for each text chunk; resolves with the conversation id (when the
 * server persists history — empty string otherwise). The server runs the tool
 * loop — the client only sends prior messages and renders the streamed answer.
 */
export async function streamChat(
  messages: ChatTurn[],
  onDelta: (text: string) => void,
  options?: {
    conversationId?: string | null;
    onProposal?: (proposal: AssistantProposal) => void;
    signal?: AbortSignal;
  }
): Promise<string> {
  const res = await fetch(apiUrl("/chat"), {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Accept: "text/event-stream",
      ...authHeaders(),
    },
    credentials: "include",
    body: JSON.stringify({ messages: messages.slice(-20), conversationId: options?.conversationId ?? null }),
    signal: options?.signal,
  });

  if (!res.ok || !res.body) {
    const body = await res.text().catch(() => "");
    throw new Error(body || `Assistant request failed (${res.status})`);
  }

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let conversationId = "";
  let completed = false;

  // Parse the SSE frames: each event is "data: {json}\n\n".
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });

      let idx: number;
      while ((idx = buffer.indexOf("\n\n")) !== -1) {
        const frame = buffer.slice(0, idx);
        buffer = buffer.slice(idx + 2);

        const line = frame.split("\n").find((l) => l.startsWith("data:"));
        if (!line) continue;
        const json = line.slice(5).trim();
        if (!json) continue;

        const evt = parseAssistantEvent(json);
        if (evt.type === "delta") onDelta(evt.data);
        else if (evt.type === "proposal")
          options?.onProposal?.(JSON.parse(evt.data) as AssistantProposal);
        else if (evt.type === "done") { conversationId = evt.data; completed = true; }

      }
    }

    if (!completed) throw new Error("Assistant connection ended before the response completed. Please try again.");
    return conversationId;
  } finally {
    await reader.cancel().catch(() => {});
    reader.releaseLock();
  }
}
