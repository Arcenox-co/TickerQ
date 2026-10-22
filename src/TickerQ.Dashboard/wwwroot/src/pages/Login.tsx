import { useState, type FormEvent } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { ExternalLink, LogIn, Lock, User } from "lucide-react";
import tickerqLogo from "@/assets/tickerq-logo.svg";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/lib/auth/auth-context";
import { AuthApiError } from "@/lib/auth/auth-api";
import { formatApplicationNamespace, getRuntimeConfig } from "@/lib/runtime-config";

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
  const config = getRuntimeConfig();
  const environmentName = config.environmentName.trim();
  const applicationNamespace = formatApplicationNamespace(config.applicationNamespace);
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
          <img src={tickerqLogo} alt="" className="size-10 shrink-0" width={40} height={40} />
          <span className="font-semibold text-lg">TickerQ</span>
          <span className="text-muted-foreground text-sm">Scheduler Dashboard</span>
        </div>
        <form
          onSubmit={onSubmit}
          className="rounded-lg border border-border bg-surface-1 p-6 space-y-4 shadow-lg"
        >
          <div>
            <h1 className="flex items-baseline gap-2 text-lg font-semibold">
              <span>Sign in</span>
              {environmentName && (
                <>
                  <span className="text-xs font-normal text-muted-foreground/50" aria-hidden="true">
                    ·
                  </span>
                  <span
                    className="truncate text-xs font-medium text-primary"
                    title={`Environment: ${environmentName}`}
                  >
                    {environmentName}
                  </span>
                </>
              )}
            </h1>
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

        <footer className="mt-5" aria-label="Dashboard environment">
          <div className="flex items-center gap-2 rounded-lg border border-border/70 bg-surface-1/40 px-3.5 py-2.5 text-left">
            <span
              className="min-w-0 flex-1 truncate text-xs font-medium text-foreground/85"
              title={config.title}
            >
              {config.title}
            </span>
            <span className="shrink-0 text-[10px] text-muted-foreground/40" aria-hidden="true">
              ·
            </span>
            <span className="flex min-w-0 shrink items-center gap-1.5 text-[10px] text-muted-foreground">
              <span className="shrink-0">Namespace</span>
              <span
                className="max-w-36 truncate font-mono text-foreground/60"
                title={applicationNamespace}
              >
                {applicationNamespace}
              </span>
            </span>
          </div>
          <div className="mt-4 flex items-center justify-center gap-2 text-[10px] text-muted-foreground/65">
            <span>© {new Date().getFullYear()} Arcenox</span>
            <span aria-hidden="true">·</span>
            <a
              href="https://tickerq.net/docs/5.x"
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center gap-1 transition-colors hover:text-foreground"
            >
              Documentation
              <ExternalLink className="size-2.5" aria-hidden="true" />
            </a>
          </div>
        </footer>
      </div>
    </div>
  );
}
