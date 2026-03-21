import type { IncomingMessage, ServerResponse } from 'http';
import { validateSignature } from '../utils/TickerQSignature';
import { TickerSdkOptions } from '../TickerSdkOptions';
import { TickerFunctionProvider } from '../infrastructure/TickerFunctionProvider';
import { TickerQFunctionSyncService } from '../infrastructure/TickerQFunctionSyncService';
import { TickerQTaskScheduler } from '../worker/TickerQTaskScheduler';
import { TickerFunctionConcurrencyGate } from '../worker/TickerFunctionConcurrencyGate';
import { TickerQRemotePersistenceProvider } from '../persistence/TickerQRemotePersistenceProvider';
import { normalizeExecutionContext, type RemoteExecutionContext } from '../models/RemoteExecutionContext';
import type { TickerFunctionContext } from '../models/TickerFunctionContext';
import type { InternalFunctionContext } from '../models/InternalFunctionContext';
import { TickerType, TickerStatus, TickerTaskPriority, RunCondition } from '../enums';
import type { TickerQLogger } from '../client/TickerQSdkHttpClient';

function buildFunctionContext(context: RemoteExecutionContext): TickerFunctionContext<unknown> {
    return {
        id: context.id,
        type: context.type,
        retryCount: context.retryCount,
        isDue: context.isDue,
        scheduledFor: new Date(context.scheduledFor),
        functionName: context.functionName,
        request: TickerFunctionProvider.getRequestDefault(context.functionName),
    };
}

function buildInternalContext(
    context: RemoteExecutionContext,
    registration: { priority: TickerTaskPriority; maxConcurrency: number },
): InternalFunctionContext {
    return {
        parametersToUpdate: [],
        cachedPriority: registration.priority,
        cachedMaxConcurrency: registration.maxConcurrency,
        functionName: context.functionName,
        tickerId: context.id,
        parentId: null,
        type: context.type,
        retries: 0,
        retryCount: context.retryCount,
        status: TickerStatus.InProgress,
        elapsedTime: 0,
        exceptionDetails: null,
        executedAt: new Date().toISOString(),
        retryIntervals: [],
        releaseLock: false,
        executionTime: context.scheduledFor,
        runCondition: RunCondition.OnSuccess,
        timeTickerChildren: [],
    };
}

function serializeException(err: unknown): string {
    if (err instanceof Error) {
        return JSON.stringify({
            type: err.constructor.name,
            message: err.message,
            stackTrace: err.stack ?? null,
        });
    }
    return JSON.stringify({ type: 'Unknown', message: String(err), stackTrace: null });
}

/**
 * HTTP request handler for the /execute and /resync endpoints.
 * Framework-agnostic — works with raw Node.js http, Express, Fastify, etc.
 */
export class SdkExecutionEndpoint {
    private readonly options: TickerSdkOptions;
    private readonly syncService: TickerQFunctionSyncService;
    private readonly scheduler: TickerQTaskScheduler;
    private readonly concurrencyGate: TickerFunctionConcurrencyGate;
    private readonly persistenceProvider: TickerQRemotePersistenceProvider;
    private readonly logger: TickerQLogger | null;

    constructor(
        options: TickerSdkOptions,
        syncService: TickerQFunctionSyncService,
        scheduler: TickerQTaskScheduler,
        concurrencyGate: TickerFunctionConcurrencyGate,
        persistenceProvider: TickerQRemotePersistenceProvider,
        logger?: TickerQLogger,
    ) {
        this.options = options;
        this.syncService = syncService;
        this.scheduler = scheduler;
        this.concurrencyGate = concurrencyGate;
        this.persistenceProvider = persistenceProvider;
        this.logger = logger ?? null;
    }

