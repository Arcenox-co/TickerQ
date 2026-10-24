import type { TickerQAssistantConfig } from "./runtime-config";

export type AssistantConnectionState = "not-configured" | "failed" | "local" | "remote" | "unknown";

export interface AssistantConnection {
  state: AssistantConnectionState;
  /** True when the chat can send messages. */
  connected: boolean;
  /** Short status label, for example "Connected" or "Not connected". */
  status: string;
  /** Where the model runs, for example "Local model" or "Hosted model". Empty when not connected. */
  placement: string;
  /** Display name of the provider, for example "OpenAI". Empty when unknown. */
  provider: string;
  model: string;
  endpoint: string;
  /** One sentence telling the operator where chat messages go. */
  privacyNote: string;
}

const providerNames: Record<string, string> = {
  openai: "OpenAI",
  "azure.ai.openai": "Azure OpenAI",
  azure_openai: "Azure OpenAI",
  "azure.ai.inference": "Azure AI",
  anthropic: "Anthropic",
  ollama: "Ollama",
  onnx: "ONNX Runtime",
  "aws.bedrock": "Amazon Bedrock",
  gemini: "Google Gemini",
  "gcp.gemini": "Google Gemini",
};

export function providerDisplayName(provider: string | null | undefined): string {
  const trimmed = provider?.trim() ?? "";
  return providerNames[trimmed.toLowerCase()] ?? trimmed;
}

export function describeAssistantConnection(config: Pick<TickerQAssistantConfig,
  "enabled" | "model" | "provider" | "endpoint" | "location" | "connectionFailed">): AssistantConnection {
  // A local server reached through the OpenAI SDK reports "openai"; it only speaks that protocol.
  const reported = providerDisplayName(config.provider);
  const provider = config.location === "local" && reported === "OpenAI" ? "OpenAI-compatible" : reported;
  const model = config.model?.trim() ?? "";
  const endpoint = config.endpoint?.trim() ?? "";
  const base = { provider, model, endpoint };

  if (!config.enabled) {
    return {
      ...base,
      state: "not-configured",
      connected: false,
      status: "Not connected",
      placement: "",
      privacyNote: "No AI model is configured. Nothing is sent anywhere.",
    };
  }
  if (config.connectionFailed) {
    return {
      ...base,
      state: "failed",
      connected: false,
      status: "Connection failed",
      placement: "",
      privacyNote: "The server could not create the configured chat client. Check its API key or endpoint.",
    };
  }
  if (config.location === "local") {
    return {
      ...base,
      state: "local",
      connected: true,
      status: "Connected",
      placement: "Local model",
      privacyNote: `Runs on a local model${endpoint ? ` at ${endpoint}` : ""}. Messages stay on this machine.`,
    };
  }
  if (config.location === "remote") {
    const target = provider || endpoint || "the model provider";
    return {
      ...base,
      state: "remote",
      connected: true,
      status: "Connected",
      placement: "Hosted model",
      privacyNote: `Messages and tool results are sent to ${target}${provider && endpoint ? ` (${endpoint})` : ""}.`,
    };
  }
  return {
    ...base,
    state: "unknown",
    connected: true,
    status: "Connected",
    placement: "",
    privacyNote: "Messages and tool results are sent to the configured model provider.",
  };
}
