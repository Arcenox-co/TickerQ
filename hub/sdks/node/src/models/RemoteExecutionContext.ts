import { TickerType } from '../enums';

/**
 * Raw execution context as sent by the TickerQ Scheduler/RemoteExecutor.
 * The Hub serializes with PascalCase.
 * We accept both PascalCase and camelCase via normalization.
 */
export interface RemoteExecutionContext {
    id: string;
    type: TickerType;
    retryCount: number;
    isDue: boolean;
    scheduledFor: string;
    functionName: string;
}

/**
 * Normalizes a parsed JSON object to camelCase keys (one level deep).
 * Handles both PascalCase and camelCase property names.
 */
export function normalizeExecutionContext(raw: Record<string, unknown>): RemoteExecutionContext {
    const get = (camel: string, pascal: string): unknown =>
        raw[camel] !== undefined ? raw[camel] : raw[pascal];

    return {
        id: (get('id', 'Id') as string) ?? '',
        type: (get('type', 'Type') as TickerType) ?? 0,
        retryCount: (get('retryCount', 'RetryCount') as number) ?? 0,
        isDue: (get('isDue', 'IsDue') as boolean) ?? false,
        scheduledFor: (get('scheduledFor', 'ScheduledFor') as string) ?? new Date().toISOString(),
        functionName: (get('functionName', 'FunctionName') as string) ?? '',
    };
}
