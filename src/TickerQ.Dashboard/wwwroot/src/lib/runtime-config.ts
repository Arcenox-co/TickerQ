export interface TickerQRealtimeConfig {
  enabled: boolean;
  /** Hub route relative to the dashboard base path, e.g. "tickerq-notification-hub". */
  hubPath: string;
}

export interface TickerQAssistantConfig {
  /** True only when the operator configured a chat client. Gates the whole chat UI. */
  enabled: boolean;
  /** Cosmetic model label for the chat panel header. */
  model: string;
  /** True when the server persists per-user chat history (IAssistantHistoryStore registered). */
  history: boolean;
  /** True when structured investigations are persisted by the server. */
  investigations: boolean;
  /** True when the host opted in to background investigation of terminal failures. */
  automaticInvestigations: boolean;
  /** True when bounded source context is configured server-side. */
  sourceContext: boolean;
}

export interface TickerQAuthConfig {
  mode: string;
  enabled: boolean;
  /** Host-app login page (Host mode) for the browser round-trip. */
  loginRedirect?: string | null;
}

export interface TickerQRuntimeConfig {
  integrationsEnabled: boolean;
  basePath: string;
  backendDomain: string;
  /** Application namespace that isolates jobs and cluster state. Null is legacy queue-only mode. */
  applicationNamespace: string | null;
  /** Dashboard package version (assembly informational version). */
  version: string;
  /** Customer-branded title shown in the shell + browser tab. */
  title: string;
  /** Human-facing deployment environment, such as Development, Staging, or Production. */
  environmentName: string;
  /** Optional customer logo URL rendered next to the title. */
  logoUrl: string | null;
  /** When true, all mutation UI is hidden (the server also rejects writes with 403). */
  readOnly: boolean;
  /** Server-configured default rendering time zone (IANA id). Null → browser zone. */
  timezone: string | null;
  realtime: TickerQRealtimeConfig;
  assistant: TickerQAssistantConfig;
  auth: TickerQAuthConfig | null;
}

declare global {
  interface Window {
    TickerQConfig?: Partial<TickerQRuntimeConfig>;
  }
}

const DEFAULTS: TickerQRuntimeConfig = {
  integrationsEnabled: false,
  basePath: "/",
  backendDomain: "",
  applicationNamespace: null,
  version: "",
  title: "TickerQ Dashboard",
  environmentName: "",
  logoUrl: null,
  readOnly: false,
  timezone: null,
  realtime: { enabled: true, hubPath: "tickerq-notification-hub" },
  assistant: { enabled: false, model: "", history: false, investigations: false, automaticInvestigations: false, sourceContext: false },
  auth: null,
};

export function getRuntimeConfig(): TickerQRuntimeConfig {
  const cfg = (typeof window !== "undefined" && window.TickerQConfig) || {};
  return {
    ...DEFAULTS,
    ...cfg,
    realtime: { ...DEFAULTS.realtime, ...(cfg.realtime ?? {}) },
    assistant: { ...DEFAULTS.assistant, ...(cfg.assistant ?? {}) },
  };
}

/** Removes source-control build metadata while preserving the package/pre-release version. */
export function formatFriendlyVersion(version: string | null | undefined): string {
  const value = version?.trim() ?? "";
  const metadataIndex = value.indexOf("+");
  return metadataIndex > 0 ? value.slice(0, metadataIndex) : value;
}

/** Human-readable namespace label used consistently across the dashboard. */
export function formatApplicationNamespace(value: string | null | undefined): string {
  return value?.trim() || "Legacy global";
}

/** True when the operator enabled the AI assistant (a chat client is configured). */
export function isAssistantEnabled(): boolean {
  return getRuntimeConfig().assistant.enabled === true;
}

/** True when the dashboard runs in observability-only mode. */
export function isReadOnly(): boolean {
  return getRuntimeConfig().readOnly === true;
}

export function normalizeBasePath(raw: string): string {
  if (!raw || raw === "/") return "/";
  return `/${raw.replace(/^\/+|\/+$/g, "")}`;
}

/** One destination policy for REST, auth, assistant streams and SignalR. */
export function resolveDashboardUrl(
  path: string,
  config: Pick<TickerQRuntimeConfig, "basePath" | "backendDomain"> = getRuntimeConfig(),
): string {
  if (!path.startsWith("/") || path.startsWith("//") || path.includes("\\"))
    throw new Error("Dashboard endpoint must be a relative route.");
  const root = normalizeBasePath(config.basePath);
  const route = `${root === "/" ? "" : root}${path}`;
  if (!config.backendDomain?.trim()) return route;
  const origin = new URL(config.backendDomain.trim());
  if (!["http:", "https:"].includes(origin.protocol) || origin.username || origin.password ||
      origin.pathname !== "/" || origin.search || origin.hash)
    throw new Error("Dashboard backendDomain must be an HTTP(S) origin.");
  if (typeof window !== "undefined" && window.location.protocol === "https:" && origin.protocol !== "https:")
    throw new Error("An HTTPS dashboard requires an HTTPS backend.");
  return `${origin.origin}${route}`;
}
