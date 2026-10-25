import { useState, type FormEvent } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { ExternalLink, KeyRound, LogIn, Lock, RotateCw, User } from "lucide-react";
import tickerqLogo from "@/assets/tickerq-logo.svg";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/lib/auth/auth-context";
import { AuthApiError } from "@/lib/auth/auth-api";
import { formatApplicationNamespace, getRuntimeConfig, resolveDashboardUrl } from "@/lib/runtime-config";

interface LocationState {
  from?: string;
}

/**
 * Standalone login screen. With the dashboard's own JWT or cookie login it shows the sign-in form, and
 * after signing in the user lands on the route they were trying to reach (or "/"). Other modes cannot
 * sign anyone in here: host authentication sends the user to the application's sign-in, and Basic,
 * API-key, or custom modes expect credentials with every request, so the screen explains that instead.
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

  const signInElsewhere = auth.info != null && !auth.info.loginAvailable;
  const hostSignIn = auth.info?.mode === "host";

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
        {signInElsewhere ? (
          <div className="rounded-lg border border-border bg-surface-1 p-6 space-y-4 shadow-lg">
            <div>
              <h1 className="flex items-center gap-2 text-lg font-semibold">
                <KeyRound className="h-4 w-4 text-primary" />
                {hostSignIn ? "Sign in through the application" : "Credentials required"}
              </h1>
              <p className="text-xs text-muted-foreground mt-1.5 leading-relaxed">
                {hostSignIn
                  ? "This dashboard uses the sign-in of the application that hosts it. Sign in there and you will come back here."
                  : "This dashboard has no sign-in form. It accepts credentials sent with each request, such as HTTP Basic or an API key. Ask the person who runs this application for access."}
              </p>
            </div>
            {hostSignIn && (
              <Button
                type="button"
                className="w-full"
                onClick={() => window.location.assign(resolveDashboardUrl("/auth/challenge"))}
              >
                <LogIn className="h-3.5 w-3.5 mr-1.5" />
                Sign in
              </Button>
            )}
            <Button
              type="button"
              variant="outline"
              className="w-full"
              onClick={() => window.location.reload()}
            >
              <RotateCw className="h-3.5 w-3.5 mr-1.5" />
              Try again
            </Button>
          </div>
        ) : (
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
        )}

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
