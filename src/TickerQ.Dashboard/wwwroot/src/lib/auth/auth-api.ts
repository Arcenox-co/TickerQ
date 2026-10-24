import { resolveDashboardUrl } from "@/lib/runtime-config";
import { tokenStore } from "./token-store";

export interface AuthInfo {
  authenticated: boolean;
  username: string | null;
  userId: string | null;
  mode: string;
  enabled: boolean;
  schemes: string[];
  loginAvailable: boolean;
  /** Host-app login page (Host mode). Full-page redirect target when the session expires. */
  loginRedirect: string | null;
}

export interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  username: string;
  userId: string | null;
}

export class AuthApiError extends Error {
  readonly status: number;
  constructor(status: number, message: string) {
    super(message);
    this.name = "AuthApiError";
    this.status = status;
  }
}

export async function fetchAuthInfo(signal?: AbortSignal): Promise<AuthInfo> {
  const token = tokenStore.get();
  const res = await fetch(resolveDashboardUrl("/api/auth/info"), {
    credentials: "include", signal, headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });
  if (!res.ok) throw new AuthApiError(res.status, `auth/info ${res.status}`);
  return (await res.json()) as AuthInfo;
}

export async function login(username: string, password: string): Promise<LoginResponse> {
  const res = await fetch(resolveDashboardUrl("/api/auth/login"), {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    credentials: "include",
    body: JSON.stringify({ username, password }),
  });
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new AuthApiError(res.status, body || `${res.status} ${res.statusText}`);
  }
  return (await res.json()) as LoginResponse;
}

/**
 * Mint a fresh access token using the existing one. Works for both transports:
 * - **Bearer**: sends the in-memory token as <c>Authorization</c>.
 * - **Cookie**: relies on <c>credentials: "include"</c> to send the auth
 *   cookie; the server accepts either source. The response's accessToken is
 *   empty when only cookie mode is active — that's fine, we still get
 *   {username, expiresIn}.
 */
export async function refresh(): Promise<LoginResponse | null> {
  const current = tokenStore.get();
  const headers: Record<string, string> = {};
  if (current) headers.Authorization = `Bearer ${current}`;

  const res = await fetch(resolveDashboardUrl("/api/auth/refresh"), {
    method: "POST",
    headers,
    credentials: "include",
  });
  if (res.status === 401) {
    tokenStore.set(null);
    return null;
  }
  if (!res.ok) throw new AuthApiError(res.status, `auth/refresh ${res.status}`);
  return (await res.json()) as LoginResponse;
}

export async function logout(): Promise<void> {
  const token = tokenStore.get();
  try {
    await fetch(resolveDashboardUrl("/api/auth/logout"), {
      method: "POST",
      headers: token ? { Authorization: `Bearer ${token}` } : undefined,
      credentials: "include",
    });
  } catch {
    /* server-side state may not exist (stateless JWT) — clear local state regardless */
  }
  tokenStore.set(null);
}
