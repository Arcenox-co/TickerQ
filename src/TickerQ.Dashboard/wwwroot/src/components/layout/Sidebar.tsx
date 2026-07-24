import { useState } from "react";
import { NavLink, useLocation } from "react-router-dom";
import {
  LayoutDashboard,
  ListChecks,
  Clock,
  Timer,
  Sparkles,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { cn } from "@/lib/utils";

interface NavItem {
  path: string;
  label: string;
  icon: typeof LayoutDashboard;
  end?: boolean;
}

const navGroups: { label: string; items: NavItem[] }[] = [
  {
    label: "Monitor",
    items: [
      { path: "/", label: "Overview", icon: LayoutDashboard, end: true },
      { path: "/executions", label: "Executions", icon: ListChecks },
    ],
  },
  {
    label: "Scheduling",
    items: [
      { path: "/time-tickers", label: "Time Tickers", icon: Clock },
      { path: "/cron-tickers", label: "Cron Tickers", icon: Timer },
    ],
  },
  {
    // Always visible so the feature is discoverable. When no chat client is
    // configured, the page itself explains how to enable it (AddAssistant).
    label: "AI",
    items: [{ path: "/assistant", label: "Chat AI", icon: Sparkles }],
  },
];

export function Sidebar() {
  const [collapsed, setCollapsed] = useState(false);
  const { pathname } = useLocation();

  return (
    <aside
      className={cn(
        "flex flex-col border-r border-border bg-sidebar transition-all duration-200 shrink-0 relative",
        collapsed ? "w-[52px]" : "w-52"
      )}
    >
      <nav className="flex-1 py-3 px-2 overflow-y-auto scrollbar-thin">
        {navGroups.map((group, gi) => (
          <div key={group.label} className={cn(gi > 0 && "mt-5")}>
            {!collapsed && (
              <p className="text-[10px] font-semibold uppercase tracking-[0.1em] text-muted-foreground/50 px-2.5 mb-1.5">
                {group.label}
              </p>
            )}
            <div className="space-y-0.5">
              {group.items.map((item) => {
                const isActive = item.end
                  ? pathname === item.path
                  : pathname.startsWith(item.path);
                return (
                  <NavLink
                    key={item.path}
                    to={item.path}
                    end={item.end}
                    className={cn(
                      "flex items-center gap-2.5 rounded-lg text-[13px] transition-all duration-150 group relative",
                      collapsed ? "px-0 py-2 justify-center" : "px-2.5 py-[7px]",
                      isActive
                        ? "bg-primary/8 text-foreground font-medium"
                        : "text-muted-foreground hover:text-foreground hover:bg-surface-2/60"
                    )}
                  >
                    {isActive && (
                      <div className="absolute left-0 top-1/2 -translate-y-1/2 w-[2px] h-4 rounded-r-full bg-primary" />
                    )}
                    <item.icon
                      className={cn(
                        "h-[15px] w-[15px] shrink-0 transition-colors",
                        isActive
                          ? "text-primary"
                          : "text-muted-foreground/70 group-hover:text-foreground"
                      )}
                    />
                    {!collapsed && <span>{item.label}</span>}
                  </NavLink>
                );
              })}
            </div>
          </div>
        ))}
      </nav>

      <div className="p-2 border-t border-border">
        <button
          type="button"
          onClick={() => setCollapsed((c) => !c)}
          className="w-full flex items-center justify-center p-1.5 rounded-lg text-muted-foreground/50 hover:text-foreground hover:bg-surface-2 transition-colors"
          aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
        >
          {collapsed ? (
            <ChevronRight className="h-3.5 w-3.5" />
          ) : (
            <ChevronLeft className="h-3.5 w-3.5" />
          )}
        </button>
      </div>
    </aside>
  );
}
