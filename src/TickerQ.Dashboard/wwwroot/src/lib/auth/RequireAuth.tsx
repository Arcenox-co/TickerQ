import { Navigate, Outlet, useLocation } from "react-router-dom";
import { Loader2 } from "lucide-react";
import { useAuth } from "./auth-context";

/**
 * Route gate. Renders the protected children only when the user is
 * authenticated OR auth is disabled. While the AuthProvider is still
 * resolving info/refresh, shows a spinner instead of flashing a 401 redirect.
 * On anonymous state, redirects to /login carrying the original path so
 * post-login we can land back where we started. Host-mode setups with a
 * configured login page get a full-page redirect to the HOST app's login
 * (with returnUrl) instead — the SPA's own login form can't help there.
 */
export function RequireAuth() {
  const auth = useAuth();
  const location = useLocation();

  if (auth.status === "loading") {
    return (
      <div className="min-h-screen flex items-center justify-center text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
      </div>
    );
  }

  if (auth.status === "anonymous") {
    const hostLogin = auth.info?.loginRedirect;
    if (hostLogin && !auth.info?.loginAvailable) {
      const returnUrl = window.location.pathname + window.location.search;
      const separator = hostLogin.includes("?") ? "&" : "?";
      window.location.assign(`${hostLogin}${separator}returnUrl=${encodeURIComponent(returnUrl)}`);
      return (
        <div className="min-h-screen flex items-center justify-center text-muted-foreground">
          <Loader2 className="h-5 w-5 animate-spin" />
        </div>
      );
    }
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }

  return <Outlet />;
}
