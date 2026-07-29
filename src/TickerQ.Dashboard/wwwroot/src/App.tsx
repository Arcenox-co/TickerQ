import { useEffect } from "react";
import { Route, Routes } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { Shell } from "@/components/layout/Shell";
import OverviewPage from "@/pages/Overview";
import ExecutionsPage from "@/pages/Executions";
import TimeTickersPage from "@/pages/TimeTickers";
import CronTickersPage from "@/pages/CronTickers";
import CronTickerDetailPage from "@/pages/CronTickerDetail";
import ChainBuilderPage from "@/pages/ChainBuilder";
import ChainFlowchartPage from "@/pages/ChainFlowchart";
import AssistantChatPage from "@/pages/AssistantChat";
import LoginPage from "@/pages/Login";
import { startTickerHub } from "@/services/ticker-hub";
import { RequireAuth } from "@/lib/auth/RequireAuth";
import { useAuth } from "@/lib/auth/auth-context";

export default function App() {
  // Boot the SignalR hub once the user is authenticated (or auth is off).
  // Connecting earlier would fail the hub's auth gate; reconnecting on login
  // is cheaper than dealing with a permanently broken hub instance.
  const qc = useQueryClient();
  const auth = useAuth();
  useEffect(() => {
    if (auth.status === "authenticated" || auth.status === "anonymous-allowed") {
      startTickerHub(qc);
    }
  }, [qc, auth.status]);

  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<Shell />}>
          <Route index element={<OverviewPage />} />
          <Route path="executions" element={<ExecutionsPage />} />
          <Route path="time-tickers" element={<TimeTickersPage />} />
          <Route path="time-tickers/new-chain" element={<ChainBuilderPage />} />
          <Route path="time-tickers/:id/flowchart" element={<ChainFlowchartPage />} />
          <Route path="cron-tickers" element={<CronTickersPage />} />
          <Route path="cron-tickers/:id" element={<CronTickerDetailPage />} />
          <Route path="assistant" element={<AssistantChatPage />} />
        </Route>
      </Route>
    </Routes>
  );
}
