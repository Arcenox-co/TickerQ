import { test } from "node:test";
import assert from "node:assert/strict";
import { MAX_SEEN_ROW_IDS, rememberRecentRows } from "../src/lib/cron/row-highlight-cache.ts";
import { boundTranscript, chatMessage, MAX_CHAT_MESSAGES, MAX_CHAT_MESSAGE_CHARS, MAX_CHAT_PROPOSALS, updateProposalStatus, type ChatItem } from "../src/lib/assistant-transcript.ts";
import type { AssistantProposal } from "../src/services/assistant-api.ts";

test("row highlights expire seen ids and cap a high-volume recent window", () => {
  const seen = new Map<string, number>();
  const now = 1_000_000;
  const read = (rows: { id: string; created: number }[], time = now) =>
    rememberRecentRows(seen, rows, x => x.id, x => x.created, time);
  assert.deepEqual(read([{ id: "old", created: now - 30_000 }, { id: "future", created: now + 1 }]), []);
  assert.equal(seen.size, 0);
  assert.deepEqual(read([{ id: "fresh", created: now }]), ["fresh"]);
  assert.deepEqual(read([{ id: "fresh", created: now }]), []);
  const burst = Array.from({ length: MAX_SEEN_ROW_IDS * 3 }, (_, i) => ({ id: `row-${i}`, created: now }));
  assert.equal(read(burst).length, MAX_SEEN_ROW_IDS);
  assert.equal(seen.size, MAX_SEEN_ROW_IDS);
  assert.deepEqual(read(burst, now + 30_000), []);
  assert.equal(seen.size, 0);
});

test("long chat keeps bounded messages/proposals and stable confirmation ids while streaming", () => {
  let items: ChatItem[] = [];
  const proposal = { kind: "createTimeTicker", function: "job" } as AssistantProposal;
  for (let i = 0; i < 1000; i++) {
    items = boundTranscript([...items, chatMessage("user", `question ${i}`), chatMessage("assistant", "")]);
    items.splice(items.length - 1, 0, { id: `proposal-${i}`, type: "proposal", proposal, status: "pending" });
    items = boundTranscript(items);
    const last = items.at(-1)!;
    assert.equal(last.type, "message");
    if (last.type === "message") last.content += `answer ${i}`;
    assert.ok(items.length <= MAX_CHAT_MESSAGES + MAX_CHAT_PROPOSALS);
  }
  assert.equal(items.filter(x => x.type === "message").length, MAX_CHAT_MESSAGES);
  assert.equal(items.filter(x => x.type === "proposal").length, MAX_CHAT_PROPOSALS);
  // A pending action can resolve after insertion/pruning shifts its former index.
  items = boundTranscript([...items, chatMessage("user", "next"), chatMessage("assistant", "")]);
  items = updateProposalStatus(items, "proposal-999", "applied");
  assert.equal(items.find(x => x.id === "proposal-999")?.type, "proposal");
  assert.equal(items.filter(x => x.type === "proposal" && x.status === "applied").length, 1);
  assert.equal(items.at(-1)?.type, "message");
  assert.equal((chatMessage("assistant", "a".repeat(MAX_CHAT_MESSAGE_CHARS * 2)) as Extract<ChatItem, { type: "message" }>).content.length, MAX_CHAT_MESSAGE_CHARS);
});
