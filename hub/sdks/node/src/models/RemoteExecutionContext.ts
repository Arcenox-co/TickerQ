import { TickerType } from '../enums';
import { normalizeResultEnvelope, type ResultEnvelope } from './ResultEnvelope';

const MAX_REQUEST_PAYLOAD_BYTES = 1024 * 1024;

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
    /** Unpredictable identity for this exact invocation, used by the signed cancellation channel. */
    executionId: string;
    /** Whether the scheduler loaded a persisted invocation request (distinct from JSON null). */
    hasRequest: boolean;
    /** Parsed persisted invocation request. Present when hasRequest is true, including JSON null. */
    request?: unknown;
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
    const executionId = get('executionId', 'ExecutionId');
    if (typeof executionId !== 'string' ||
        !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(executionId) ||
        executionId.toLowerCase() === '00000000-0000-0000-0000-000000000000') {
        throw new TypeError('executionId must be a non-empty UUID.');
    }
    const rawHasRequest = get('hasRequest', 'HasRequest');
    const hasRequest = rawHasRequest ?? false;
    const requestPayload = get('requestPayload', 'RequestPayload');
    if (typeof hasRequest !== 'boolean') {
        throw new TypeError('hasRequest must be a boolean.');
    }
    let request: unknown;
    if (hasRequest) {
        if (typeof requestPayload !== 'string' || !isCanonicalBase64(requestPayload)) {
            throw new TypeError('requestPayload must be canonical base64 when hasRequest is true.');
        }
        const bytes = Buffer.from(requestPayload, 'base64');
        if (bytes.length > MAX_REQUEST_PAYLOAD_BYTES) {
            throw new TypeError(`requestPayload exceeds the ${MAX_REQUEST_PAYLOAD_BYTES}-byte limit.`);
        }
        try { request = JSON.parse(bytes.toString('utf8')); }
        catch { throw new TypeError('requestPayload must contain exactly one valid JSON value.'); }
    } else if (requestPayload !== undefined && requestPayload !== null) {
        throw new TypeError('requestPayload must be absent or null when hasRequest is false.');
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
        executionId,
        hasRequest,
        ...(hasRequest ? { request } : {}),
        ...(rawParentResult === undefined || rawParentResult === null
            ? {}
            : { parentResult: normalizeResultEnvelope(rawParentResult) }),
    };
}

function isCanonicalBase64(value: string): boolean {
    if (value.length === 0 || value.length % 4 !== 0 || !/^[A-Za-z0-9+/]*={0,2}$/.test(value)) return false;
    return Buffer.from(value, 'base64').toString('base64') === value;
}
