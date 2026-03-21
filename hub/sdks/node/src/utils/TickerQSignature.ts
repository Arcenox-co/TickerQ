import { createHmac, timingSafeEqual } from 'crypto';

const MAX_TIMESTAMP_SKEW_SECONDS = 300;

/**
 * Generates an HMAC-SHA256 signature for outgoing requests to the Scheduler.
 *
 * Payload = UTF-8("{METHOD}\n{PATH}?{QUERY}\n{TIMESTAMP}\n") + UTF-8(body)
 * Key    = UTF-8(webhookSignature)
 * Output = Base64(HMAC-SHA256(key, payload))
 */
export function generateSignature(
    webhookSignature: string,
    method: string,
    pathAndQuery: string,
    timestamp: number,
    body: string,
): string {
    const header = `${method}\n${pathAndQuery}\n${timestamp}\n`;
    const headerBytes = Buffer.from(header, 'utf-8');
    const bodyBytes = Buffer.from(body || '', 'utf-8');
    const payload = Buffer.concat([headerBytes, bodyBytes]);

    const key = Buffer.from(webhookSignature, 'utf-8');
    const hmac = createHmac('sha256', key);
    hmac.update(payload);
    return hmac.digest('base64');
}

/**
 * Validates an incoming HMAC-SHA256 signature on webhook requests.
 *
 * Returns null on success, or an error message string on failure.
 */
export function validateSignature(
    webhookSignature: string | null,
    method: string,
    pathAndQuery: string,
    timestampHeader: string | undefined,
    signatureHeader: string | undefined,
    bodyBytes: Buffer,
): string | null {
    if (!webhookSignature) {
        return 'WebhookSignature is not configured. Cannot validate request.';
    }

    if (!signatureHeader) {
        return 'Missing X-TickerQ-Signature header.';
    }

    if (!timestampHeader) {
        return 'Missing X-Timestamp header.';
    }

    const timestamp = parseInt(timestampHeader, 10);
    if (isNaN(timestamp)) {
        return 'Invalid X-Timestamp format.';
    }

    const nowSeconds = Math.floor(Date.now() / 1000);
    if (Math.abs(nowSeconds - timestamp) > MAX_TIMESTAMP_SKEW_SECONDS) {
        return `Timestamp skew exceeds ${MAX_TIMESTAMP_SKEW_SECONDS} seconds.`;
    }

    let receivedBytes: Buffer;
    try {
        receivedBytes = Buffer.from(signatureHeader, 'base64');
    } catch {
        return 'Invalid Base64 in X-TickerQ-Signature header.';
    }

    const header = `${method}\n${pathAndQuery}\n${timestamp}\n`;
    const headerBytes = Buffer.from(header, 'utf-8');
    const payload = Buffer.concat([headerBytes, bodyBytes]);

    const key = Buffer.from(webhookSignature, 'utf-8');
    const hmac = createHmac('sha256', key);
    hmac.update(payload);
    const expectedBytes = hmac.digest();

    if (expectedBytes.length !== receivedBytes.length) {
        return 'Signature mismatch.';
    }

    if (!timingSafeEqual(expectedBytes, receivedBytes)) {
        return 'Signature mismatch.';
    }

    return null;
}
