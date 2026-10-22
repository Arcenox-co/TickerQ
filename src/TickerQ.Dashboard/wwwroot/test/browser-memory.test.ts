import { test } from "node:test";
import assert from "node:assert/strict";
import { MAX_SEEN_ROW_IDS, rememberRecentRows } from "../src/lib/cron/row-highlight-cache.ts";
import { boundTranscript, chatItemsToTurns, chatMessage, historyMessagesToChatItems, MAX_CHAT_INVESTIGATION_GROUPS, MAX_CHAT_MESSAGES, MAX_CHAT_MESSAGE_CHARS, MAX_CHAT_PROPOSALS, updateProposalStatus, type ChatItem } from "../src/lib/assistant-transcript.ts";
import type { AssistantInvestigation, AssistantProposal, HistoryMessage } from "../src/services/assistant-api.ts";

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
  items = updateProposalStatus(items, "proposal-999", "applied", {
    message: "Job draft saved",
    href: "/time-tickers/new-chain?draft=draft-999",
    linkLabel: "Open draft",
  });
  assert.equal(items.find(x => x.id === "proposal-999")?.type, "proposal");
  assert.equal(items.filter(x => x.type === "proposal" && x.status === "applied").length, 1);
  const applied = items.find((x): x is Extract<ChatItem, { type: "proposal" }> => x.id === "proposal-999" && x.type === "proposal");
  assert.equal(applied?.result?.href, "/time-tickers/new-chain?draft=draft-999");
  assert.equal(items.at(-1)?.type, "message");
  assert.equal((chatMessage("assistant", "a".repeat(MAX_CHAT_MESSAGE_CHARS * 2)) as Extract<ChatItem, { type: "message" }>).content.length, MAX_CHAT_MESSAGE_CHARS);
});

test("chat bounds investigation suggestion groups and cards", () => {
  const investigation = (id: string) => ({ id }) as AssistantInvestigation;
  const items: ChatItem[] = Array.from({ length: MAX_CHAT_INVESTIGATION_GROUPS * 3 }, (_, index) => ({
    id: `investigations-${index}`,
    type: "investigations" as const,
    investigations: Array.from({ length: 8 }, (_, card) => investigation(`${index}-${card}`)),
  }));
  const bounded = boundTranscript(items);
  assert.equal(bounded.length, MAX_CHAT_INVESTIGATION_GROUPS);
  assert.ok(bounded.every(item => item.type === "investigations" && item.investigations.length === 3));
});

test("proposal cards are included in model context for follow-up requests", () => {
  const proposal = {
    kind: "createTimeTickerDraft",
    draftName: "Execute progress",
    function: "Demo.Progress",
    executionTime: "2026-10-23T06:00:00Z",
  } as AssistantProposal;
  const turns = chatItemsToTurns([
    chatMessage("user", "Create a job"),
    { id: "proposal-1", type: "proposal", proposal, status: "applied" },
    chatMessage("assistant", "The draft is ready."),
    chatMessage("user", "another one"),
  ]);

  assert.equal(turns.length, 4);
  assert.match(turns[1].content, /Previous TickerQ proposal card/);
  assert.match(turns[1].content, /Demo\.Progress/);
  assert.match(turns[1].content, /"status":"applied"/);
  assert.equal(turns.at(-1)?.content, "another one");
});

test("server history restores proposal state and only trusted draft links", () => {
  const proposalId = "ed695035-11aa-4f4e-92a1-24a99d4ecbf0";
  const proposal = {
    kind: "createTimeTickerDraft", function: "Demo.Progress", expression: "", currentExpression: "",
    executionTime: "2026-10-22T11:30:00Z", retries: null, description: "", requestJson: "",
    targetId: null, isEnabled: null, chainJson: "",
  } satisfies AssistantProposal;
  const row = (href: string): HistoryMessage => ({
    id: proposalId,
    role: "proposal",
    content: "",
    createdAt: "2026-10-22T10:00:00Z",
    proposalJson: JSON.stringify(proposal),
    proposalStatus: "applied",
    proposalResultJson: JSON.stringify({ message: "Job draft saved", href, linkLabel: "Open draft" }),
  });

  const restored = historyMessagesToChatItems([row("/time-tickers/new-chain?draft=91af8d52-95fd-4a37-867c-a42176111d4c")]);
  assert.equal(restored[0].type, "proposal");
  if (restored[0].type === "proposal") {
    assert.equal(restored[0].status, "applied");
    assert.equal(restored[0].proposal.proposalId, proposalId);
    assert.equal(restored[0].result?.href, "/time-tickers/new-chain?draft=91af8d52-95fd-4a37-867c-a42176111d4c");
  }

  const unsafe = historyMessagesToChatItems([row("https://attacker.example")]);
  assert.equal(unsafe[0].type, "proposal");
  if (unsafe[0].type === "proposal") assert.equal(unsafe[0].result?.href, undefined);
});
