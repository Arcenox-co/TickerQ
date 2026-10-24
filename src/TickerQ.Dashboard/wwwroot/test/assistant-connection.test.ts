import test from "node:test";
import assert from "node:assert/strict";
import { describeAssistantConnection, providerDisplayName } from "../src/lib/assistant-connection.ts";

const base = {
  enabled: true,
  model: "llama3.1",
  provider: "ollama",
  endpoint: "localhost:11434",
  location: "local" as const,
  connectionFailed: false,
};

test("no chat client means not connected and nothing is sent", () => {
  const connection = describeAssistantConnection({ ...base, enabled: false });
  assert.equal(connection.state, "not-configured");
  assert.equal(connection.connected, false);
  assert.equal(connection.status, "Not connected");
  assert.match(connection.privacyNote, /Nothing is sent/);
});

test("a chat client that could not be created is not connected", () => {
  const connection = describeAssistantConnection({ ...base, connectionFailed: true });
  assert.equal(connection.state, "failed");
  assert.equal(connection.connected, false);
});

test("a local endpoint says messages stay on this machine", () => {
  const connection = describeAssistantConnection(base);
  assert.equal(connection.state, "local");
  assert.equal(connection.placement, "Local model");
  assert.equal(connection.provider, "Ollama");
  assert.equal(connection.privacyNote, "Runs on a local model at localhost:11434. Messages stay on this machine.");
});

test("a local server reached through the OpenAI SDK is labeled OpenAI-compatible", () => {
  assert.equal(describeAssistantConnection({ ...base, provider: "openai" }).provider, "OpenAI-compatible");
  assert.equal(describeAssistantConnection({ ...base, provider: "openai", location: "remote" }).provider, "OpenAI");
});

test("a hosted endpoint names the provider that receives messages", () => {
  const connection = describeAssistantConnection({
    ...base, provider: "openai", endpoint: "api.openai.com", location: "remote", model: "gpt-4o-mini",
  });
  assert.equal(connection.state, "remote");
  assert.equal(connection.placement, "Hosted model");
  assert.equal(connection.privacyNote, "Messages and tool results are sent to OpenAI (api.openai.com).");
});

test("an unknown location stays connected without claiming where data goes", () => {
  const connection = describeAssistantConnection({ ...base, provider: "", endpoint: "", location: "unknown" });
  assert.equal(connection.state, "unknown");
  assert.equal(connection.connected, true);
  assert.equal(connection.placement, "");
});

test("unknown providers keep their reported name", () => {
  assert.equal(providerDisplayName("my-gateway"), "my-gateway");
  assert.equal(providerDisplayName("OpenAI"), "OpenAI");
  assert.equal(providerDisplayName(""), "");
});
