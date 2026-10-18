import type {
  IntegrationConfiguration,
  IntegrationDestination,
} from "../../services/integration-types.ts";

export const eventOptions = [
  {
    value: "failed",
    label: "Final failure",
    description: "A job fails after its retries are exhausted.",
  },
  {
    value: "timeout_cancelled",
    label: "Execution timed out",
    description: "A job reaches its execution time limit.",
  },
  {
    value: "stale_recovered",
    label: "Abandoned jobs recovered",
    description: "The scheduler recovers jobs left by a stopped worker.",
  },
  {
    value: "contract_drift",
    label: "Request contract changed",
    description: "A job's saved request no longer matches its function.",
  },
];
export const providerName = (provider: string) =>
  ({ slack: "Slack", teams: "Microsoft Teams", webhook: "Custom webhook" })[
    provider
  ] ?? provider;
export const providerDescription = (provider: string) =>
  ({
    slack: "Send job alerts to a Slack channel.",
    teams: "Post alerts through a Teams Workflow.",
    webhook: "Send a JSON payload to your own endpoint.",
  })[provider] ?? "Connect an application to your job alerts.";
export const errorText = (error: unknown) =>
  error instanceof Error ? error.message : "Please try again.";
export const splitPatterns = (value: string) => [
  ...new Set(
    value
      .split(/[\n,]/)
      .map((s) => s.trim())
      .filter(Boolean),
  ),
];
// Materialize the legacy shared template into all categories only when editing.
// Viewing a connection never changes its saved configuration.
export function editableDestination(
  value: IntegrationDestination,
): IntegrationDestination {
  const copy = structuredClone(value);
  copy.messageTemplates ??= {
    jobAlert: copy.payloadTemplate ?? null,
    monitoringAlert: copy.payloadTemplate ?? null,
    recovery: copy.payloadTemplate ?? null,
  };
  copy.payloadTemplate = null;
  return copy;
}

export function newDestination(provider: string): IntegrationDestination {
  return {
    id: crypto.randomUUID(),
    name: "",
    provider,
    enabled: true,
    url: null,
    headers: null,
    payloadTemplate: null,
  };
}

// Blank keeps the write-only saved values; an explicit empty object clears them.
export function parseHeaders(value: string): Record<string, string> | null {
  if (!value.trim()) return null;
  let parsed: unknown;
  try {
    parsed = JSON.parse(value);
  } catch {
    throw new Error(
      'Enter headers as a JSON object, for example {"Authorization": "Bearer …"}.',
    );
  }
  if (
    !parsed ||
    Array.isArray(parsed) ||
    typeof parsed !== "object" ||
    Object.values(parsed).some((v) => typeof v !== "string")
  )
    throw new Error(
      "Header names and values must be strings in a JSON object.",
    );
  return parsed as Record<string, string>;
}

export function removeDestination(
  config: IntegrationConfiguration,
  id: string,
): IntegrationConfiguration {
  return {
    ...config,
    destinations: config.destinations.filter((d) => d.id !== id),
    monitors: (config.monitors ?? [])
      .map((m) => ({
        ...m,
        destinationIds: m.destinationIds.filter((value) => value !== id),
      }))
      .filter((m) => m.destinationIds.length > 0),
    // Rules cannot be saved without a destination. The confirmation explains this removal.
    rules: config.rules
      .map((r) => ({
        ...r,
        destinationIds: r.destinationIds.filter((value) => value !== id),
      }))
      .filter((r) => r.destinationIds.length > 0),
  };
}