    /**
     * Returns an Express-compatible middleware router.
     * Mounts POST /execute and POST /resync under the given prefix.
     */
    createHandler(prefix = ''): (req: IncomingMessage, res: ServerResponse) => void {
        const executePath = `${prefix}/execute`;
        const resyncPath = `${prefix}/resync`;

        return async (req: IncomingMessage, res: ServerResponse) => {
            const url = req.url ?? '';
            const method = req.method?.toUpperCase() ?? '';

            if (method !== 'POST') {
                res.writeHead(405);
                res.end('Method Not Allowed');
                return;
            }

            if (url === executePath) {
                await this.handleExecute(req, res);
            } else if (url === resyncPath) {
                await this.handleResync(req, res);
            } else {
                res.writeHead(404);
                res.end('Not Found');
            }
        };
    }

    /**
     * Returns Express-compatible route handlers.
     * Call with your express app or router instance:
     *
     * ```ts
     * const { execute, resync } = sdk.getEndpoint().expressHandlers();
     * app.post('/execute', execute);
     * app.post('/resync', resync);
     * ```
     */
    expressHandlers(prefix = ''): {
        execute: (req: any, res: any) => Promise<void>;
        resync: (req: any, res: any) => Promise<void>;
        mount: (app: { post: (path: string, handler: (req: any, res: any) => Promise<void>) => void }) => void;
    } {
        const execute = async (req: any, res: any) => {
            await this.handleExecuteExpress(req, res);
        };
        const resync = async (req: any, res: any) => {
            await this.handleResync(req, res);
        };
        const mount = (app: { post: (path: string, handler: (req: any, res: any) => Promise<void>) => void }) => {
            app.post(`${prefix}/execute`, execute);
            app.post(`${prefix}/resync`, resync);
        };
        return { execute, resync, mount };
    }

    // ─── /execute ───────────────────────────────────────────────────────

    private async handleExecute(req: IncomingMessage, res: ServerResponse): Promise<void> {
        const bodyBytes = await readBody(req);

        // Validate signature
        const pathAndQuery = req.url ?? '/execute';
        const validationError = validateSignature(
            this.options.webhookSignature,
            'POST',
            pathAndQuery,
            getHeader(req, 'x-timestamp'),
            getHeader(req, 'x-tickerq-signature'),
            bodyBytes,
        );

        if (validationError) {
            this.logger?.warn(`TickerQ signature validation failed: ${validationError}`);
            res.writeHead(401);
            res.end('Unauthorized');
            return;
        }

        let context: RemoteExecutionContext;
        try {
            const raw = JSON.parse(bodyBytes.toString('utf-8'));
            context = normalizeExecutionContext(raw);
        } catch {
            res.writeHead(400);
            res.end('Invalid JSON body');
            return;
        }

        if (!context.functionName) {
            res.writeHead(400);
            res.end('Missing functionName');
            return;
        }

        this.logger?.info(
            `TickerQ: Received /execute for '${context.functionName}' (id: ${context.id}, type: ${context.type})`,
        );

        // Look up the function
        const registration = TickerFunctionProvider.getFunction(context.functionName);
        if (!registration) {
            this.logger?.error(`TickerQ: Function '${context.functionName}' not found. Ensure it is registered.`);
            res.writeHead(404);
            res.end(`Function '${context.functionName}' not found`);
            return;
        }

        const functionContext = buildFunctionContext(context);

        // Queue execution with priority and concurrency gate
        const semaphore = this.concurrencyGate.getSemaphore(
            context.functionName,
            registration.maxConcurrency,
        );

        // Respond immediately — execution happens async (fire-and-forget from Hub's perspective)
        res.writeHead(200);
        res.end('OK');

        // Execute in the task scheduler
        this.scheduler.queueAsync(async (signal) => {
            await this.executeAndReportStatus(context, registration, functionContext, semaphore, signal);
        }, registration.priority).catch((err) => {
            this.logger?.error(`TickerQ: Failed to queue '${context.functionName}':`, err);
        });
    }

