import { TickerType } from '../enums';
import { normalizeResultEnvelope, type ResultEnvelope } from './ResultEnvelope';

/**
 * Raw execution context as sent by the TickerQ Scheduler/RemoteExecutor.
 * The Hub serializes with PascalCase.
 * We accept both PascalCase and camelCase via normalization.
 */
export interface RemoteExecutionContext {
    id: string;
    parentId: string | null;
    type: TickerType;
    retryCount: number;
    isDue: boolean;
    scheduledFor: string;
    functionName: string;
    /** Result produced by the direct parent execution, when present. */
    parentResult?: ResultEnvelope;
}

/**
 * Normalizes a parsed JSON object to camelCase keys (one level deep).
 * Handles both PascalCase and camelCase property names.
 */
export function normalizeExecutionContext(raw: Record<string, unknown>): RemoteExecutionContext {
    const get = (camel: string, pascal: string): unknown =>
        raw[camel] !== undefined ? raw[camel] : raw[pascal];

    const rawParentResult = get('parentResult', 'ParentResult');
    return {
        id: (get('id', 'Id') as string) ?? '',
        parentId: (get('parentId', 'ParentId') as string | null | undefined) ?? null,
        type: (get('type', 'Type') as TickerType) ?? 0,
        retryCount: (get('retryCount', 'RetryCount') as number) ?? 0,
        isDue: (get('isDue', 'IsDue') as boolean) ?? false,
        scheduledFor: (get('scheduledFor', 'ScheduledFor') as string) ?? new Date().toISOString(),
        functionName: (get('functionName', 'FunctionName') as string) ?? '',
        ...(rawParentResult === undefined || rawParentResult === null
            ? {}
            : { parentResult: normalizeResultEnvelope(rawParentResult) }),
    };
}
