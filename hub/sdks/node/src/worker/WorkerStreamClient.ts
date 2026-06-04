import * as crypto from 'node:crypto';
import { TickerSdkOptions, TICKERQ_SDK_CONSTANTS } from '../TickerSdkOptions';
import { workerProto, createCredentials, normalizeGrpcTarget } from '../grpc/GrpcContracts';
import { TickerFunctionProvider, type TickerFunctionRegistration } from '../infrastructure/TickerFunctionProvider';
import { TickerFunctionConcurrencyGate } from './TickerFunctionConcurrencyGate';
import { TickerQTaskScheduler } from './TickerQTaskScheduler';
import { TickerStatus, TickerTaskPriority, TickerType, RunCondition } from '../enums';
import type { TickerFunctionContext } from '../models/TickerFunctionContext';
import type { InternalFunctionContext } from '../models/InternalFunctionContext';
import type { TickerQLogger } from '../logging/TickerQLogger';
import { toBareFunctionName } from '../utils/FunctionName';

type Pending<T> = {
    resolve: (value: T) => void;
    reject: (reason: unknown) => void;
    timeout: NodeJS.Timeout;
    cleanup?: () => void;
};

const MIN_RECONNECT_DELAY_MS = 1_000;
const MAX_RECONNECT_DELAY_MS = 30_000;

export interface WorkerExecuteFunction {
    requestId: string;
    tickerId: string;
    functionName: string;
    type: number;
    retryCount: number;
    isDue: boolean;
    scheduledFor?: { seconds?: string | number; nanos?: number } | Date | string;
    requestPayload?: Buffer | Uint8Array;
    retries: number;
    retryIntervalsSeconds?: number[];
}

function newRequestId(): string {
    return crypto.randomUUID().replace(/-/g, '');
}

function timestampToDate(value: WorkerExecuteFunction['scheduledFor']): Date {
    if (!value) return new Date();
    if (value instanceof Date) return value;
    if (typeof value === 'string') return new Date(value);

    const seconds = typeof value.seconds === 'string'
        ? Number.parseInt(value.seconds, 10)
        : value.seconds ?? 0;
    const nanos = value.nanos ?? 0;
    return new Date((seconds * 1000) + Math.floor(nanos / 1_000_000));
}

function dateToTimestamp(value: string | Date | null | undefined): { seconds: number; nanos: number } | undefined {
    if (!value) return undefined;
    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) return undefined;

    return {
        seconds: Math.floor(date.getTime() / 1000),
        nanos: (date.getTime() % 1000) * 1_000_000,
    };
}