    /**
     * Express-specific handler that reads body from req.body if already parsed.
     */
    private async handleExecuteExpress(req: any, res: any): Promise<void> {
        let bodyBytes: Buffer;
        let bodyStr: string;

        if (req.body && typeof req.body === 'object') {
            bodyStr = JSON.stringify(req.body);
            bodyBytes = Buffer.from(bodyStr, 'utf-8');
        } else if (req.rawBody) {
            bodyBytes = Buffer.isBuffer(req.rawBody) ? req.rawBody : Buffer.from(req.rawBody);
            bodyStr = bodyBytes.toString('utf-8');
        } else {
            bodyBytes = await readBody(req);
            bodyStr = bodyBytes.toString('utf-8');
        }

        // Validate signature
        const pathAndQuery = req.originalUrl ?? req.url ?? '/execute';
        const validationError = validateSignature(
            this.options.webhookSignature,
            'POST',
            pathAndQuery,
            req.headers['x-timestamp'] as string | undefined,
            req.headers['x-tickerq-signature'] as string | undefined,
            bodyBytes,
        );

        if (validationError) {
            this.logger?.warn(`TickerQ signature validation failed: ${validationError}`);
            res.status(401).send('Unauthorized');
            return;
        }

        let context: RemoteExecutionContext;
        try {
            const raw = typeof req.body === 'object' ? req.body : JSON.parse(bodyStr);
            context = normalizeExecutionContext(raw);
        } catch {
            res.status(400).send('Invalid JSON body');
            return;
        }

        if (!context.functionName) {
            res.status(400).send('Missing functionName');
            return;
        }

        this.logger?.info(
            `TickerQ: Received /execute for '${context.functionName}' (id: ${context.id}, type: ${context.type})`,
        );

        const registration = TickerFunctionProvider.getFunction(context.functionName);
        if (!registration) {
            this.logger?.error(`TickerQ: Function '${context.functionName}' not found.`);
            res.status(404).send(`Function '${context.functionName}' not found`);
            return;
        }

        const functionContext = buildFunctionContext(context);

        const semaphore = this.concurrencyGate.getSemaphore(
            context.functionName,
            registration.maxConcurrency,
        );

        res.status(200).send('OK');

        this.scheduler.queueAsync(async (signal) => {
            await this.executeAndReportStatus(context, registration, functionContext, semaphore, signal);
        }, registration.priority).catch((err) => {
            this.logger?.error(`TickerQ: Failed to queue '${context.functionName}':`, err);
        });
    }

    // ─── Execution lifecycle ──────────────────────────────────────────────

