export const TICKERQ_SDK_CONSTANTS = {
    HubGrpcBaseUrl: 'https://grpc.hub.tickerq.net/',
    SdkVersion: '1.0.0',
    SdkType: 'nodejs',
} as const;

export interface TickerSdkLogCaptureOptions {
    enabled: boolean;
    minLevel: 'trace' | 'debug' | 'info' | 'warn' | 'error' | 'critical';
}

export class TickerSdkOptions {
    /** Scheduler worker-stream URL — set after sync with Hub. */
    apiUri: string | null = null;

    /** Fixed Hub gRPC URL. */
    readonly hubControlUri: string = TICKERQ_SDK_CONSTANTS.HubGrpcBaseUrl;

    /** HMAC-SHA256 worker-stream signature key — set after Hub sync. */
    webhookSignature: string | null = null;

    /** Single Hub-issued SDK token. */
    apiKey: string | null = null;

    /** Identifier for this application node. */
    nodeName: string = process.env.COMPUTERNAME
        ?? process.env.HOSTNAME
        ?? 'tickerq-node';

    /** gRPC operation timeout in milliseconds (default: 30000). */
    timeoutMs: number = 30_000;

    /** Allow self-signed SSL certificates for local scheduler worker streams. */
    allowSelfSignedCerts: boolean = false;

    /** Per-execution log forwarding settings. */
    logCapture: TickerSdkLogCaptureOptions = {
        enabled: true,
        minLevel: 'info',
    };

    setApiKey(apiKey: string): this {
        this.apiKey = apiKey;
        return this;
    }

    setNodeName(nodeName: string): this {
        if (!nodeName || !nodeName.trim()) {
            throw new Error('TickerQ SDK: NodeName cannot be empty.');
        }
        this.nodeName = nodeName;
        return this;
    }

    setTimeoutMs(timeoutMs: number): this {
        this.timeoutMs = timeoutMs;
        return this;
    }

    setAllowSelfSignedCerts(allow: boolean): this {
        this.allowSelfSignedCerts = allow;
        return this;
    }

    setLogCapture(options: Partial<TickerSdkLogCaptureOptions>): this {
        this.logCapture = { ...this.logCapture, ...options };
        return this;
    }

    validate(): void {
        if (!this.apiKey) {
            throw new Error('TickerQ SDK: ApiKey is required. Call setApiKey() with the Hub-issued tq_sdk_* token.');
        }
    }
}
