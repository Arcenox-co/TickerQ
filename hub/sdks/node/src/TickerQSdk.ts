import { TickerSdkOptions } from './TickerSdkOptions';
import { TickerQSdkHttpClient, TickerQLogger } from './client/TickerQSdkHttpClient';
import {
    TickerFunctionProvider,
    type TickerFunctionHandler,
    type TickerFunctionHandlerNoRequest,
} from './infrastructure/TickerFunctionProvider';
import { TickerFunctionBuilder, type FunctionOptions } from './infrastructure/TickerFunctionBuilder';
import { TickerQFunctionSyncService } from './infrastructure/TickerQFunctionSyncService';
import { TickerQRemotePersistenceProvider } from './persistence/TickerQRemotePersistenceProvider';
import { TickerQTaskScheduler } from './worker/TickerQTaskScheduler';
import { TickerFunctionConcurrencyGate } from './worker/TickerFunctionConcurrencyGate';
import { SdkExecutionEndpoint } from './middleware/SdkExecutionEndpoint';

/**
 * Main entry point for the TickerQ Node.js SDK.
 *
 * Usage:
 * ```ts
 * const sdk = new TickerQSdk(opts => opts
 *     .setApiKey('your-key')
 *     .setApiSecret('your-secret')
 *     .setCallbackUri('https://your-app.com')
 *     .setNodeName('node-1')
 * );
 *
 * // With typed request
 * sdk.function('SendEmail', { priority: TickerTaskPriority.High })
 *     .withRequest({ to: '', subject: '', body: '' })
 *     .handle(async (ctx, signal) => {
 *         ctx.request.to; // fully typed
 *     });
 *
 * // Without request
 * sdk.function('Cleanup', { cronExpression: '0 0 3 * * *' })
 *     .handle(async (ctx, signal) => {
 *         console.log(ctx.functionName);
 *     });
 *
 * await sdk.start();
 * sdk.expressHandlers().mount(app);
 * ```
 */
export class TickerQSdk {
    readonly options: TickerSdkOptions;
    readonly httpClient: TickerQSdkHttpClient;
    readonly syncService: TickerQFunctionSyncService;
    readonly persistenceProvider: TickerQRemotePersistenceProvider;
    readonly taskScheduler: TickerQTaskScheduler;
    readonly concurrencyGate: TickerFunctionConcurrencyGate;

    private readonly endpoint: SdkExecutionEndpoint;
    private readonly logger: TickerQLogger | null;
    private _started = false;

    constructor(
        configure: (options: TickerSdkOptions) => void,
        logger?: TickerQLogger,
    ) {
        this.options = new TickerSdkOptions();
        configure(this.options);
        this.options.validate();

        this.logger = logger ?? null;
        this.httpClient = new TickerQSdkHttpClient(this.options, this.logger ?? undefined);
        this.syncService = new TickerQFunctionSyncService(this.httpClient, this.options);
        this.persistenceProvider = new TickerQRemotePersistenceProvider(this.httpClient);
        this.taskScheduler = new TickerQTaskScheduler();
        this.concurrencyGate = new TickerFunctionConcurrencyGate();

        this.endpoint = new SdkExecutionEndpoint(
            this.options,
            this.syncService,
            this.taskScheduler,
            this.concurrencyGate,
            this.persistenceProvider,
            this.logger ?? undefined,
        );
    }

    /**
     * Register a function WITH a typed request payload.
     * The default instance provides both the type inference AND the example JSON for the Hub.
     *
     * ```ts
     * sdk.registerFunction('SendEmail',
     *     { to: '', subject: '', body: '' },    // ← default instance
     *     async (ctx, signal) => {
     *         ctx.request.to;   // ← string, fully typed
     *     },
     * );
     * ```
     */
    registerFunction<TRequest>(
        functionName: string,
        requestDefault: TRequest,
        handler: TickerFunctionHandler<TRequest>,
        options?: FunctionOptions,
    ): this;

    /**
     * Register a function WITHOUT a request payload.
     *
     * ```ts
     * sdk.registerFunction('Cleanup', async (ctx, signal) => {
     *     console.log(ctx.functionName);
     * });
     * ```
     */
    registerFunction(
        functionName: string,
        handler: TickerFunctionHandlerNoRequest,
        options?: FunctionOptions,
    ): this;

    // ─── Implementation ─────────────────────────────────────────────────

    registerFunction(
        functionName: string,
        requestDefaultOrHandler: Record<string, unknown> | TickerFunctionHandlerNoRequest,
        handlerOrOptions?: TickerFunctionHandler<any> | FunctionOptions,
        maybeOptions?: FunctionOptions,
    ): this {
        if (typeof requestDefaultOrHandler === 'function') {
            TickerFunctionProvider.registerFunction(
                functionName,
                requestDefaultOrHandler as TickerFunctionHandlerNoRequest,
                handlerOrOptions as FunctionOptions | undefined,
            );
        } else {
            TickerFunctionProvider.registerFunction(
                functionName,
                requestDefaultOrHandler,
                handlerOrOptions as TickerFunctionHandler<any>,
                maybeOptions,
            );
        }
        return this;
    }

    /**
     * Fluent builder for registering a function.
     *
     * ```ts
     * // With typed request
     * sdk.function('SendEmail', { priority: TickerTaskPriority.High })
     *     .withRequest({ to: '', subject: '', body: '' })
     *     .handle(async (ctx, signal) => {
     *         ctx.request.to; // fully typed
     *     });
     *
     * // Without request
     * sdk.function('Cleanup', { cronExpression: '0 0 3 * * *' })
     *     .handle(async (ctx, signal) => { });
     * ```
     */
    function(functionName: string, options?: FunctionOptions): TickerFunctionBuilder {
        return new TickerFunctionBuilder(functionName, options);
    }

    /**
     * Start the SDK: freeze function registry and sync with Hub.
     */
    async start(): Promise<void> {
        if (this._started) return;

        TickerFunctionProvider.build();

        this.logger?.info(
            `TickerQ SDK: Starting with ${TickerFunctionProvider.tickerFunctions.size} registered function(s)...`,
        );

        const result = await this.syncService.syncAsync();

        if (result) {
            this.logger?.info(
                `TickerQ SDK: Synced with Hub. Scheduler URL: ${result.applicationUrl}`,
            );
        } else {
            this.logger?.warn('TickerQ SDK: Hub sync returned null. Functions may not be scheduled.');
        }

        this._started = true;
    }

    /**
     * Graceful shutdown: wait for running tasks and dispose the scheduler.
     */
    async stop(timeoutMs = 30_000): Promise<void> {
        this.logger?.info('TickerQ SDK: Stopping...');
        this.taskScheduler.freeze();
        await this.taskScheduler.waitForRunningTasks(timeoutMs);
        this.taskScheduler.dispose();
        this.logger?.info('TickerQ SDK: Stopped.');
    }

    /**
     * Returns a framework-agnostic HTTP handler for /execute and /resync.
     */
    createHandler(prefix = ''): (req: import('http').IncomingMessage, res: import('http').ServerResponse) => void {
        return this.endpoint.createHandler(prefix);
    }

    /**
     * Returns Express-compatible route handlers for /execute and /resync.
     */
    expressHandlers(prefix = '') {
        return this.endpoint.expressHandlers(prefix);
    }

    get isStarted(): boolean {
        return this._started;
    }
}
