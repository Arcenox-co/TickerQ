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
}

export interface TickerQAuthConfig {
  mode: string;
  enabled: boolean;
  sessionTimeout: number;
  /** Host-app login page (Host mode) for the browser round-trip. */
  loginRedirect?: string | null;
}

export interface TickerQRuntimeConfig {
  integrationsEnabled: boolean;
  basePath: string;
  backendDomain: string;
  /** Dashboard package version (assembly informational version). */
  version: string;
  /** Customer-branded title shown in the shell + browser tab. */
  title: string;
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
  version: "",
  title: "TickerQ Dashboard",
  logoUrl: null,
  readOnly: false,
  timezone: null,
  realtime: { enabled: true, hubPath: "tickerq-notification-hub" },
  assistant: { enabled: false, model: "", history: false },
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
