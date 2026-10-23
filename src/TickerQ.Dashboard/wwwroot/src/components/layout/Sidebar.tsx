import { NavLink, useLocation } from "react-router-dom";
import {
  LayoutDashboard,
  ListChecks,
  Clock,
  Timer,
  Sparkles,
  BadgeCheck,
  Plug,
  Info,
  Activity,
  Server,
  CalendarClock,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { WorkspaceLicense } from "./WorkspaceLicense";

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
    label: "Policies",
    items: [
      { path: "/schedule-policies", label: "Schedule Policies", icon: CalendarClock },
    ],
  },
  {
    // Always visible so the feature is discoverable. When no chat client is
    // configured, the page itself explains how to enable it (AddAssistant).
    label: "AI",
    items: [{ path: "/assistant", label: "Chat AI", icon: Sparkles }],
  },
  {
    label: "Clustering",
    items: [{ path: "/nodes", label: "Nodes", icon: Server }],
  },
  {
    label: "System",
    items: [
      { path: "/diagnostics", label: "Diagnostics", icon: Activity },
      // Keep optional integrations discoverable. The page explains how to
      // install and configure TickerQ.Integrations when it is unavailable.
      { path: "/integrations", label: "Integrations", icon: Plug },
    ],
  },
  {
    label: "Information",
    items: [
      { path: "/license", label: "License", icon: BadgeCheck },
      { path: "/about", label: "About", icon: Info },
    ],
  },
];

export function Sidebar({ collapsed, onToggle }: { collapsed: boolean; onToggle: () => void }) {
  const { pathname } = useLocation();

  return (
    <aside
      className={cn(
        "flex flex-col border-r border-border bg-sidebar transition-all duration-200 shrink-0 relative",
        collapsed ? "w-[52px]" : "w-[52px] md:w-52"
      )}
    >
      {!collapsed && (
        <div className="hidden shrink-0 md:block">
          <WorkspaceLicense />
        </div>
      )}
      <nav aria-label="Dashboard" className="flex-1 py-3 px-2 overflow-y-auto scrollbar-thin">
        {navGroups.map((group, gi) => (
          <div
            key={group.label}
            role="group"
            aria-label={group.label}
            className={cn(gi > 0 && "mt-5")}
          >
            {!collapsed && (
              <p className="hidden md:block text-[10px] font-semibold uppercase tracking-[0.1em] text-muted-foreground/50 px-2.5 mb-1.5">
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
                    aria-label={item.label}
                    title={item.label}
                    className={cn(
                      "flex items-center gap-2.5 rounded-lg text-[13px] transition-all duration-150 group relative",
                      collapsed ? "px-0 py-2 justify-center" : "px-0 py-2 justify-center md:px-2.5 md:py-[7px] md:justify-start",
                      isActive
                        ? "bg-primary/8 text-sidebar-foreground font-medium"
                        : "text-muted-foreground hover:text-sidebar-foreground hover:bg-surface-2/60"
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
                          : "text-muted-foreground/70 group-hover:text-sidebar-foreground"
                      )}
                    />
                    {!collapsed && <span className="hidden md:inline">{item.label}</span>}
                  </NavLink>
                );
              })}
            </div>
          </div>
        ))}
      </nav>

      <div className="hidden md:block p-2 border-t border-border">
        <button
          type="button"
          onClick={onToggle}
          className="w-full flex items-center justify-center p-1.5 rounded-lg text-muted-foreground/50 hover:text-sidebar-foreground hover:bg-surface-2 transition-colors"
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
