import { ChevronDown, Eye, KeyRound, LogOut, ShieldCheck } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { useAuth } from "@/lib/auth/auth-context";
import { getRuntimeConfig } from "@/lib/runtime-config";
import { userInitials } from "@/lib/user-initials";

/**
 * Account menu for the top bar. Shows who is signed in, how (the dashboard's own login or a session
 * owned by the host application), and the access level. The dashboard's login signs out through its
 * API; a host session signs out at the host's logout endpoint when one is configured.
 */
export function UserMenu() {
  const auth = useAuth();
  const readOnly = getRuntimeConfig().readOnly;
  const dashboardLogin = auth.info?.loginAvailable === true;
  const hostSession = !dashboardLogin && auth.info?.mode === "host";
  if (auth.status !== "authenticated" || (!dashboardLogin && !hostSession)) return null;

  const name = auth.username ?? "user";
  const hostLogout = hostSession ? auth.info?.logoutRedirect ?? null : null;
  const canSignOut = dashboardLogin || hostLogout !== null;
  const signOut = () => {
    if (hostLogout) {
      auth.markAnonymous();
      window.location.assign(hostLogout);
      return;
    }
    void auth.logout();
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          title={`Signed in as ${name}`}
          className="ml-1 inline-flex items-center gap-2 rounded-full border border-border bg-surface-1 py-0.5 pl-0.5 pr-2 text-xs text-foreground/85 transition-colors hover:bg-surface-2 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring data-[state=open]:bg-surface-2"
        >
          <Avatar size="sm">
            <AvatarFallback className="bg-primary/15 text-[10px] font-semibold text-primary">
              {userInitials(name)}
            </AvatarFallback>
          </Avatar>
          <span className="hidden max-w-32 truncate font-medium sm:block">{name}</span>
          <ChevronDown className="h-3 w-3 text-muted-foreground" />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-64">
        <DropdownMenuLabel className="flex items-center gap-3 px-2 py-2 font-normal">
          <Avatar>
            <AvatarFallback className="bg-primary/15 text-xs font-semibold text-primary">
              {userInitials(name)}
            </AvatarFallback>
          </Avatar>
          <div className="min-w-0">
            <div className="truncate text-sm font-medium text-foreground" title={name}>{name}</div>
            <div className="truncate text-xs text-muted-foreground">
              {hostSession ? "Signed in through the application" : "Signed in to the dashboard"}
            </div>
          </div>
        </DropdownMenuLabel>
        <div className="px-2 pb-2">
          {readOnly ? (
            <span className="inline-flex items-center gap-1.5 rounded-full border border-border px-2 py-0.5 text-[11px] text-muted-foreground">
              <Eye className="h-3 w-3" />
              Read-only access
            </span>
          ) : (
            <span className="inline-flex items-center gap-1.5 rounded-full border border-status-healthy/30 bg-status-healthy/10 px-2 py-0.5 text-[11px] text-status-healthy">
              <ShieldCheck className="h-3 w-3" />
              Full access
            </span>
          )}
        </div>
        <DropdownMenuSeparator />
        {canSignOut ? (
          <DropdownMenuItem variant="destructive" onSelect={signOut}>
            <LogOut />
            Sign out
          </DropdownMenuItem>
        ) : (
          <div className="flex items-start gap-2 px-2 py-1.5 text-xs text-muted-foreground">
            <KeyRound className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            Your session belongs to the application. Sign out there.
          </div>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
