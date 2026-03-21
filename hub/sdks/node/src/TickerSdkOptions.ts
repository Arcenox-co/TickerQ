export const TICKERQ_SDK_CONSTANTS = {
    HubBaseUrl: 'https://hub.tickerq.net/',
    HubHostname: 'hub.tickerq.net',
} as const;

export class TickerSdkOptions {
    /** Scheduler URL — updated after sync with Hub. */
    apiUri: string | null = null;

    /** Fixed Hub URL. */
    readonly hubUri: string = TICKERQ_SDK_CONSTANTS.HubBaseUrl;

    /** HMAC-SHA256 webhook signature key — set after Hub sync. */
    webhookSignature: string | null = null;

    /** Public URL where the Hub sends execution callbacks. */
    callbackUri: string | null = null;

    /** Hub API key for authentication. */
    apiKey: string | null = null;

    /** Hub API secret for authentication. */
    apiSecret: string | null = null;

    /** Identifier for this application node. */
    nodeName: string | null = null;

    /** HTTP request timeout in milliseconds (default: 30000). */
    timeoutMs: number = 30_000;

    /** Allow self-signed SSL certificates (dev/local Scheduler). Default: false. */
    allowSelfSignedCerts: boolean = false;

    setApiKey(apiKey: string): this {
        this.apiKey = apiKey;
        return this;
    }

    setApiSecret(apiSecret: string): this {
        this.apiSecret = apiSecret;
        return this;
    }

    setCallbackUri(callbackUri: string): this {
        this.callbackUri = callbackUri;
        return this;
    }

    setNodeName(nodeName: string): this {
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

    validate(): void {
        if (!this.apiKey) {
            throw new Error('TickerQ SDK: ApiKey is required. Call setApiKey().');
        }
        if (!this.apiSecret) {
            throw new Error('TickerQ SDK: ApiSecret is required. Call setApiSecret().');
        }
        if (!this.callbackUri) {
            throw new Error('TickerQ SDK: CallbackUri is required. Call setCallbackUri().');
        }
        if (!this.nodeName) {
            throw new Error('TickerQ SDK: NodeName is required. Call setNodeName().');
        }
    }
}
