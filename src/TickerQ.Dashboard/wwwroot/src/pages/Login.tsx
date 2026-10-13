import { useState, type FormEvent } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { LogIn, Lock, User, Activity } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/lib/auth/auth-context";
import { AuthApiError } from "@/lib/auth/auth-api";

interface LocationState {
  from?: string;
}

/**
 * Standalone login screen. Rendered when AuthContext.status === "anonymous"
 * AND login is available (JWT or Cookie scheme). After successful login the
 * user lands on the route they were trying to reach (or "/").
 */
export default function LoginPage() {
  const auth = useAuth();
  const location = useLocation();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  if (auth.status === "authenticated" || auth.status === "anonymous-allowed") {
    const target = (location.state as LocationState | null)?.from ?? "/";
    return <Navigate to={target} replace />;
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (!username || !password) {
      setError("Username and password are required.");
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await auth.login(username, password);
    } catch (err) {
      if (err instanceof AuthApiError && err.status === 401) {
        setError("Invalid username or password.");
      } else {
        setError(err instanceof Error ? err.message : "Login failed.");
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-surface-0 p-4">
      <div className="w-full max-w-sm">
        <div className="flex items-center justify-center gap-2 mb-6">
          <Activity className="h-5 w-5 text-primary" />
          <span className="font-semibold text-lg">TickerQ</span>
          <span className="text-muted-foreground text-sm">Scheduler Dashboard</span>
        </div>
        <form
          onSubmit={onSubmit}
          className="rounded-lg border border-border bg-surface-1 p-6 space-y-4 shadow-lg"
        >
          <div>
            <h1 className="text-lg font-semibold">Sign in</h1>
            <p className="text-xs text-muted-foreground mt-0.5">Authenticate to access the dashboard.</p>
          </div>

          <div className="space-y-1.5">
            <label htmlFor="username" className="text-xs font-medium text-muted-foreground flex items-center gap-1.5">
              <User className="h-3 w-3" /> Username
            </label>
            <Input
              id="username"
              type="text"
              autoComplete="username"
              autoFocus
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              disabled={submitting}
            />
          </div>

          <div className="space-y-1.5">
            <label htmlFor="password" className="text-xs font-medium text-muted-foreground flex items-center gap-1.5">
              <Lock className="h-3 w-3" /> Password
            </label>
            <Input
              id="password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={submitting}
            />
          </div>

          {error && (
            <div className="rounded-md border border-status-error/30 bg-status-error/10 text-status-error text-xs px-3 py-2">
              {error}
            </div>
          )}

          <Button type="submit" className="w-full" disabled={submitting}>
            <LogIn className="h-3.5 w-3.5 mr-1.5" />
            {submitting ? "Signing in…" : "Sign in"}
          </Button>
        </form>
      </div>
    </div>
  );
}
