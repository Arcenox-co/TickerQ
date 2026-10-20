import { Suspense, useState, type ReactNode } from "react";
import { Outlet } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { TopBar } from "./TopBar";
import { LicenseBanner } from "./LicenseBanner";
import { RouteLoading } from "./RouteLoading";

export function Shell({ children }: { children?: ReactNode }) {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);

  return (
    <div className="cron-dashboard flex h-screen flex-col overflow-hidden">
      <TopBar sidebarCollapsed={sidebarCollapsed} />
      <LicenseBanner />
      <div className="flex flex-1 overflow-hidden">
        <Sidebar collapsed={sidebarCollapsed} onToggle={() => setSidebarCollapsed((value) => !value)} />
        <main className="relative min-w-0 flex-1 overflow-y-auto scrollbar-thin bg-background">
          <div className="mx-auto p-4 sm:p-6 animate-fade-in">
            <Suspense fallback={<RouteLoading />}>{children ?? <Outlet />}</Suspense>
          </div>
        </main>
      </div>
    </div>
  );
}
