import type { ReactNode } from "react";
import { Outlet } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { TopBar } from "./TopBar";

export function Shell({ children }: { children?: ReactNode }) {
  return (
    <div className="cron-dashboard flex h-screen flex-col overflow-hidden">
      <TopBar />
      <div className="flex flex-1 overflow-hidden">
        <Sidebar />
        <main className="relative flex-1 overflow-y-auto scrollbar-thin bg-background">
          <div className="mx-auto p-6 animate-fade-in">
            {children ?? <Outlet />}
          </div>
        </main>
      </div>
    </div>
  );
}
