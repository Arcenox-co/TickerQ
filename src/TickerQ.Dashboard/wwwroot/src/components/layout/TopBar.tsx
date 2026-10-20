import { Eye, LogOut, UserCircle2 } from "lucide-react";
import tickerqLogo from "@/assets/tickerq-logo.svg";
import { TimezoneSelector } from "@/components/layout/TimezoneSelector";
import { useAuth } from "@/lib/auth/auth-context";
import { formatApplicationNamespace, formatFriendlyVersion, getRuntimeConfig } from "@/lib/runtime-config";
import { WorkspaceLicense } from "./WorkspaceLicense";

export function TopBar({ sidebarCollapsed = false }: { sidebarCollapsed?: boolean }) {
  const auth = useAuth();
  const cfg = getRuntimeConfig();
  const friendlyVersion = formatFriendlyVersion(cfg.version);
  const applicationNamespace = formatApplicationNamespace(cfg.applicationNamespace);
  const showUser =
    auth.status === "authenticated" && auth.info?.loginAvailable === true;

  return (
    <header className="h-11 flex items-center justify-between gap-2 px-2 sm:px-4 border-b border-border bg-surface-0 shrink-0">
      <div className="flex min-w-0 items-center gap-2 text-sm">
        <div className="inline-flex min-w-0 items-center gap-1.5 rounded-md px-2 py-1">
          {cfg.logoUrl ? (
            <img src={cfg.logoUrl} alt="" className="h-4 w-auto shrink-0" />
          ) : (
            <img src={tickerqLogo} alt="" className="size-8 shrink-0" width={32} height={32} />
          )}
          <span className="truncate font-semibold text-foreground" title={cfg.title}>{cfg.title}</span>
          {friendlyVersion && (
            <span className="hidden sm:inline shrink-0 text-muted-foreground/60 text-[10px]">v{friendlyVersion}</span>
          )}
        </div>
        <span
          title={`Application namespace: ${applicationNamespace}`}
          className="hidden min-w-0 max-w-52 items-center gap-1 rounded-md border border-border/70 bg-surface-1 px-2 py-1 text-[10px] text-muted-foreground md:inline-flex"
        >
          <span className="shrink-0 text-muted-foreground/60">Namespace</span>
          <span className="truncate font-mono text-foreground/80">{applicationNamespace}</span>
        </span>
        <WorkspaceLicense compact className={sidebarCollapsed ? undefined : "md:hidden"} />
        {cfg.readOnly && (
          <span
            title="The dashboard is running in read-only mode — all mutations are disabled."
            className="inline-flex shrink-0 items-center gap-1 rounded-full border border-border px-2 py-0.5 text-[10px] text-muted-foreground"
          >
            <Eye className="h-3 w-3" />
            Read-only
          </span>
        )}
      </div>

      <div className="flex shrink-0 items-center gap-2 text-xs text-muted-foreground">
        <TimezoneSelector />
        <span className="hidden sm:inline-flex items-center gap-1.5">
          <span className="size-1.5 rounded-full bg-status-healthy animate-pulse-dot" />
          Connected
        </span>
        {showUser && (
          <div className="flex items-center gap-1.5 ml-1 pl-2 border-l border-border">
            <UserCircle2 className="hidden sm:block h-3.5 w-3.5 text-muted-foreground/60" />
            <span className="hidden sm:block max-w-32 truncate text-foreground/80">{auth.username ?? "user"}</span>
            <button
              type="button"
              onClick={() => void auth.logout()}
              title="Sign out"
              className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded hover:bg-surface-2 hover:text-foreground transition-colors"
            >
              <LogOut className="h-3 w-3" />
              Sign out
            </button>
          </div>
        )}
      </div>
    </header>
  );
}
