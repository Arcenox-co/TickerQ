import { createHmac, timingSafeEqual } from 'crypto';

export const MAX_TIMESTAMP_SKEW_SECONDS = 300;

export function generateSignature(
    webhookSignature: string,
    method: string,
    pathAndQuery: string,
    timestamp: number,
    body: string | Buffer,
    requestNonce?: string,
): string {
    const payload = Buffer.concat([
        Buffer.from(requestNonce === undefined
            ? `${method}\n${pathAndQuery}\n${timestamp}\n`
            : `${method}\n${pathAndQuery}\n${timestamp}\n${requestNonce}\n`, 'utf8'),
        Buffer.isBuffer(body) ? body : Buffer.from(body || '', 'utf8'),
    ]);
    return createHmac('sha256', Buffer.from(webhookSignature, 'utf8')).update(payload).digest('base64');
}

export function generateResponseSignature(
    webhookSignature: string,
    status: number,
    pathAndQuery: string,
    timestamp: number,
    requestNonce: string,
    body: Buffer,
): string {
    const payload = Buffer.concat([
        Buffer.from(`${status}\n${pathAndQuery}\n${timestamp}\n${requestNonce}\n`, 'utf8'),
        body,
    ]);
    return createHmac('sha256', Buffer.from(webhookSignature, 'utf8')).update(payload).digest('base64');
}

export function validateSignature(
    webhookSignature: string | null,
    method: string,
    pathAndQuery: string,
    timestampHeader: string | undefined,
    signatureHeader: string | undefined,
    bodyBytes: Buffer,
    requestNonce?: string,
): string | null {
    if (!webhookSignature) return 'WebhookSignature is not configured. Cannot validate request.';
    if (!signatureHeader) return 'Missing X-TickerQ-Signature header.';
    if (!timestampHeader || !/^-?\d+$/.test(timestampHeader)) return 'Invalid X-Timestamp format.';
    const timestamp = Number(timestampHeader);
    if (!Number.isSafeInteger(timestamp)) return 'Invalid X-Timestamp format.';
    const nowSeconds = Math.floor(Date.now() / 1000);
    if (Math.abs(nowSeconds - timestamp) > MAX_TIMESTAMP_SKEW_SECONDS)
        return `Timestamp skew exceeds ${MAX_TIMESTAMP_SKEW_SECONDS} seconds.`;
    let received: Buffer;
    try { received = Buffer.from(signatureHeader, 'base64'); }
    catch { return 'Invalid Base64 in X-TickerQ-Signature header.'; }
    const expected = Buffer.from(generateSignature(webhookSignature, method, pathAndQuery, timestamp, bodyBytes, requestNonce), 'base64');
    return received.length === expected.length && timingSafeEqual(received, expected) ? null : 'Signature mismatch.';
}