function deserializeRequest(payload?: Buffer | Uint8Array): unknown {
    if (!payload || payload.length === 0) return undefined;
    const buffer = Buffer.from(payload);
    const text = buffer.toString('utf-8');
    if (!text) return undefined;

    try {
        return JSON.parse(text);
    } catch {
        return Buffer.from(text, 'utf-8').equals(buffer) ? text : buffer;
    }
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

function logLevelValue(level: 'trace' | 'debug' | 'info' | 'warn' | 'error' | 'critical'): number {
    switch (level) {
        case 'trace': return 0;
        case 'debug': return 1;
        case 'info': return 2;
        case 'warn': return 3;
        case 'error': return 4;
        case 'critical': return 5;
    }
}

function buildInternalContext(
    req: WorkerExecuteFunction,
    bareFunctionName: string,
    registration: TickerFunctionRegistration,
    status: TickerStatus,
    elapsedTime: number,
    error: unknown | null,
): InternalFunctionContext {
    return {
        parametersToUpdate: error
            ? ['Status', 'ElapsedTime', 'ExecutedAt', 'ExceptionDetails']
            : ['Status', 'ElapsedTime', 'ExecutedAt'],
        cachedPriority: registration.priority,
        cachedMaxConcurrency: registration.maxConcurrency,
        functionName: bareFunctionName,
        tickerId: req.tickerId,
        parentId: null,
        type: req.type as TickerType,
        retries: req.retries ?? 0,
        retryCount: req.retryCount ?? 0,
        status,
        elapsedTime,
        exceptionDetails: error ? serializeException(error) : null,
        executedAt: new Date().toISOString(),
        retryIntervals: req.retryIntervalsSeconds ?? [],
        releaseLock: false,
        executionTime: timestampToDate(req.scheduledFor).toISOString(),
        runCondition: RunCondition.OnSuccess,
        timeTickerChildren: [],
    };
}

export class WorkerStreamClient {
    private readonly options: TickerSdkOptions;
    private readonly syncService: { syncAsync(signal?: AbortSignal): Promise<unknown> };
    private readonly scheduler: TickerQTaskScheduler;
    private readonly concurrencyGate: TickerFunctionConcurrencyGate;
    private readonly logger: TickerQLogger | null;

    private writer: any | null = null;
    private stopped = false;
    private started = false;
    private connecting = false;
    private ready = false;
    // gRPC typically fires both 'error' and 'end' on a torn-down stream;
    // this flag keeps cleanupStream + scheduleReconnect from running twice
    // per disconnect (which produced duplicate "closed before reply" warnings).
    private streamClosed = false;
    private readyResolve: (() => void) | null = null;
    private readyReject: ((reason: unknown) => void) | null = null;
    private readyPromise: Promise<void> = this.createReadyPromise();
    private readonly readyWaiters = new Set<{ resolve: () => void; reject: (reason: unknown) => void; timeout: NodeJS.Timeout }>();
    private heartbeatTimer: NodeJS.Timeout | null = null;
    private reconnectTimer: NodeJS.Timeout | null = null;
    private readonly pendingOperations = new Map<string, Pending<any>>();
    private readonly pendingBytes = new Map<string, Pending<any>>();
    private readonly runningControllers = new Map<string, AbortController>();

    constructor(
        options: TickerSdkOptions,
        syncService: { syncAsync(signal?: AbortSignal): Promise<unknown> },
        scheduler: TickerQTaskScheduler,
        concurrencyGate: TickerFunctionConcurrencyGate,
        logger?: TickerQLogger,
    ) {
        this.options = options;
        this.syncService = syncService;
        this.scheduler = scheduler;
        this.concurrencyGate = concurrencyGate;
        this.logger = logger ?? null;
    }

    async start(timeoutMs = this.options.timeoutMs): Promise<void> {
        if (this.started) {
            await this.waitUntilReady(timeoutMs);
            return;
        }
        this.started = true;
        this.stopped = false;
        this.connectWithBackoff(0);
        try {
            await this.waitUntilReady(timeoutMs);
        } catch (err) {
            this.started = false;
            throw err;
        }
    }

    async stop(abortRunning = false): Promise<void> {
        this.stopped = true;
        this.started = false;
        this.connecting = false;
        this.ready = false;
        if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
        if (this.heartbeatTimer) clearInterval(this.heartbeatTimer);
        if (abortRunning) {
            for (const controller of this.runningControllers.values()) {
                controller.abort();
            }
        }
        this.writer?.end?.();
        this.failPending(new Error('Worker stream stopped.'));
    }

    async sendAndAwaitOperation(
        requestId: string,
        event: Record<string, unknown>,
        timeoutMs = this.options.timeoutMs,
        signal?: AbortSignal,
    ): Promise<any> {
        return this.sendAndAwait(this.pendingOperations, requestId, event, timeoutMs, signal);
    }

    async sendAndAwaitBytes(
        requestId: string,
        event: Record<string, unknown>,
        timeoutMs = this.options.timeoutMs,
        signal?: AbortSignal,
    ): Promise<any> {
        return this.sendAndAwait(this.pendingBytes, requestId, event, timeoutMs, signal);
    }

    async sendLogLine(logLine: Record<string, unknown>): Promise<void> {
        await this.send({ logLine });
    }

    private connectWithBackoff(delayMs: number): void {
        if (this.stopped || this.connecting || this.reconnectTimer) return;

        // Jitter only the sleep, not the value threaded back into
        // nextReconnectDelay(), so the base keeps growing deterministically while a
        // fleet of SDKs that dropped together (e.g. a Hub restart) doesn't reconnect
        // in lockstep and stampede the scheduler.
        this.reconnectTimer = setTimeout(() => {
            this.reconnectTimer = null;
            if (this.stopped) return;
            this.connecting = true;
            this.ready = false;

            this.runOnce(Math.min(delayMs, 30_000)).catch((err) => {
                this.logger?.warn('TickerQ SDK: Worker stream failed:', err);
                this.scheduleReconnect(this.nextReconnectDelay(delayMs));
            });
        }, this.applyJitter(delayMs));
    }

    // Uniform ±20% spread around the base delay; never negative.
    private applyJitter(delayMs: number): number {
        if (delayMs <= 0) return 0;
        const factor = 1 + (Math.random() * 2 - 1) * 0.2;
        return Math.max(0, Math.floor(delayMs * factor));
    }

    private scheduleReconnect(delayMs: number): void {
        if (this.stopped) return;
        this.connecting = false;
        this.ready = false;
        if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
        this.reconnectTimer = null;
        this.writer = null;
        this.readyPromise = this.createReadyPromise();
        this.connectWithBackoff(delayMs);
    }

    private async runOnce(previousDelayMs: number): Promise<void> {
        if (!this.options.apiUri || !this.options.webhookSignature) {
            this.scheduleReconnect(previousDelayMs);
            return;
        }

        const SchedulerWorkerService = workerProto.tickerq.worker.v1.SchedulerWorkerService;
        const client = new SchedulerWorkerService(
            normalizeGrpcTarget(this.options.apiUri),
            createCredentials(this.options.apiUri, this.options.allowSelfSignedCerts),
            {
                'grpc.max_receive_message_length': 16 * 1024 * 1024,
                'grpc.max_send_message_length': 16 * 1024 * 1024,
            },
        );

        const stream = client.openWorkerStream();
        this.writer = stream;
        this.streamClosed = false;

        stream.on('data', (command: any) => {
            this.handleCommand(command).catch((err) => {
                this.logger?.warn('TickerQ SDK: Worker command failed:', err);
            });
        });
        stream.on('error', (err: Error) => {
            if (this.streamClosed) return;
            this.logger?.warn('TickerQ SDK: Worker stream error:', err);
            this.cleanupStream();
            this.scheduleReconnect(this.nextReconnectDelay(previousDelayMs));
        });
        stream.on('end', () => {
            if (this.streamClosed) return;
            this.cleanupStream();
            this.scheduleReconnect(1_000);
        });

        await this.sendHello();
    }

    private cleanupStream(): void {
        if (this.streamClosed) return;
        this.streamClosed = true;
        if (this.heartbeatTimer) clearInterval(this.heartbeatTimer);
        this.connecting = false;
        this.ready = false;
        this.writer = null;
        this.failPending(new Error('Worker stream closed before reply arrived.'));
        this.readyPromise = this.createReadyPromise();
    }

    private async sendHello(): Promise<void> {
        const nonce = crypto.randomUUID().replace(/-/g, '');
        const unixSeconds = Math.floor(Date.now() / 1000);
        const canonical = `${this.options.nodeName}\n${TICKERQ_SDK_CONSTANTS.SdkVersion}\n${nonce}\n${unixSeconds}`;
        const hmacSignature = crypto
            .createHmac('sha256', this.options.webhookSignature ?? '')
            .update(canonical, 'utf8')
            .digest('base64');

        await this.sendUnguarded({
            hello: {
                apiKey: this.options.apiKey ?? '',
                nodeName: this.options.nodeName,
                sdkVersion: TICKERQ_SDK_CONSTANTS.SdkVersion,
                maxConcurrency: 0,
                nonce,
                unixSeconds,
                hmacSignature,
            },
        });
    }

    private async handleCommand(command: any): Promise<void> {
        if (command.helloAck) {
            this.connecting = false;
            this.ready = true;
            this.readyResolve?.();
            this.resolveReadyWaiters();
            this.startHeartbeat();
            this.logger?.info(`TickerQ SDK: Worker stream registered (${command.helloAck.workerId}).`);
            return;
        }
        if (command.heartbeat) return;
        if (command.disconnect) {
            this.logger?.warn(`TickerQ SDK: Scheduler disconnected worker stream: ${command.disconnect.reason}`);
            this.writer?.end?.();
            this.scheduleReconnect(1_000);
            return;
        }
        if (command.operationResult) {
            this.completePending(this.pendingOperations, command.operationResult.requestId, command.operationResult);
            return;
        }
        if (command.bytesResult) {
            this.completePending(this.pendingBytes, command.bytesResult.requestId, command.bytesResult);
            return;
        }
        if (command.executeFunction) {
            await this.handleExecuteFunction(command.executeFunction);
            return;
        }
        if (command.triggerResync) {
            await this.handleTriggerResync(command.triggerResync);
            return;
        }
        if (command.removeFunction) {
            await this.handleRemoveFunction(command.removeFunction);
            return;
        }
        if (command.cancelExecution) {
            await this.handleCancelExecution(command.cancelExecution);
        }
    }

    private async handleExecuteFunction(req: WorkerExecuteFunction): Promise<void> {
        if (!req.requestId || !req.functionName || !req.tickerId) {
            await this.sendExecutionResult(req.requestId, false, 'ExecuteFunction is missing required fields.');
            return;
        }

        const bareFunctionName = toBareFunctionName(req.functionName);
        const registration = TickerFunctionProvider.getFunction(bareFunctionName);
        if (!registration) {
            await this.sendExecutionResult(req.requestId, false, `Function '${bareFunctionName}' is not registered.`);
            return;
        }

        const executionController = new AbortController();
        this.runningControllers.set(req.tickerId, executionController);

        this.scheduler.queueAsync(async (schedulerSignal) => {
            const linkedController = new AbortController();
            const abort = () => linkedController.abort();
            executionController.signal.addEventListener('abort', abort, { once: true });
            schedulerSignal.addEventListener('abort', abort, { once: true });
            if (executionController.signal.aborted || schedulerSignal.aborted) {
                linkedController.abort();
            }

            const semaphore = this.concurrencyGate.getSemaphore(bareFunctionName, registration.maxConcurrency);
            let release: (() => void) | null = null;
            const startedAt = performance.now();

            try {
                release = semaphore ? await semaphore.acquire() : null;
                if (linkedController.signal.aborted) {
                    throw new DOMException('Aborted', 'AbortError');
                }
                await this.executeFunctionOnce(req, bareFunctionName, registration, linkedController.signal);
                const elapsed = Math.round(performance.now() - startedAt);
                await this.reportStatus(req, bareFunctionName, registration, req.isDue ? TickerStatus.DueDone : TickerStatus.Done, elapsed, null);
                await this.sendExecutionResult(req.requestId, true);
            } catch (err) {
                const elapsed = Math.round(performance.now() - startedAt);
                const cancelled = linkedController.signal.aborted || (err instanceof Error && err.name === 'AbortError');
                await this.reportStatus(
                    req,
                    bareFunctionName,
                    registration,
                    cancelled ? TickerStatus.Cancelled : TickerStatus.Failed,
                    elapsed,
                    err,
                );
                await this.sendExecutionResult(
                    req.requestId,
                    false,
                    cancelled ? 'Cancelled by dashboard' : (err instanceof Error ? err.message : String(err)),
                    cancelled,
                );
            } finally {
                release?.();
                executionController.signal.removeEventListener('abort', abort);
                schedulerSignal.removeEventListener('abort', abort);
                this.runningControllers.delete(req.tickerId);
            }
        }, registration.priority).catch((err) => {
            this.runningControllers.delete(req.tickerId);
            this.sendExecutionResult(req.requestId, false, err instanceof Error ? err.message : String(err)).catch(() => undefined);
        });
    }

    /**
     * Runs the user function exactly ONCE for the attempt the scheduler dispatched.
     *
     * Retries are owned by the scheduler in the worker-stream model: on failure it
     * re-dispatches a fresh ExecuteFunction with an incremented `retryCount`, having
     * already waited the configured retry interval. The SDK must therefore execute a
     * single attempt per message — looping here as well would compound the user's
     * retry budget (≈ Retries² executions) and double the delays. `retries` /
     * `retryIntervalsSeconds` are intentionally NOT consulted for looping here; they
     * are the scheduler's to act on.
     */
    private async executeFunctionOnce(
        req: WorkerExecuteFunction,
        bareFunctionName: string,
        registration: TickerFunctionRegistration,
        signal: AbortSignal,
    ): Promise<void> {
        if (signal.aborted) throw new DOMException('Aborted', 'AbortError');

        const retryCount = req.retryCount ?? 0;
        const ctx: TickerFunctionContext<unknown> = {
            id: req.tickerId,
            type: req.type as TickerType,
            retryCount,
            isDue: req.isDue,
            scheduledFor: timestampToDate(req.scheduledFor),
            functionName: bareFunctionName,
            request: deserializeRequest(req.requestPayload) ?? TickerFunctionProvider.getRequestDefault(bareFunctionName),
            log: this.createExecutionLogger(req, bareFunctionName),
        };
        await registration.delegate(ctx, signal);
    }

    private async reportStatus(
        req: WorkerExecuteFunction,
        bareFunctionName: string,
        registration: TickerFunctionRegistration,
        status: TickerStatus,
        elapsedTime: number,
        error: unknown | null,
    ): Promise<void> {
        const requestId = newRequestId();
        const context = this.mapFunctionContext(
            buildInternalContext(req, bareFunctionName, registration, status, elapsedTime, error),
        );

        const event = req.type === TickerType.CronTickerOccurrence
            ? { updateCronOccurrence: { requestId, context } }
            : { updateTimeTicker: { requestId, context } };

        try {
            const result = await this.sendAndAwaitOperation(requestId, event);
            if (!result.success) {
                this.logger?.warn(`TickerQ SDK: Scheduler rejected status update: ${result.error}`);
            }
        } catch (err) {
            this.logger?.warn('TickerQ SDK: Failed to report ticker status:', err);
        }
    }

    private createExecutionLogger(req: WorkerExecuteFunction, bareFunctionName: string) {
        const write = (level: 'trace' | 'debug' | 'info' | 'warn' | 'error' | 'critical', message: string, category = 'sdk') => {
            if (!this.options.logCapture.enabled) return;
            if (logLevelValue(level) < logLevelValue(this.options.logCapture.minLevel)) return;
            this.sendLogLine({
                tickerId: req.tickerId,
                tickerType: req.type,
                unixMs: Date.now(),
                level: logLevelValue(level),
                source: 'sdk',
                message,
                category,
                functionName: bareFunctionName,
            }).catch(() => undefined);
        };

        return {
            trace: (message: string, category?: string) => write('trace', message, category),
            debug: (message: string, category?: string) => write('debug', message, category),
            info: (message: string, category?: string) => write('info', message, category),
            warn: (message: string, category?: string) => write('warn', message, category),
            error: (message: string, category?: string) => write('error', message, category),
            critical: (message: string, category?: string) => write('critical', message, category),
        };
    }

    mapFunctionContext(context: InternalFunctionContext): Record<string, unknown> {
        const mapped: Record<string, unknown> = {
            functionName: context.functionName,
            tickerId: context.tickerId,
            type: context.type,
            retries: context.retries,
            retryCount: context.retryCount,
            status: context.status,
            elapsedTimeMs: context.elapsedTime,
            exceptionDetails: context.exceptionDetails ?? '',
            releaseLock: context.releaseLock,
            runCondition: context.runCondition,
            retryIntervals: context.retryIntervals ?? [],
            parametersToUpdate: context.parametersToUpdate ?? [],
        };

        if (context.parentId) mapped.parentId = context.parentId;
        const executedAt = dateToTimestamp(context.executedAt);
        const executionTime = dateToTimestamp(context.executionTime);
        if (executedAt) mapped.executedAt = executedAt;
        if (executionTime) mapped.executionTime = executionTime;

        return mapped;
    }

    newRequestId(): string {
        return newRequestId();
    }

    private async handleTriggerResync(req: { requestId: string }): Promise<void> {
        try {
            await this.syncService.syncAsync();
            await this.send({ ack: { requestId: req.requestId, success: true, error: '' } });
        } catch (err) {
            await this.send({
                ack: {
                    requestId: req.requestId,
                    success: false,
                    error: err instanceof Error ? err.message : String(err),
                },
            });
        }
    }

    private async handleRemoveFunction(req: { requestId: string; functionName: string }): Promise<void> {
        TickerFunctionProvider.removeFunction(req.functionName);
        await this.send({ ack: { requestId: req.requestId, success: true, error: '' } });
    }

    private async handleCancelExecution(req: { requestId: string; tickerId: string }): Promise<void> {
        this.runningControllers.get(req.tickerId)?.abort();
        await this.send({ ack: { requestId: req.requestId, success: true, error: '' } });
    }

    private async sendExecutionResult(requestId: string, success: boolean, error = '', cancelled = false): Promise<void> {
        if (!requestId) return;
        await this.send({
            executionResult: {
                requestId,
                success,
                error,
                cancelled,
            },
        });
    }

    private startHeartbeat(): void {
        if (this.heartbeatTimer) clearInterval(this.heartbeatTimer);
        this.heartbeatTimer = setInterval(() => {
            this.send({ heartbeat: { unixMs: Date.now() } }).catch(() => undefined);
        }, 15_000);
    }

    private async sendAndAwait<T>(
        pendingMap: Map<string, Pending<T>>,
        requestId: string,
        event: Record<string, unknown>,
        timeoutMs: number,
        signal?: AbortSignal,
    ): Promise<T> {
        if (signal?.aborted) {
            throw new DOMException('Aborted', 'AbortError');
        }
        await this.waitForReady(signal);
        if (signal?.aborted) {
            throw new DOMException('Aborted', 'AbortError');
        }
        return new Promise<T>((resolve, reject) => {
            const timeout = setTimeout(() => {
                signal?.removeEventListener('abort', abort);
                pendingMap.delete(requestId);
                reject(new Error(`Scheduler did not reply to ${requestId} within ${timeoutMs}ms.`));
            }, timeoutMs);

            const abort = () => {
                clearTimeout(timeout);
                pendingMap.delete(requestId);
                reject(new DOMException('Aborted', 'AbortError'));
            };

            if (signal) {
                signal.addEventListener('abort', abort, { once: true });
            }

            const cleanup = () => signal?.removeEventListener('abort', abort);

            pendingMap.set(requestId, { resolve, reject, timeout, cleanup });
            this.sendUnguarded(event).catch((err) => {
                clearTimeout(timeout);
                cleanup();
                pendingMap.delete(requestId);
                reject(err);
            });
        });
    }

    private async send(event: Record<string, unknown>): Promise<void> {
        await this.readyPromise;
        await this.sendUnguarded(event);
    }

    private async waitForReady(signal?: AbortSignal): Promise<void> {
        if (!signal) {
            await this.readyPromise;
            return;
        }

        await new Promise<void>((resolve, reject) => {
            const abort = () => {
                signal.removeEventListener('abort', abort);
                reject(new DOMException('Aborted', 'AbortError'));
            };
            signal.addEventListener('abort', abort, { once: true });
            this.readyPromise
                .then(() => {
                    signal.removeEventListener('abort', abort);
                    resolve();
                })
                .catch((err) => {
                    signal.removeEventListener('abort', abort);
                    reject(err);
                });
        });
    }

    private async sendUnguarded(event: Record<string, unknown>): Promise<void> {
        if (!this.writer) {
            throw new Error('Worker stream is not connected.');
        }

        await new Promise<void>((resolve, reject) => {
            this.writer.write(event, (err: Error | null | undefined) => {
                if (err) reject(err);
                else resolve();
            });
        });
    }

    private nextReconnectDelay(previousDelayMs: number): number {
        if (previousDelayMs <= 0) return MIN_RECONNECT_DELAY_MS;
        return Math.min(previousDelayMs * 2, MAX_RECONNECT_DELAY_MS);
    }

    private completePending<T>(pendingMap: Map<string, Pending<T>>, requestId: string, value: T): void {
        const pending = pendingMap.get(requestId);
        if (!pending) return;
        clearTimeout(pending.timeout);
        pending.cleanup?.();
        pendingMap.delete(requestId);
        pending.resolve(value);
    }

    private failPending(err: Error): void {
        for (const pending of [...this.pendingOperations.values(), ...this.pendingBytes.values()]) {
            clearTimeout(pending.timeout);
            pending.cleanup?.();
            pending.reject(err);
        }
        this.pendingOperations.clear();
        this.pendingBytes.clear();
        this.readyReject?.(err);
        this.rejectReadyWaiters(err);
    }

    private createReadyPromise(): Promise<void> {
        const promise = new Promise<void>((resolve, reject) => {
            this.readyResolve = resolve;
            this.readyReject = reject;
        });
        // failPending() rejects this promise on every disconnect/stop. When no
        // send()/waitForReady() is awaiting it, that rejection would surface as an
        // unhandledRejection (log noise, or a hard crash under
        // --unhandled-rejections=strict). Attach a no-op catch so the stored
        // promise is always considered handled; real awaiters still observe the
        // rejection through their own await.
        promise.catch(() => undefined);
        return promise;
    }

    private waitUntilReady(timeoutMs: number): Promise<void> {
        if (this.ready) return Promise.resolve();

        return new Promise<void>((resolve, reject) => {
            const waiter = {
                resolve,
                reject,
                timeout: setTimeout(() => {
                    this.readyWaiters.delete(waiter);
                    reject(new Error(`TickerQ SDK worker stream did not register within ${timeoutMs}ms.`));
                }, timeoutMs),
            };
            this.readyWaiters.add(waiter);
        });
    }

    private resolveReadyWaiters(): void {
        for (const waiter of this.readyWaiters) {
            clearTimeout(waiter.timeout);
            waiter.resolve();
        }
        this.readyWaiters.clear();
    }

    private rejectReadyWaiters(reason: unknown): void {
        for (const waiter of this.readyWaiters) {
            clearTimeout(waiter.timeout);
            waiter.reject(reason);
        }
        this.readyWaiters.clear();
    }
}
