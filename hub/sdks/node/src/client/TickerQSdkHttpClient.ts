import * as https from 'node:https';
import * as http from 'node:http';
import { TickerSdkOptions, TICKERQ_SDK_CONSTANTS } from '../TickerSdkOptions';
import { generateSignature } from '../utils/TickerQSignature';

/**
 * HTTP client for communicating with TickerQ Hub and Scheduler.
 *
 * - Hub requests get X-Api-Key / X-Api-Secret headers.
 * - Scheduler requests get X-Timestamp / X-TickerQ-Signature headers.
 */
export class TickerQSdkHttpClient {
    private readonly options: TickerSdkOptions;
    private readonly logger: TickerQLogger | null;
    private readonly insecureAgent: https.Agent | undefined;

    constructor(options: TickerSdkOptions, logger?: TickerQLogger) {
        this.options = options;
        this.logger = logger ?? null;

        if (options.allowSelfSignedCerts) {
            this.insecureAgent = new https.Agent({ rejectUnauthorized: false });
        }
    }

    async getAsync<TResponse>(path: string, signal?: AbortSignal): Promise<TResponse | null> {
        return this.sendAsync<undefined, TResponse>('GET', path, undefined, signal);
    }

    async postAsync<TRequest, TResponse>(path: string, request: TRequest, signal?: AbortSignal): Promise<TResponse | null> {
        return this.sendAsync<TRequest, TResponse>('POST', path, request, signal);
    }

    async putAsync<TRequest, TResponse>(path: string, request: TRequest, signal?: AbortSignal): Promise<TResponse | null> {
        return this.sendAsync<TRequest, TResponse>('PUT', path, request, signal);
    }

    /**
     * PUT that throws on failure instead of swallowing errors.
     * Used for critical operations like status reporting.
     */
    async putAsyncOrThrow<TRequest>(path: string, request: TRequest, signal?: AbortSignal): Promise<void> {
        const url = this.buildUrl(path);
        const body = JSON.stringify(request);
        const headers = this.buildHeaders(url, 'PUT', body);
        headers['Content-Type'] = 'application/json';

        const responseBody = await this.rawRequest(url, 'PUT', headers, body, signal);

        if (responseBody === null) {
            throw new Error(`TickerQ HTTP PUT ${path} failed: no response`);
        }
    }

    async deleteAsync(path: string, signal?: AbortSignal): Promise<void> {
        await this.sendAsync<undefined, undefined>('DELETE', path, undefined, signal);
    }

    async getBytesAsync(path: string, signal?: AbortSignal): Promise<Buffer | null> {
        const url = this.buildUrl(path);
        const headers = this.buildHeaders(url, 'GET', '');

        try {
            const result = await this.rawRequest(url, 'GET', headers, undefined, signal);
            if (result === null) return null;
            return Buffer.from(result, 'utf-8');
        } catch (err) {
            this.logger?.error(`TickerQ HTTP GET ${path} error:`, err);
            return null;
        }
    }

    private async sendAsync<TRequest, TResponse>(
        method: string,
        path: string,
        request?: TRequest,
        signal?: AbortSignal,
    ): Promise<TResponse | null> {
        const url = this.buildUrl(path);
        const body = request !== undefined ? JSON.stringify(request) : '';
        const headers = this.buildHeaders(url, method, body);

        if (body) {
            headers['Content-Type'] = 'application/json';
        }

        try {
            const responseBody = await this.rawRequest(url, method, headers, body || undefined, signal);
            if (!responseBody) return null;
            return JSON.parse(responseBody) as TResponse;
        } catch (err) {
            this.logger?.error(`TickerQ HTTP ${method} ${path} error:`, err);
            return null;
        }
    }

    /**
     * Low-level HTTP request using node:http / node:https.
     * This bypasses fetch entirely so we can use https.Agent
     * with rejectUnauthorized: false for self-signed certs.
     */
    private rawRequest(
        url: URL,
        method: string,
        headers: Record<string, string>,
        body?: string,
        signal?: AbortSignal,
    ): Promise<string | null> {
        return new Promise((resolve, reject) => {
            const isHttps = url.protocol === 'https:';
            const transport = isHttps ? https : http;

            const reqOptions: https.RequestOptions = {
                hostname: url.hostname,
                port: url.port || (isHttps ? 443 : 80),
                path: url.pathname + (url.search || ''),
                method,
                headers,
                timeout: this.options.timeoutMs,
            };

            // Apply insecure agent for self-signed certs
            if (isHttps && this.insecureAgent) {
                reqOptions.agent = this.insecureAgent;
            }

            const req = transport.request(reqOptions, (res) => {
                const chunks: Buffer[] = [];
                res.on('data', (chunk: Buffer) => chunks.push(chunk));
                res.on('end', () => {
                    const responseBody = Buffer.concat(chunks).toString('utf-8');

                    if (!res.statusCode || res.statusCode >= 400) {
                        const errMsg = `TickerQ HTTP ${method} ${url.pathname} failed: ${res.statusCode} ${responseBody}`;
                        this.logger?.error(errMsg);
                        reject(new Error(errMsg));
                        return;
                    }

                    resolve(responseBody || null);
                });
            });

            req.on('error', reject);
            req.on('timeout', () => {
                req.destroy(new Error(`TickerQ HTTP ${method} ${url.pathname} timed out after ${this.options.timeoutMs}ms`));
            });

            // Abort support
            if (signal) {
                if (signal.aborted) {
                    req.destroy(new Error('Aborted'));
                    return;
                }
                signal.addEventListener('abort', () => req.destroy(new Error('Aborted')), { once: true });
            }

            if (body) {
                req.write(body, 'utf-8');
            }
            req.end();
        });
    }

    private buildUrl(path: string): URL {
        const baseUri = this.isHubPath(path)
            ? this.options.hubUri
            : (this.options.apiUri ?? this.options.hubUri);
        return new URL(path, baseUri);
    }

    private isHubPath(path: string): boolean {
        return path.startsWith('/api/apps/');
    }

    private isHubRequest(url: URL): boolean {
        return url.hostname === TICKERQ_SDK_CONSTANTS.HubHostname;
    }

    private buildHeaders(url: URL, method: string, body: string): Record<string, string> {
        const headers: Record<string, string> = {};

        if (this.isHubRequest(url)) {
            if (this.options.apiKey) headers['X-Api-Key'] = this.options.apiKey;
            if (this.options.apiSecret) headers['X-Api-Secret'] = this.options.apiSecret;
        } else if (this.options.webhookSignature) {
            const timestamp = Math.floor(Date.now() / 1000);
            const pathAndQuery = url.pathname + (url.search || '');
            const signature = generateSignature(
                this.options.webhookSignature,
                method,
                pathAndQuery,
                timestamp,
                body,
            );
            headers['X-Timestamp'] = String(timestamp);
            headers['X-TickerQ-Signature'] = signature;
        }

        return headers;
    }
}

export interface TickerQLogger {
    info(message: string, ...args: unknown[]): void;
    warn(message: string, ...args: unknown[]): void;
    error(message: string, ...args: unknown[]): void;
}
