import { parseAssistantEvent } from "../lib/assistant-stream.ts";
import type { IntegrationRule, IntegrationMonitor } from "./integration-types";
import { resolveDashboardUrl } from "@/lib/runtime-config";
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
  id: string;
  role: "user" | "assistant" | "proposal";
  content: string;
  createdAt: string;
  proposalJson?: string;
  proposalStatus?: "pending" | "applied" | "dismissed" | string;
  proposalResultJson?: string;
}

export interface AssistantInvestigation {
  id: string;
  applicationNamespace: string;
  executionId: string;
  executionType: "TimeTicker" | "CronOccurrence" | string;
  chainRootId: string | null;
  functionName: string;
  executionStatus: string;
  classification: string;
  summary: string;
  model: string;
  trigger: "User" | "Automatic" | string;
  state: "Processing" | "Completed" | "Failed" | string;
  reviewState: "New" | "Reviewed" | "Resolved" | string;
  evidenceFingerprint: string;
  sourceRepository: string;
  sourceRevision: string;
  sourcePath: string;
  sourceLine: number | null;
  conversationId: string | null;
  occurredAt: string;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
  reviewedAt: string | null;
  recoveredByExecutionId: string | null;
  recoveredAt: string | null;
  failureMessage: string;
}

export interface AssistantInvestigationPage {
  items: AssistantInvestigation[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

export interface AssistantInvestigationPattern {
  functionName: string;
  classification: string;
  count: number;
  latestAt: string;
}

export interface AssistantInvestigationOverview {
  total: number;
  awaitingReview: number;
  completedToday: number;
  recovered: number;
  processing: number;
  recent: AssistantInvestigation[];
  patterns: AssistantInvestigationPattern[];
}

/**
 * A mutation drafted by the model, awaiting human confirmation. Confirming
 * executes through the dashboard's normal REST endpoints — the model itself
 * never writes anything.
 */
export interface AssistantProposal {
  proposalId?: string | null;
  kind: "createTimeTicker" | "createTimeTickerDraft" | "createCronTicker" | "updateCronTicker" | "createChain" | "createChainDraft"
    | "createCronChain" | "retryExecution" | "cancelExecution" | "deleteExecution"
    | "createAlertRule" | "createMonitor";
  draftName?: string;
  function: string;
  expression: string;
  currentExpression: string;
  executionTime: string;
  retries: number | null;
  description: string;
  requestJson: string;
  targetId: string | null;
  isEnabled: boolean | null;
  /** Normalized chain tree as JSON (createChain/createChainDraft only) — a single root ProposalChainNode. */
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
  return resolveDashboardUrl(`/api/assistant${path}`);
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

export async function setAssistantProposalState(
  id: string,
  status: "applied" | "dismissed",
  result?: unknown,
): Promise<void> {
  const res = await fetch(apiUrl(`/proposals/${encodeURIComponent(id)}`), {
    method: "PATCH",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    credentials: "include",
    body: JSON.stringify({
      status,
      resultJson: result == null ? "" : JSON.stringify(result),
    }),
  });
  if (!res.ok) throw new Error(`Failed to save proposal state (${res.status})`);
}

// ── Structured run investigations (available when assistant.investigations is true) ──

export async function getInvestigationOverview(signal?: AbortSignal): Promise<AssistantInvestigationOverview> {
  const res = await fetch(apiUrl("/overview"), {
    headers: authHeaders(), credentials: "include", signal,
  });
  if (!res.ok) throw new Error(`Failed to load AI overview (${res.status})`);
  return (await res.json()) as AssistantInvestigationOverview;
}

export async function listInvestigations(
  query: { search?: string; state?: string; reviewState?: string; classification?: string; groupByFunction?: boolean; page?: number; pageSize?: number } = {},
  signal?: AbortSignal,
): Promise<AssistantInvestigationPage> {
  const params = new URLSearchParams();
  if (query.search) params.set("search", query.search);
  if (query.state) params.set("state", query.state);
  if (query.reviewState) params.set("reviewState", query.reviewState);
  if (query.classification) params.set("classification", query.classification);
  if (query.groupByFunction) params.set("groupByFunction", "true");
  params.set("page", String(query.page ?? 1));
  params.set("pageSize", String(query.pageSize ?? 20));
  const res = await fetch(apiUrl(`/investigations?${params.toString()}`), {
    headers: authHeaders(), credentials: "include", signal,
  });
  if (!res.ok) throw new Error(`Failed to load investigations (${res.status})`);
  return (await res.json()) as AssistantInvestigationPage;
}

export async function getInvestigation(id: string, signal?: AbortSignal): Promise<AssistantInvestigation> {
  const res = await fetch(apiUrl(`/investigations/${encodeURIComponent(id)}`), {
    headers: authHeaders(), credentials: "include", signal,
  });
  if (!res.ok) throw new Error(`Failed to load investigation (${res.status})`);
  return (await res.json()) as AssistantInvestigation;
}

export async function setInvestigationReviewState(
  id: string,
  reviewState: "New" | "Reviewed" | "Resolved",
): Promise<AssistantInvestigation> {
  const res = await fetch(apiUrl(`/investigations/${encodeURIComponent(id)}/review`), {
    method: "PATCH",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    credentials: "include",
    body: JSON.stringify({ reviewState }),
  });
  if (!res.ok) throw new Error(`Failed to update investigation (${res.status})`);
  return (await res.json()) as AssistantInvestigation;
}

export async function deleteInvestigation(id: string): Promise<void> {
  const res = await fetch(apiUrl(`/investigations/${encodeURIComponent(id)}`), {
    method: "DELETE",
    headers: authHeaders(),
    credentials: "include",
  });
  if (!res.ok) throw new Error(`Failed to delete investigation (${res.status})`);
}

export async function deleteInvestigationGroup(functionName: string): Promise<void> {
  const params = new URLSearchParams({ functionName });
  const res = await fetch(apiUrl(`/investigations?${params.toString()}`), {
    method: "DELETE",
    headers: authHeaders(),
    credentials: "include",
  });
  if (!res.ok) throw new Error(`Failed to delete investigation group (${res.status})`);
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
    investigationId?: string | null;
    onProposal?: (proposal: AssistantProposal) => void;
    onInvestigations?: (investigations: AssistantInvestigation[]) => void;
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
    body: JSON.stringify({
      messages: messages.slice(-20),
      conversationId: options?.conversationId ?? null,
      investigationId: options?.investigationId ?? null,
    }),
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
        else if (evt.type === "investigations") {
          const page = JSON.parse(evt.data) as AssistantInvestigationPage;
          if (Array.isArray(page.items) && page.items.length)
            options?.onInvestigations?.(page.items);
        }
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
