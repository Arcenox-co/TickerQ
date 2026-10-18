export interface IntegrationDestination {
  id: string; name: string; provider: string; enabled: boolean;
  url: string | null; headers: Record<string, string> | null; payloadTemplate: string | null;
  endpointHost?: string | null; hasUrl?: boolean; hasHeaders?: boolean;
}
export interface IntegrationRule {
  id: string; name: string; enabled: boolean; destinationIds: string[];
  events: string[]; includeFunctions: string[]; excludeFunctions: string[]; cooldownSeconds: number;
}
export interface IntegrationConfiguration { version: string; destinations: IntegrationDestination[]; rules: IntegrationRule[]; }
export interface IntegrationDeliveryStatus { destinationId: string; atUtc: string; outcome: string; message: string; }
export interface IntegrationSnapshot {
  available: boolean; canManage: boolean; persistent: boolean; configuration: IntegrationConfiguration;
  providers: string[]; templateTokens: string[]; recentDeliveries: IntegrationDeliveryStatus[]; dropped: number; suppressed: number;
}
