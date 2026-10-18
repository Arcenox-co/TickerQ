export interface NotificationDestination {
  id: string; name: string; provider: string; enabled: boolean;
  url: string | null; headers: Record<string, string> | null; payloadTemplate: string | null;
  endpointHost?: string | null; hasUrl?: boolean; hasHeaders?: boolean;
}
export interface NotificationRule {
  id: string; name: string; enabled: boolean; destinationIds: string[];
  events: string[]; includeFunctions: string[]; excludeFunctions: string[]; cooldownSeconds: number;
}
export interface NotificationConfiguration { version: string; destinations: NotificationDestination[]; rules: NotificationRule[]; }
export interface NotificationDeliveryStatus { destinationId: string; atUtc: string; outcome: string; message: string; }
export interface NotificationSnapshot {
  available: boolean; canManage: boolean; persistent: boolean; configuration: NotificationConfiguration;
  providers: string[]; templateTokens: string[]; recentDeliveries: NotificationDeliveryStatus[]; dropped: number; suppressed: number;
}
