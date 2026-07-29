/**
 * Holds the JWT access token in memory + sessionStorage. We avoid localStorage
 * so the token doesn't survive across browser sessions — a refresh is fine,
 * but closing the tab logs out. Listeners are notified on change so the
 * SignalR client and the fetch wrapper see updates instantly.
 */
const STORAGE_KEY = "tickerq.auth.token";

type Listener = (token: string | null) => void;

class TokenStore {
  private token: string | null = null;
  private listeners = new Set<Listener>();

  constructor() {
    try {
      this.token = typeof sessionStorage !== "undefined" ? sessionStorage.getItem(STORAGE_KEY) : null;
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
      if (token) sessionStorage.setItem(STORAGE_KEY, token);
      else sessionStorage.removeItem(STORAGE_KEY);
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
