import { test } from "node:test";
import assert from "node:assert/strict";
import { formatApplicationNamespace, formatFriendlyVersion, resolveDashboardUrl } from "../src/lib/runtime-config.ts";
import { assistantStorageKey, loadAssistantTranscript, storeAssistantTranscript, clearAssistantTranscript } from "../src/lib/assistant-storage.ts";
import { chatMessage } from "../src/lib/assistant-transcript.ts";
import { LatestRequest } from "../src/lib/latest-request.ts";
import { TokenStore } from "../src/lib/auth/token-store.ts";

function storage() {
  const values = new Map<string, string>();
  return { values, getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value); }, removeItem: (key: string) => { values.delete(key); } };
}

test("dashboard identity keeps friendly versions and readable namespaces", () => {
  assert.equal(formatFriendlyVersion("10.5.0-rc.1+d9f321"), "10.5.0-rc.1");
  assert.equal(formatFriendlyVersion("10.5.0"), "10.5.0");
  assert.equal(formatApplicationNamespace("orders-production"), "orders-production");
  assert.equal(formatApplicationNamespace(null), "Legacy global");
});

test("assistant browser history is isolated by authenticated owner, mount and backend", () => {
  const saved = storage();
  const alice = assistantStorageKey("alice", "https://api.test/tenant-a/dashboard/")!;
  const bob = assistantStorageKey("bob", "https://api.test/tenant-a/dashboard/")!;
  const otherMount = assistantStorageKey("alice", "https://api.test/tenant-b/dashboard/")!;
  const otherBackend = assistantStorageKey("alice", "https://other.test/tenant-a/dashboard/")!;
  storeAssistantTranscript(saved, alice, [chatMessage("user", "Private transcript")]);
  assert.equal(loadAssistantTranscript(saved, alice).length, 1);
  for (const key of [bob, otherMount, otherBackend]) assert.deepEqual(loadAssistantTranscript(saved, key), []);
  clearAssistantTranscript(saved, alice);
  assert.deepEqual(loadAssistantTranscript(saved, alice), []);
});

test("anonymous, unresolved and legacy transcripts cannot be inherited by a new account", () => {
  const saved = storage();
  saved.setItem("tickerq-assistant-chat", JSON.stringify([{ role: "user", content: "Previous account" }]));
  assert.equal(assistantStorageKey(null, "https://api.test/dashboard/"), null);
  assert.deepEqual(loadAssistantTranscript(saved, null), []);
  storeAssistantTranscript(saved, null, [chatMessage("user", "Anonymous")]);
  assert.equal(saved.values.size, 0);
  assert.deepEqual(loadAssistantTranscript(saved, assistantStorageKey("alice", "https://api.test/dashboard/")), []);
});

test("malformed or unavailable browser storage remains optional", () => {
  const saved = storage();
  saved.setItem("key", "[null, {}, {\"role\":\"user\",\"content\":\"ok\"}]");
  assert.equal(loadAssistantTranscript(saved, "key").length, 1);
  const broken = { getItem() { throw new Error("Unavailable"); }, setItem() { throw new Error("Unavailable"); }, removeItem() { throw new Error("Unavailable"); } };
  assert.deepEqual(loadAssistantTranscript(broken, "key"), []);
  assert.doesNotThrow(() => storeAssistantTranscript(broken, "key", []));
});

test("all endpoint families resolve under the configured origin and complete mount path", () => {
  for (const basePath of ["/", "/tickerq/dashboard/", "/proxy/tenant/tickerq/dashboard/"]) {
    for (const path of ["/api/dashboard/time-tickers/query", "/api/options", "/api/auth/login", "/api/auth/refresh", "/api/auth/logout", "/api/assistant/chat", "/api/assistant/conversations", "/tickerq-notification-hub"]) {
      const prefix = basePath === "/" ? "" : basePath.slice(0, -1);
      assert.equal(resolveDashboardUrl(path, { basePath, backendDomain: "" }), `${prefix}${path}`);
      assert.equal(resolveDashboardUrl(path, { basePath, backendDomain: "https://api.test:8443/" }), `https://api.test:8443${prefix}${path}`);
    }
  }
});

test("endpoint resolver rejects ambiguous or credential-bearing backend targets", () => {
  for (const backendDomain of ["//evil.test", "https://user:secret@api.test", "https://api.test/path", "https://api.test?token=secret", "javascript:alert(1)"]) {
    assert.throws(() => resolveDashboardUrl("/api/auth/login", { basePath: "/dashboard", backendDomain }));
  }
  for (const path of ["https://evil.test", "//evil.test", "/\\evil.test"]) {
    assert.throws(() => resolveDashboardUrl(path, { basePath: "/", backendDomain: "" }));
  }
});

test("bearer session storage is scoped to the configured backend and mount", () => {
  const saved = storage();
  const originalWindow = Object.getOwnPropertyDescriptor(globalThis, "window");
  const originalStorage = Object.getOwnPropertyDescriptor(globalThis, "sessionStorage");
  const browser = { location: { protocol: "https:" }, TickerQConfig: { basePath: "/a", backendDomain: "https://api.test" } };
  Object.defineProperty(globalThis, "window", { value: browser, configurable: true });
  Object.defineProperty(globalThis, "sessionStorage", { value: saved, configurable: true });
  try {
    saved.setItem("tickerq.auth.token", "legacy-unscoped-token");
    const first = new TokenStore();
    assert.equal(first.get(), null);
    assert.equal(saved.getItem("tickerq.auth.token"), null);
    first.set("alice-token");
    assert.equal(new TokenStore().get(), "alice-token");
    browser.TickerQConfig.basePath = "/b";
    assert.equal(new TokenStore().get(), null);
    browser.TickerQConfig.basePath = "/a";
    browser.TickerQConfig.backendDomain = "https://other.test";
    assert.equal(new TokenStore().get(), null);
    assert.throws(() => resolveDashboardUrl("/api/auth/login", { basePath: "/", backendDomain: "http://api.test" }), /HTTPS/);
    first.set(null);
    assert.equal(saved.values.size, 0);
  } finally {
    if (originalWindow) Object.defineProperty(globalThis, "window", originalWindow); else Reflect.deleteProperty(globalThis, "window");
    if (originalStorage) Object.defineProperty(globalThis, "sessionStorage", originalStorage); else Reflect.deleteProperty(globalThis, "sessionStorage");
  }
});

test("switching threads ignores a late result even when the old request ignores abort", async () => {
  const requests = new LatestRequest();
  const first = requests.begin();
  let resolveFirst!: (value: string) => void;
  let displayed = "";
  const oldLoad = new Promise<string>((resolve) => { resolveFirst = resolve; }).then((value) => {
    if (first.isCurrent()) displayed = value;
  });
  const second = requests.begin();
  assert.equal(first.signal.aborted, true);
  if (second.isCurrent()) displayed = "Second thread";
  resolveFirst("First thread");
  await oldLoad;
  assert.equal(displayed, "Second thread");
  requests.cancel(); // New chat, logout or unmount.
  assert.equal(second.isCurrent(), false);
  assert.equal(second.signal.aborted, true);
});
