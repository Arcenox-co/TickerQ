import { TickerSdkOptions } from './TickerSdkOptions';
import type { TickerQLogger } from './logging/TickerQLogger';
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
import { WorkerStreamClient } from './worker/WorkerStreamClient';
import { TickerQSdkControlClient } from './control/TickerQSdkControlClient';

/**
 * Main entry point for the TickerQ Node.js SDK.
 *
 * Usage:
 * ```ts
 * const sdk = new TickerQSdk(opts => opts
 *     .setApiKey('your-key')
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
 * ```
 */
export class TickerQSdk {
    readonly options: TickerSdkOptions;
    readonly syncService: TickerQFunctionSyncService;
    readonly persistenceProvider: TickerQRemotePersistenceProvider;
    readonly taskScheduler: TickerQTaskScheduler;
    readonly concurrencyGate: TickerFunctionConcurrencyGate;
    readonly workerStream: WorkerStreamClient;
    readonly controlClient: TickerQSdkControlClient;

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
        this.syncService = new TickerQFunctionSyncService(this.options);
        this.taskScheduler = new TickerQTaskScheduler();
        this.concurrencyGate = new TickerFunctionConcurrencyGate();
        this.workerStream = new WorkerStreamClient(
            this.options,
            this.syncService,
            this.taskScheduler,
            this.concurrencyGate,
            this.logger ?? undefined,
        );
        this.controlClient = new TickerQSdkControlClient(
            this.options,
            this.syncService,
            this.logger ?? undefined,
        );
        this.persistenceProvider = new TickerQRemotePersistenceProvider(this.options, this.workerStream);
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

        this.logger?.info(`TickerQ SDK: Starting with ${TickerFunctionProvider.tickerFunctions.size} registered function(s)...`);

        const result = await this.syncService.syncAsync();

        if (result) {
            this.logger?.info(
                `TickerQ SDK: Synced with Hub. Scheduler URL: ${result.applicationUrl}`,
            );
            await this.controlClient.start();
            await this.workerStream.start();
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
        const drained = await this.taskScheduler.waitForRunningTasks(timeoutMs);
        this.taskScheduler.freeze();
        this.controlClient.stop();
        if (!drained) {
            this.logger?.warn(`TickerQ SDK: Stop timed out after ${timeoutMs}ms; cancelling running executions.`);
        }
        await this.workerStream.stop(!drained);
        this.taskScheduler.dispose();
        this._started = false;
        this.logger?.info('TickerQ SDK: Stopped.');
    }

    get isStarted(): boolean {
        return this._started;
    }
}

export interface CreateTickerSdkOptions {
    apiKey: string;
    nodeName?: string;
    timeoutMs?: number;
    allowSelfSignedCerts?: boolean;
}

export function createTickerSdk(
    options: CreateTickerSdkOptions,
    logger?: TickerQLogger,
): TickerQSdk {
    return new TickerQSdk((sdkOptions) => {
        sdkOptions.setApiKey(options.apiKey);
        if (options.nodeName) sdkOptions.setNodeName(options.nodeName);
        if (options.timeoutMs != null) sdkOptions.setTimeoutMs(options.timeoutMs);
        if (options.allowSelfSignedCerts != null) sdkOptions.setAllowSelfSignedCerts(options.allowSelfSignedCerts);
    }, logger);
}
