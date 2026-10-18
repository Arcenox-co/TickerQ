import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import type { QueryClient } from "@tanstack/react-query";

import { getRuntimeConfig, normalizeBasePath } from "@/lib/runtime-config";
import { tokenStore } from "@/lib/auth/token-store";
import { CronGroups } from "./cron-groups";

/**
 * SignalR client for the TickerQ notification hub. The backend already
 * broadcasts every meaningful change — adds / updates / removes of time
 * tickers, cron tickers, occurrences, host status, etc. We subscribe to those
 * events and trigger React Query invalidation so every list refreshes itself
 * live without polling.
 *
 * Singleton: the hub is connected once for the lifetime of the SPA. Components
 * that need group-scoped events (cron occurrence updates fire to a Group keyed
 * by cronTickerId) call `joinCronGroup` on mount and `leaveCronGroup` on unmount.
 */
let connection: HubConnection | null = null;
const cronGroups = new CronGroups();
let retryTimer: ReturnType<typeof setTimeout> | undefined;

function hubUrl(): string {
  const cfg = getRuntimeConfig();
  const basePath = normalizeBasePath(cfg.basePath);
  const prefix = basePath === "/" ? "" : basePath;
  // The hub route comes from the server's runtime config so backend route
  // changes can never strand the SPA.
  const hubPath = cfg.realtime.hubPath.replace(/^\//, "");
  return `${prefix}/${hubPath}`;
}

export function startTickerHub(qc: QueryClient): HubConnection | null {
  if (!getRuntimeConfig().realtime.enabled) return null;
  if (connection) return connection;

  const conn = new HubConnectionBuilder()
    .withUrl(hubUrl(), {
      // SignalR sends this as ?access_token=… on the WebSocket upgrade —
      // matches what AuthService.GetAuthorizationValue reads server-side.
      // For cookie / no-auth setups the factory just returns "" and the hub
      // ignores it.
      accessTokenFactory: () => tokenStore.get() ?? "",
      withCredentials: true,
    })
    .withAutomaticReconnect([0, 1000, 2000, 5000, 10_000, 30_000])
    .configureLogging(LogLevel.Warning)
    .build();

  // ---- Invalidation helpers ----
  const inv = (...keys: readonly (readonly unknown[])[]) => {
    for (const k of keys) qc.invalidateQueries({ queryKey: k });
  };
  // Anything time-ticker related: list, detail panel, executions feed,
  // overview tiles + tables, stats counts, 7d graphs, host's next-ticker.
  const invalidateTimeAll = () =>
    inv(
      ["time-tickers"],
      // Child changes update aggregate chain progress and inherited lock holders.
      ["cron-occurrences"],
      ["cron-tickers"],
      ["executions"],
      ["overview"],
      ["stats"],
      ["graphs"],
      ["host", "next-ticker"]
    );
  // Cron mutations affect: list, detail page, stats, graphs.
  const invalidateCronAll = () =>
    inv(["cron-tickers"], ["stats"], ["overview"], ["graphs"]);
  // Occurrences live under a cron, but they also flow into the executions
  // feed and the overview's recent activity card.
  const invalidateOccAll = () =>
    inv(
      ["cron-occurrences"],
      ["cron-tickers"],
      ["executions"],
      ["overview"],
      ["stats"],
      ["graphs"]
    );

  // ---- Time tickers ----
  conn.on("AddTimeTickerNotification", invalidateTimeAll);
  conn.on("AddTimeTickersBatchNotification", invalidateTimeAll);
  conn.on("UpdateTimeTickerNotification", invalidateTimeAll);
  conn.on("RemoveTimeTickerNotification", invalidateTimeAll);
  conn.on("CanceledTickerNotification", () => {
    invalidateTimeAll();
    invalidateOccAll();
  });

  // ---- Cron tickers ----
  conn.on("AddCronTickerNotification", invalidateCronAll);
  conn.on("UpdateCronTickerNotification", invalidateCronAll);
  conn.on("RemoveCronTickerNotification", invalidateCronAll);

  // ---- Cron occurrences (group-scoped server-side; SPA receives only after
  // JoinGroup(cronTickerId)) ----
  conn.on("AddCronOccurrenceNotification", invalidateOccAll);
  conn.on("UpdateCronOccurrenceNotification", invalidateOccAll);
  // Cover changes between the initial REST read and joining the live group.
  conn.on("GroupJoined", invalidateOccAll);

  // ---- Host status / scheduler health ----
  conn.on("GetHostStatusNotification", () => inv(["host", "status"]));
  conn.on("GetActiveThreadsNotification", () => inv(["host", "status"]));
  conn.on("GetNextOccurrenceNotification", () => inv(["host", "next-ticker"]));
  conn.on("UpdateHostExceptionNotification", () => inv(["host", "status"]));

  const restoreSubscriptions = async () => {
    if (connection !== conn) return;
    // SignalR assigns a new connection after reconnect; group membership is lost.
    await cronGroups.connect(conn).catch((err) => {
      console.warn("[TickerQ hub] failed to restore cron subscriptions:", err);
    });
    if (connection !== conn || conn.state !== HubConnectionState.Connected) return;
    invalidateTimeAll();
    invalidateCronAll();
    invalidateOccAll();
    inv(["host", "status"], ["host", "next-ticker"]);
  };

  const scheduleStart = () => {
    if (connection !== conn || retryTimer !== undefined) return;
    retryTimer = setTimeout(() => {
      retryTimer = undefined;
      if (connection === conn && conn.state === HubConnectionState.Disconnected) {
        void startConnection();
      }
    }, 5_000);
  };
  const startConnection = async () => {
    try {
      await conn.start();
      await restoreSubscriptions();
    } catch (err) {
      if (connection !== conn) return;
      // Automatic reconnect does not retry a failed initial start.
      console.warn("[TickerQ hub] failed to start:", err);
      scheduleStart();
    }
  };

  conn.onreconnecting(() => {
    if (connection === conn) cronGroups.disconnect();
  });
  conn.onreconnected(restoreSubscriptions);
  conn.onclose(() => {
    if (connection !== conn) return;
    cronGroups.disconnect();
    // Also recover after the automatic reconnect attempts have been exhausted.
    scheduleStart();
  });

  connection = conn;
  void startConnection();
  return conn;
}

export function getTickerHub(): HubConnection | null {
  return connection;
}

/**
 * Tear down the hub connection — used on logout so the next login can start a
 * fresh connection using the new bearer token. (The hub is a singleton; a
 * stopped instance won't auto-reconnect.)
 */
export async function stopTickerHub(): Promise<void> {
  if (!connection) return;
  const conn = connection;
  connection = null;
  clearTimeout(retryTimer);
  retryTimer = undefined;
  cronGroups.disconnect();
  try {
    await conn.stop();
  } catch {
    /* already stopped, ignore */
  }
}

/** Subscribe to a cron's occurrence broadcasts (server fires to a Group keyed by cronTickerId). */
export function joinCronGroup(cronTickerId: string): Promise<void> {
  return cronGroups.join(cronTickerId).catch((err) => {
    console.warn("[TickerQ hub] failed to join cron group:", err);
  });
}

export function leaveCronGroup(cronTickerId: string): Promise<void> {
  return cronGroups.leave(cronTickerId).catch((err) => {
    console.warn("[TickerQ hub] failed to leave cron group:", err);
  });
}
