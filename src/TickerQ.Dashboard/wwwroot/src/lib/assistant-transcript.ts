import type { AssistantProposal } from "../services/assistant-api";

export type ChatItem =
  | { id: string; type: "message"; role: "user" | "assistant"; content: string }
  | { id: string; type: "proposal"; proposal: AssistantProposal; status: "pending" | "applied" | "dismissed" };

export const MAX_CHAT_MESSAGES = 40;
export const MAX_CHAT_PROPOSALS = 20;
export const MAX_CHAT_MESSAGE_CHARS = 65_536;

export function chatMessage(role: "user" | "assistant", content: string): ChatItem {
  return { id: crypto.randomUUID(), type: "message", role, content: content.slice(0, MAX_CHAT_MESSAGE_CHARS) };
}

/** Stable ids survive pruning and async confirmation callbacks during streaming. */
export function boundTranscript(items: ChatItem[]): ChatItem[] {
  let messages = 0;
  let proposals = 0;
  const kept: ChatItem[] = [];
  for (let index = items.length - 1; index >= 0; index--) {
    const item = items[index];
    if (item.type === "message") {
      if (++messages <= MAX_CHAT_MESSAGES)
        kept.push(item.content.length > MAX_CHAT_MESSAGE_CHARS ? { ...item, content: item.content.slice(0, MAX_CHAT_MESSAGE_CHARS) } : item);
    } else if (++proposals <= MAX_CHAT_PROPOSALS) kept.push(item);
  }
  return kept.reverse();
}

export function updateProposalStatus(items: ChatItem[], id: string, status: "pending" | "applied" | "dismissed"): ChatItem[] {
  return items.map((item) => item.id === id && item.type === "proposal" ? { ...item, status } : item);
}
