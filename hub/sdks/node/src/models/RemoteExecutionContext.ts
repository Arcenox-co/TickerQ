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
    /** Invocation-specific scheduler acquisition generation used to fence every status write. */
    acquisitionToken: string;
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
    const acquisitionToken = get('acquisitionToken', 'AcquisitionToken');
    if (typeof acquisitionToken !== 'string' || acquisitionToken.length === 0) {
        throw new TypeError('acquisitionToken is required for fenced execution.');
    }
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(acquisitionToken)) {
        throw new TypeError('acquisitionToken must be a UUID.');
    }
    if (acquisitionToken.toLowerCase() === '00000000-0000-0000-0000-000000000000') {
        throw new TypeError('acquisitionToken must be a non-empty UUID.');
    }
    return {
        id: (get('id', 'Id') as string) ?? '',
        parentId: (get('parentId', 'ParentId') as string | null | undefined) ?? null,
        type: (get('type', 'Type') as TickerType) ?? 0,
        retryCount: (get('retryCount', 'RetryCount') as number) ?? 0,
        isDue: (get('isDue', 'IsDue') as boolean) ?? false,
        scheduledFor: (get('scheduledFor', 'ScheduledFor') as string) ?? new Date().toISOString(),
        functionName: (get('functionName', 'FunctionName') as string) ?? '',
        acquisitionToken,
        ...(rawParentResult === undefined || rawParentResult === null
            ? {}
            : { parentResult: normalizeResultEnvelope(rawParentResult) }),
    };
}
