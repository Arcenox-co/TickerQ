import { Activity, Eye, LogOut, UserCircle2 } from "lucide-react";
import { TimezoneSelector } from "@/components/layout/TimezoneSelector";
import { useAuth } from "@/lib/auth/auth-context";
import { getRuntimeConfig } from "@/lib/runtime-config";

export function TopBar() {
  const auth = useAuth();
  const cfg = getRuntimeConfig();
  const showUser =
    auth.status === "authenticated" && auth.info?.loginAvailable === true;

  return (
    <header className="h-11 flex items-center justify-between px-4 border-b border-border bg-surface-0 shrink-0">
      <div className="flex items-center gap-2 text-sm">
        <div className="inline-flex items-center gap-1.5 rounded-md px-2 py-1">
          {cfg.logoUrl ? (
            <img src={cfg.logoUrl} alt="" className="h-4 w-auto" />
          ) : (
            <Activity className="h-3.5 w-3.5 text-primary" />
          )}
          <span className="font-semibold text-foreground">{cfg.title}</span>
          {cfg.version && (
            <span className="text-muted-foreground/60 text-[10px]">v{cfg.version}</span>
          )}
        </div>
        {cfg.readOnly && (
          <span
            title="The dashboard is running in read-only mode — all mutations are disabled."
            className="inline-flex items-center gap-1 rounded-full border border-border px-2 py-0.5 text-[10px] text-muted-foreground"
          >
            <Eye className="h-3 w-3" />
            Read-only
          </span>
        )}
      </div>

      <div className="flex items-center gap-2 text-xs text-muted-foreground">
        <TimezoneSelector />
        <span className="inline-flex items-center gap-1.5">
          <span className="size-1.5 rounded-full bg-status-healthy animate-pulse-dot" />
          Connected
        </span>
        {showUser && (
          <div className="flex items-center gap-1.5 ml-1 pl-2 border-l border-border">
            <UserCircle2 className="h-3.5 w-3.5 text-muted-foreground/60" />
            <span className="text-foreground/80">{auth.username ?? "user"}</span>
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
