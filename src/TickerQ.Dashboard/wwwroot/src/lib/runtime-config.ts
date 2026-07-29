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
  const trimmed = raw.endsWith("/") ? raw.slice(0, -1) : raw;
  return trimmed.startsWith("/") ? trimmed : `/${trimmed}`;
}
