import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { tokenStore } from "./token-store";
import {
  fetchAuthInfo,
  login as apiLogin,
  logout as apiLogout,
  refresh as apiRefresh,
  type AuthInfo,
} from "./auth-api";
import { onUnauthenticated } from "./auth-events";
import { assistantStorageKey, clearAssistantTranscript } from "../assistant-storage";
import { resolveDashboardUrl } from "../runtime-config";
import { stopTickerHub } from "@/services/ticker-hub";

type AuthStatus = "loading" | "anonymous-allowed" | "authenticated" | "anonymous";

interface AuthState {
  status: AuthStatus;
  username: string | null;
  userId: string | null;
  info: AuthInfo | null;
}

interface AuthContextValue extends AuthState {
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  /** Force the SPA to switch to the anonymous state — used by the fetch wrapper on 401. */
  markAnonymous: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

const INITIAL: AuthState = { status: "loading", username: null, userId: null, info: null };

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>(INITIAL);

  // Boot: discover auth mode, then validate any persisted token by refreshing.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      let info: AuthInfo;
      try {
        info = await fetchAuthInfo();
      } catch {
        // /api/auth/info should always be reachable; if it isn't, fail closed
        // so the user sees an explicit "needs login" rather than a blank page.
        if (!cancelled) setState({ status: "anonymous", username: null, userId: null, info: null });
        return;
      }

      if (cancelled) return;

      if (!info.enabled) {
        setState({ status: "anonymous-allowed", username: null, userId: null, info });
        return;
      }

      // Host mode: the user is already authenticated by the host pipeline.
      if (!info.loginAvailable) {
        setState({ status: info.authenticated ? "authenticated" : "anonymous", username: info.username, userId: info.userId, info });
        return;
      }

      // JWT/cookie mode: try to revive the previous session via refresh. In
      // bearer mode we only attempt this when a persisted token exists; in
      // cookie mode the browser always sends the cookie automatically, so we
      // try unconditionally and let a 401 mean "no session".
      const hasCookieScheme = info.schemes.includes("cookie");
      if (tokenStore.get() || hasCookieScheme) {
        try {
          const refreshed = await apiRefresh();
          if (cancelled) return;
          if (refreshed) {
            if (refreshed.accessToken) tokenStore.set(refreshed.accessToken);
            setState({ status: "authenticated", username: refreshed.username, userId: refreshed.userId, info });
            return;
          }
        } catch {
          /* fall through to anonymous */
        }
      }

      setState({ status: "anonymous", username: null, userId: null, info });
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const login = useCallback(async (username: string, password: string) => {
    const res = await apiLogin(username, password);
    // Bearer-only setups return a non-empty accessToken; cookie-only setups
    // return "" and rely on the Set-Cookie header sent alongside. Storing the
    // empty string would just confuse the fetch wrapper, so guard it.
    if (res.accessToken) tokenStore.set(res.accessToken);
    setState((prev) => ({
      status: "authenticated",
      username: res.username,
      userId: res.userId,
      info: prev.info,
    }));
  }, []);

  const clearHistory = useCallback(() => {
    try {
      const dashboardUrl = new URL(resolveDashboardUrl("/"), window.location.origin).href;
      clearAssistantTranscript(window.localStorage, assistantStorageKey(state.userId, dashboardUrl));
    } catch { /* Browser storage may be unavailable. */ }
  }, [state.userId]);

  const logout = useCallback(async () => {
    clearHistory();
    await apiLogout();
    await stopTickerHub();
    clearHistory();
    setState((prev) => ({ status: "anonymous", username: null, userId: null, info: prev.info }));
  }, [clearHistory]);

  const markAnonymous = useCallback(() => {
    clearHistory();
    tokenStore.set(null);
    void stopTickerHub();
    setState((prev) => ({ status: "anonymous", username: null, userId: null, info: prev.info }));
  }, [clearHistory]);

  // The fetch wrapper fires `onUnauthenticated` on a final 401 (refresh
  // failed or no token to refresh). Listening here lets cookie-mode sessions
  // — where there's no token store to clear — still route the user to /login.
  useEffect(() => onUnauthenticated(() => markAnonymous()), [markAnonymous]);

  const value = useMemo<AuthContextValue>(
    () => ({ ...state, login, logout, markAnonymous }),
    [state, login, logout, markAnonymous]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>");
  return ctx;
}
