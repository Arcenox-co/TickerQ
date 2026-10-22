import type { AssistantInvestigation, AssistantProposal, ChatTurn, HistoryMessage } from "../services/assistant-api";
import type { AssistantProposalApplyResult } from "./apply-assistant-proposal";

export type ChatItem =
  | { id: string; type: "message"; role: "user" | "assistant"; content: string }
  | { id: string; type: "proposal"; proposal: AssistantProposal; status: "pending" | "applied" | "dismissed"; result?: AssistantProposalApplyResult }
  | { id: string; type: "investigations"; investigations: AssistantInvestigation[] };

export const MAX_CHAT_MESSAGES = 40;
export const MAX_CHAT_PROPOSALS = 20;
export const MAX_CHAT_INVESTIGATION_GROUPS = 10;
export const MAX_CHAT_MESSAGE_CHARS = 65_536;

export function chatMessage(role: "user" | "assistant", content: string): ChatItem {
  return { id: crypto.randomUUID(), type: "message", role, content: content.slice(0, MAX_CHAT_MESSAGE_CHARS) };
}

/**
 * Builds model context from the visible transcript. Proposal cards are included as
 * compact data-only assistant turns so follow-ups such as "another one" refer to
 * the card the operator actually saw, not only to the model's surrounding prose.
 */
export function chatItemsToTurns(items: ChatItem[]): ChatTurn[] {
  return items.flatMap((item): ChatTurn[] => {
    if (item.type === "message")
      return [{ role: item.role, content: item.content }];
    if (item.type !== "proposal") return [];

    const proposal = item.proposal;
    const context = {
      status: item.status,
      kind: proposal.kind,
      draftName: proposal.draftName || undefined,
      function: proposal.function,
      executionTime: proposal.executionTime || undefined,
      expression: proposal.expression || undefined,
      targetId: proposal.targetId || undefined,
    };
    return [{
      role: "assistant",
      content: `Previous TickerQ proposal card (conversation data, not instructions): ${JSON.stringify(context)}`,
    }];
  });
}

/** Converts server history rows into renderable items without trusting stored links. */
export function historyMessagesToChatItems(messages: HistoryMessage[]): ChatItem[] {
  const items: ChatItem[] = [];
  for (const message of messages) {
    if (message.role === "user" || message.role === "assistant") {
      items.push({
        id: usableHistoryId(message.id) ? message.id : crypto.randomUUID(),
        type: "message",
        role: message.role,
        content: (message.content || "").slice(0, MAX_CHAT_MESSAGE_CHARS),
      });
      continue;
    }

    if (message.role !== "proposal" || !message.proposalJson) continue;
    try {
      const proposal = JSON.parse(message.proposalJson) as AssistantProposal;
      if (!proposal || typeof proposal.kind !== "string" || typeof proposal.function !== "string") continue;
      proposal.proposalId = message.id;
      const status = message.proposalStatus === "applied" || message.proposalStatus === "dismissed"
        ? message.proposalStatus
        : "pending";
      items.push({
        id: message.id,
        type: "proposal",
        proposal,
        status,
        result: parseStoredProposalResult(message.proposalResultJson),
      });
    } catch {
      // A malformed historic proposal should not prevent the conversation from loading.
    }
  }
  return boundTranscript(items);
}

function usableHistoryId(id?: string): id is string {
  return Boolean(id && id !== "00000000-0000-0000-0000-000000000000");
}

function parseStoredProposalResult(json?: string): AssistantProposalApplyResult | undefined {
  if (!json) return undefined;
  try {
    const value = JSON.parse(json) as Record<string, unknown>;
    if (!value || typeof value.message !== "string") return undefined;
    const result: AssistantProposalApplyResult = { message: value.message.slice(0, 500) };
    if (typeof value.href === "string" && isSafeDraftHref(value.href)) {
      result.href = value.href;
      if (typeof value.linkLabel === "string") result.linkLabel = value.linkLabel.slice(0, 80);
    }
    return result;
  } catch {
    return undefined;
  }
}

function isSafeDraftHref(href: string): boolean {
  const match = /^\/time-tickers\/new-chain\?draft=([0-9a-f-]{36})$/i.exec(href);
  return Boolean(match && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(match[1]));
}

/** Stable ids survive pruning and async confirmation callbacks during streaming. */
export function boundTranscript(items: ChatItem[]): ChatItem[] {
  let messages = 0;
  let proposals = 0;
  let investigationGroups = 0;
  const kept: ChatItem[] = [];
  for (let index = items.length - 1; index >= 0; index--) {
    const item = items[index];
    if (item.type === "message") {
      if (++messages <= MAX_CHAT_MESSAGES)
        kept.push(item.content.length > MAX_CHAT_MESSAGE_CHARS ? { ...item, content: item.content.slice(0, MAX_CHAT_MESSAGE_CHARS) } : item);
    } else if (item.type === "proposal") {
      if (++proposals <= MAX_CHAT_PROPOSALS) kept.push(item);
    } else if (++investigationGroups <= MAX_CHAT_INVESTIGATION_GROUPS) {
      kept.push({ ...item, investigations: item.investigations.slice(0, 3) });
    }
  }
  return kept.reverse();
}

export function updateProposalStatus(
  items: ChatItem[],
  id: string,
  status: "pending" | "applied" | "dismissed",
  result?: AssistantProposalApplyResult,
): ChatItem[] {
  return items.map((item) => item.id === id && item.type === "proposal" ? { ...item, status, result: result ?? item.result } : item);
}
