import { resolveDashboardUrl } from "../runtime-config.ts";

/**
 * Holds the JWT access token in memory + sessionStorage. We avoid localStorage
 * so the token doesn't survive across browser sessions — a refresh is fine,
 * but closing the tab logs out. Listeners are notified on change so the
 * SignalR client and the fetch wrapper see updates instantly.
 */
const LEGACY_STORAGE_KEY = "tickerq.auth.token";

type Listener = (token: string | null) => void;

export class TokenStore {
  private token: string | null = null;
  private listeners = new Set<Listener>();
  private storageKey: string | null = null;

  constructor() {
    try {
      this.storageKey = `tickerq.auth.token:v2:${encodeURIComponent(resolveDashboardUrl("/"))}`;
      if (typeof sessionStorage !== "undefined") {
        // An old unscoped credential has no provable backend owner. Require a fresh login.
        sessionStorage.removeItem(LEGACY_STORAGE_KEY);
        this.token = sessionStorage.getItem(this.storageKey);
      }
    } catch {
      this.token = null;
    }
  }

  get(): string | null {
    return this.token;
  }

  set(token: string | null): void {
    this.token = token;
    try {
      if (this.storageKey) {
        if (token) sessionStorage.setItem(this.storageKey, token);
        else sessionStorage.removeItem(this.storageKey);
      }
    } catch {
      /* sessionStorage unavailable — in-memory only, OK */
    }
    for (const l of this.listeners) l(token);
  }

  subscribe(listener: Listener): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }
}

export const tokenStore = new TokenStore();
