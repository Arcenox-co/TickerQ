export type IntegrationMessageCategory =
  | "jobAlert"
  | "monitoringAlert"
  | "recovery";
export interface IntegrationMessageTemplates {
  jobAlert: string | null;
  monitoringAlert: string | null;
  recovery: string | null;
}
export interface IntegrationPreviewResponse {
  payload: string;
  defaultTemplate: string | null;
}
export interface IntegrationDestination {
  id: string;
  name: string;
  provider: string;
  enabled: boolean;
  url: string | null;
  headers: Record<string, string> | null;
  payloadTemplate: string | null;
  messageTemplates?: IntegrationMessageTemplates | null;
  endpointHost?: string | null;
  hasUrl?: boolean;
  hasHeaders?: boolean;
}
export interface IntegrationRule {
  id: string;
  name: string;
  enabled: boolean;
  destinationIds: string[];
  events: string[];
  includeFunctions: string[];
  excludeFunctions: string[];
  cooldownSeconds: number;
}
export interface IntegrationConfiguration {
  version: string;
  destinations: IntegrationDestination[];
  rules: IntegrationRule[];
  monitors?: IntegrationMonitor[];
}
export interface IntegrationDeliveryStatus {
  destinationId: string;
  atUtc: string;
  outcome: string;
  message: string;
}
export interface IntegrationSnapshot {
  available: boolean;
  canManage: boolean;
  persistent: boolean;
  configuration: IntegrationConfiguration;
  monitoringAvailable?: boolean;
  monitors?: IntegrationMonitorStatus[];
  monitoringPendingDeliveries?: number;
  monitoringCheckIntervalSeconds?: number;
  providers: string[];
  templateTokens: string[];
  recentDeliveries: IntegrationDeliveryStatus[];
  dropped: number;
  suppressed: number;
}

export interface IntegrationMonitor {
  id: string;
  name: string;
  enabled: boolean;
  function: string;
  kind: "deadline" | "long_running" | "consecutive_failures";
  destinationIds: string[];
  notifyRecovery: boolean;
  timeZoneId: string;
  deadlineLocalTime: string;
  graceMinutes: number;
  durationSeconds: number;
  failureThreshold: number;
  lookbackHours: number;
}
export interface IntegrationMonitorStatus {
  monitorId: string;
  status: "waiting" | "healthy" | "incident" | "unknown" | "paused";
  message: string;
  lastCheckedUtc?: string;
  openedAtUtc?: string;
  recoveredAtUtc?: string;
}