    private async executeAndReportStatus(
        context: RemoteExecutionContext,
        registration: { delegate: (ctx: any, signal: AbortSignal) => Promise<void>; priority: TickerTaskPriority; maxConcurrency: number },
        functionContext: TickerFunctionContext<unknown>,
        semaphore: { acquire: () => Promise<() => void> } | null,
        signal: AbortSignal,
    ): Promise<void> {
        const internalCtx = buildInternalContext(context, registration);
        const startTime = performance.now();
        let release: (() => void) | null = null;
        const typeName = context.type === TickerType.CronTickerOccurrence ? 'CronTicker' : 'TimeTicker';

        this.logger?.info(
            `TickerQ [${typeName}] Executing '${context.functionName}' (id: ${context.id}, retry: ${context.retryCount}, isDue: ${context.isDue})`,
        );

        try {
            if (semaphore) {
                this.logger?.info(`TickerQ [${typeName}] '${context.functionName}' waiting for concurrency semaphore...`);
                release = await semaphore.acquire();
                this.logger?.info(`TickerQ [${typeName}] '${context.functionName}' semaphore acquired.`);
            }

            internalCtx.status = TickerStatus.InProgress;
            this.logger?.info(`TickerQ [${typeName}] '${context.functionName}' status -> InProgress`);

            await registration.delegate(functionContext, signal);

            // Success — set Done or DueDone based on isDue flag
            const elapsed = Math.round(performance.now() - startTime);
            internalCtx.status = context.isDue ? TickerStatus.DueDone : TickerStatus.Done;
            internalCtx.elapsedTime = elapsed;
            internalCtx.executedAt = new Date().toISOString();
            internalCtx.parametersToUpdate = ['Status', 'ElapsedTime', 'ExecutedAt'];

            this.logger?.info(
                `TickerQ [${typeName}] '${context.functionName}' status -> ${TickerStatus[internalCtx.status]} (${elapsed}ms)`,
            );
        } catch (err) {
            const elapsed = Math.round(performance.now() - startTime);

            if (signal.aborted || (err instanceof Error && err.name === 'AbortError')) {
                internalCtx.status = TickerStatus.Cancelled;
                this.logger?.warn(
                    `TickerQ [${typeName}] '${context.functionName}' status -> Cancelled after ${elapsed}ms`,
                );
            } else {
                internalCtx.status = TickerStatus.Failed;
                this.logger?.error(
                    `TickerQ [${typeName}] '${context.functionName}' status -> Failed after ${elapsed}ms:`,
                    err,
                );
            }

            internalCtx.elapsedTime = elapsed;
            internalCtx.executedAt = new Date().toISOString();
            internalCtx.exceptionDetails = serializeException(err);
            internalCtx.parametersToUpdate = ['Status', 'ElapsedTime', 'ExecutedAt', 'ExceptionDetails'];
        } finally {
            if (release) {
                release();
                this.logger?.info(`TickerQ [${typeName}] '${context.functionName}' semaphore released.`);
            }
        }

        // Report status back to the Scheduler/Hub
        const endpoint = context.type === TickerType.CronTickerOccurrence
            ? 'cron-ticker-occurrences/context'
            : 'time-tickers/context';

        this.logger?.info(
            `TickerQ [${typeName}] '${context.functionName}' reporting status ${TickerStatus[internalCtx.status]} to Scheduler (PUT /${endpoint})...`,
        );

        try {
            if (context.type === TickerType.CronTickerOccurrence) {
                await this.persistenceProvider.updateCronTickerOccurrence(internalCtx);
            } else {
                await this.persistenceProvider.updateTimeTicker(internalCtx);
            }
            this.logger?.info(
                `TickerQ [${typeName}] '${context.functionName}' status reported successfully.`,
            );
        } catch (err) {
            this.logger?.error(
                `TickerQ [${typeName}] '${context.functionName}' failed to report status ${TickerStatus[internalCtx.status]} to Scheduler:`,
                err,
            );
        }
    }

    // ─── /resync ────────────────────────────────────────────────────────

    private async handleResync(req: IncomingMessage | any, res: ServerResponse | any): Promise<void> {
        // Validate signature on resync too
        const bodyBytes = await readBody(req);
        const pathAndQuery = req.originalUrl ?? req.url ?? '/resync';
        const validationError = validateSignature(
            this.options.webhookSignature,
            'POST',
            pathAndQuery,
            getHeader(req, 'x-timestamp'),
            getHeader(req, 'x-tickerq-signature'),
            bodyBytes,
        );

        if (validationError) {
            this.logger?.warn(`TickerQ resync signature validation failed: ${validationError}`);
            if (typeof res.status === 'function') {
                res.status(401).send('Unauthorized');
            } else {
                res.writeHead(401);
                res.end('Unauthorized');
            }
            return;
        }

        try {
            await this.syncService.syncAsync();
            if (typeof res.status === 'function') {
                res.status(200).send('OK');
            } else {
                res.writeHead(200);
                res.end('OK');
            }
        } catch (err) {
            this.logger?.error('TickerQ: Resync failed:', err);
            if (typeof res.status === 'function') {
                res.status(500).send('Resync failed');
            } else {
                res.writeHead(500);
                res.end('Resync failed');
            }
        }
    }
}

// ─── Helpers ────────────────────────────────────────────────────────────

function readBody(req: IncomingMessage): Promise<Buffer> {
    return new Promise((resolve, reject) => {
        const chunks: Buffer[] = [];
        req.on('data', (chunk: Buffer) => chunks.push(chunk));
        req.on('end', () => resolve(Buffer.concat(chunks)));
        req.on('error', reject);
    });
}

function getHeader(req: IncomingMessage, name: string): string | undefined {
    const val = req.headers[name];
    return Array.isArray(val) ? val[0] : val;
}
