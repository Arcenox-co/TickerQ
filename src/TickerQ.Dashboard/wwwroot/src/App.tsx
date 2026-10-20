import { lazy, Suspense, useEffect } from "react";
import { Route, Routes } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { Shell } from "@/components/layout/Shell";
import { startTickerHub } from "@/services/ticker-hub";
import { RequireAuth } from "@/lib/auth/RequireAuth";
import { useAuth } from "@/lib/auth/auth-context";

import { RouteLoading } from "@/components/layout/RouteLoading";

const OverviewPage = lazy(() => import("@/pages/Overview"));
const ExecutionsPage = lazy(() => import("@/pages/Executions"));
const TimeTickersPage = lazy(() => import("@/pages/TimeTickers"));
const CronTickersPage = lazy(() => import("@/pages/CronTickers"));
const CronTickerDetailPage = lazy(() => import("@/pages/CronTickerDetail"));
const ChainBuilderPage = lazy(() => import("@/pages/ChainBuilder"));
const ChainFlowchartPage = lazy(() => import("@/pages/ChainFlowchart"));
const CronOccurrenceChainPage = lazy(() => import("@/pages/CronOccurrenceChain"));
const AssistantChatPage = lazy(() => import("@/pages/AssistantChat"));
const IntegrationsPage = lazy(() => import("@/pages/Integrations"));
const LicensePage = lazy(() => import("@/pages/License"));
const AboutPage = lazy(() => import("@/pages/About"));
const DiagnosticsPage = lazy(() => import("@/pages/Diagnostics"));
const LoginPage = lazy(() => import("@/pages/Login"));

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
      <Route path="/login" element={<Suspense fallback={<RouteLoading />}><LoginPage /></Suspense>} />
      <Route element={<RequireAuth />}>
        <Route element={<Shell />}>
          <Route index element={<OverviewPage />} />
          <Route path="executions" element={<ExecutionsPage />} />
          <Route path="time-tickers" element={<TimeTickersPage />} />
          <Route path="time-tickers/new-chain" element={<ChainBuilderPage />} />
          <Route path="time-tickers/:id/flowchart" element={<ChainFlowchartPage />} />
          <Route path="cron-tickers" element={<CronTickersPage />} />
          <Route path="cron-tickers/new-chain" element={<ChainBuilderPage />} />
          <Route path="cron-tickers/:id" element={<CronTickerDetailPage />} />
          <Route path="cron-tickers/:id/occurrences/:occurrenceId/flowchart" element={<CronOccurrenceChainPage />} />
          <Route path="assistant" element={<AssistantChatPage />} />
          <Route path="license" element={<LicensePage />} />
          <Route path="integrations" element={<IntegrationsPage />} />
          <Route path="about" element={<AboutPage />} />
          <Route path="diagnostics" element={<DiagnosticsPage />} />
        </Route>
      </Route>
    </Routes>
  );
}
