import { boundTranscript, chatMessage, MAX_CHAT_MESSAGES, type ChatItem } from "./assistant-transcript.ts";

const LEGACY_KEY = "tickerq-assistant-chat";
type StorageAccess = Pick<Storage, "getItem" | "setItem" | "removeItem">;

/** No shared fallback key: unknown/anonymous identities keep chats in memory only. */
export function assistantStorageKey(userId: string | null, dashboardUrl: string): string | null {
  return userId ? `tickerq-assistant-chat:v2:${encodeURIComponent(dashboardUrl)}:${encodeURIComponent(userId)}` : null;
}

export function loadAssistantTranscript(storage: StorageAccess, key: string | null): ChatItem[] {
  try {
    // Legacy transcripts have no provable owner and must never be migrated to the next account.
    storage.removeItem(LEGACY_KEY);
    if (!key) return [];
    const parsed: unknown = JSON.parse(storage.getItem(key) ?? "[]");
    if (!Array.isArray(parsed)) return [];
    return boundTranscript(parsed.filter((m) => m && (m.role === "user" || m.role === "assistant") && typeof m.content === "string")
      .slice(-MAX_CHAT_MESSAGES)
      .map((m) => chatMessage(m.role, m.content)));
  } catch { return []; }
}

export function storeAssistantTranscript(storage: StorageAccess, key: string | null, items: ChatItem[]): void {
  if (!key) return;
  try {
    const messages = boundTranscript(items)
      .filter((item): item is Extract<ChatItem, { type: "message" }> => item.type === "message" && item.content !== "")
      .map(({ role, content }) => ({ role, content }));
    storage.setItem(key, JSON.stringify(messages));
  } catch { /* Storage is optional. */ }
}

export function clearAssistantTranscript(storage: StorageAccess, key: string | null): void {
  try { storage.removeItem(LEGACY_KEY); if (key) storage.removeItem(key); } catch { /* Storage is optional. */ }
}
