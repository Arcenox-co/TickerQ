export const RESULT_ENVELOPE_VERSION = 1;
/** Symmetric limit for decoded inbound parent results and outbound function results. */
export const MAX_RESULT_PAYLOAD_BYTES = 1024 * 1024;

/** JSON-safe transport envelope for a function result. */
export interface ResultEnvelope {
    envelopeVersion: number;
    mediaType: string;
    contractId?: string;
    contractType?: string;
    /** Base64-encoded payload bytes. Presence is distinct from no envelope. */
    payload: string;
}

export function createJsonResultEnvelope(
    value: unknown,
    contract?: { contractId?: string; contractType?: string; mediaType?: string },
): ResultEnvelope {
    const json = JSON.stringify(value);
    if (json === undefined) {
        throw new Error('TickerQ: result value is not JSON serializable.');
    }
    const bytes = Buffer.from(json, 'utf8');
    if (bytes.length > MAX_RESULT_PAYLOAD_BYTES) {
        throw new Error(`TickerQ: result payload exceeds the ${MAX_RESULT_PAYLOAD_BYTES}-byte limit.`);
    }
    return {
        envelopeVersion: RESULT_ENVELOPE_VERSION,
        mediaType: contract?.mediaType ?? 'application/json',
        ...(contract?.contractId ? { contractId: contract.contractId } : {}),
        ...(contract?.contractType ? { contractType: contract.contractType } : {}),
        payload: bytes.toString('base64'),
    };
}

export function normalizeResultEnvelope(raw: unknown): ResultEnvelope {
    if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) {
        throw new Error('TickerQ: parent result envelope must be an object.');
    }
    const value = raw as Record<string, unknown>;
    const get = (camel: string, pascal: string): unknown =>
        value[camel] !== undefined ? value[camel] : value[pascal];
    const envelopeVersion = get('envelopeVersion', 'EnvelopeVersion');
    if (!Number.isInteger(envelopeVersion) || (envelopeVersion as number) <= 0) {
        throw new Error('TickerQ: result envelope version must be a positive integer.');
    }
    if (envelopeVersion !== RESULT_ENVELOPE_VERSION) {
        throw new Error(`TickerQ: unsupported result envelope version '${String(envelopeVersion)}'.`);
    }
    const mediaType = get('mediaType', 'MediaType');
    if (typeof mediaType !== 'string' || mediaType.trim().length === 0) {
        throw new Error('TickerQ: result envelope mediaType is required.');
    }
    const payload = get('payload', 'Payload');
    if (typeof payload !== 'string' || !isCanonicalBase64(payload)) {
        throw new Error('TickerQ: result envelope payload must be valid base64.');
    }
    const bytes = Buffer.from(payload, 'base64');
    if (bytes.length > MAX_RESULT_PAYLOAD_BYTES) {
        throw new Error(`TickerQ: result payload exceeds the ${MAX_RESULT_PAYLOAD_BYTES}-byte limit.`);
    }
    const contractId = get('contractId', 'ContractId');
    const contractType = get('contractType', 'ContractType');
    if (contractId !== undefined && contractId !== null
        && (typeof contractId !== 'string' || contractId.trim().length === 0)) {
        throw new Error('TickerQ: result envelope contractId must be a non-blank string when present.');
    }
    if (contractType !== undefined && contractType !== null
        && (typeof contractType !== 'string' || contractType.trim().length === 0)) {
        throw new Error('TickerQ: result envelope contractType must be a non-blank string when present.');
    }
    return {
        envelopeVersion: envelopeVersion as number,
        mediaType,
        ...(typeof contractId === 'string' ? { contractId } : {}),
        ...(typeof contractType === 'string' ? { contractType } : {}),
        payload,
    };
}

export function deserializeJsonResult<T>(envelope: ResultEnvelope): T {
    const mediaType = envelope.mediaType.toLowerCase().split(';', 1)[0].trim();
    if (mediaType !== 'application/json' && !mediaType.endsWith('+json')) {
        throw new Error(`TickerQ: cannot deserialize parent result media type '${envelope.mediaType}' as JSON.`);
    }
    return JSON.parse(Buffer.from(envelope.payload, 'base64').toString('utf8')) as T;
}

function isCanonicalBase64(value: string): boolean {
    if (value.length % 4 !== 0 || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(value)) {
        return false;
    }
    return Buffer.from(value, 'base64').toString('base64') === value;
}
