import type { InternalFunctionContext } from '../models/InternalFunctionContext';
import type { TimeTickerEntity } from '../models/TimeTickerEntity';
import type { CronTickerEntity } from '../models/CronTickerEntity';
import type { WorkerStreamClient } from '../worker/WorkerStreamClient';
import { qualifyFunctionName } from '../utils/FunctionName';
import { TickerSdkOptions } from '../TickerSdkOptions';

type TickerWithRequest = { function: string; request?: unknown };

function encodeRequestPayload(request: unknown): string | null {
    if (request == null) return null;
    if (Buffer.isBuffer(request)) return request.toString('base64');
    if (request instanceof Uint8Array) return Buffer.from(request).toString('base64');

    if (typeof request === 'string') {
        return Buffer.from(request, 'utf8').toString('base64');
    }

    return Buffer.from(JSON.stringify(request), 'utf8').toString('base64');
}

/**
 * Remote persistence provider that communicates with the TickerQ Scheduler via worker stream.
 *
 * Only CRUD operations are implemented. Query/queue operations throw NotSupportedError.
 */
export class TickerQRemotePersistenceProvider {
    private readonly options: TickerSdkOptions;
    private readonly stream: WorkerStreamClient;

    constructor(options: TickerSdkOptions, stream: WorkerStreamClient) {
        this.options = options;
        this.stream = stream;
    }

    // ─── Time Ticker CRUD ───────────────────────────────────────────────

    async addTimeTickers(tickers: TimeTickerEntity[], signal?: AbortSignal): Promise<number> {
        const entities = this.qualifyTickers(tickers);
        const requestId = this.stream.newRequestId();
        return this.awaitOperation(requestId, {
            addTimeTickers: {
                requestId,
                entitiesJson: Buffer.from(JSON.stringify(entities), 'utf8'),
            },
        }, signal);
    }

    async updateTimeTickers(tickers: TimeTickerEntity[], signal?: AbortSignal): Promise<number> {
        const entities = this.qualifyTickers(tickers);
        const requestId = this.stream.newRequestId();
        return this.awaitOperation(requestId, {
            updateTimeTickers: {
                requestId,
                entitiesJson: Buffer.from(JSON.stringify(entities), 'utf8'),
            },
        }, signal);
    }

    async removeTimeTickers(tickerIds: string[], signal?: AbortSignal): Promise<number> {
        const requestId = this.stream.newRequestId();
        return this.awaitOperation(requestId, {
            removeTimeTickers: {
                requestId,
                ids: tickerIds,
            },
        }, signal);
    }

    async updateTimeTicker(functionContext: InternalFunctionContext, signal?: AbortSignal): Promise<void> {
        const requestId = this.stream.newRequestId();
        await this.awaitOperation(requestId, {
            updateTimeTicker: {
                requestId,
                context: this.stream.mapFunctionContext(functionContext),
            },
        }, signal);
    }

    async updateTimeTickersWithUnifiedContext(
        timeTickerIds: string[],
        functionContext: InternalFunctionContext,
        signal?: AbortSignal,
    ): Promise<void> {
        const requestId = this.stream.newRequestId();
        await this.awaitOperation(requestId, {
            updateTimeTickersUnified: {
                requestId,
                ids: timeTickerIds,
                context: this.stream.mapFunctionContext(functionContext),
            },
        }, signal);
    }

    async getTimeTickerRequest(id: string, signal?: AbortSignal): Promise<Buffer | null> {
        const requestId = this.stream.newRequestId();
        return this.awaitBytes(requestId, {
            getTimeTickerRequest: {
                requestId,
                tickerId: id,
            },
        }, signal);
    }

    // ─── Cron Ticker CRUD ───────────────────────────────────────────────

    async insertCronTickers(tickers: CronTickerEntity[], signal?: AbortSignal): Promise<number> {
        const entities = this.qualifyTickers(tickers);
        const requestId = this.stream.newRequestId();
        return this.awaitOperation(requestId, {
            insertCronTickers: {
                requestId,
                entitiesJson: Buffer.from(JSON.stringify(entities), 'utf8'),
            },
        }, signal);
    }

    async updateCronTickers(tickers: CronTickerEntity[], signal?: AbortSignal): Promise<number> {
        const entities = this.qualifyTickers(tickers);
        const requestId = this.stream.newRequestId();
        return this.awaitOperation(requestId, {
            updateCronTickers: {
                requestId,
                entitiesJson: Buffer.from(JSON.stringify(entities), 'utf8'),
            },
        }, signal);
    }

    async removeCronTickers(cronTickerIds: string[], signal?: AbortSignal): Promise<number> {
        const requestId = this.stream.newRequestId();
        return this.awaitOperation(requestId, {
            removeCronTickers: {
                requestId,
                ids: cronTickerIds,
            },
        }, signal);
    }

    // ─── Cron Ticker Occurrence ─────────────────────────────────────────

    async updateCronTickerOccurrence(functionContext: InternalFunctionContext, signal?: AbortSignal): Promise<void> {
        const requestId = this.stream.newRequestId();
        await this.awaitOperation(requestId, {
            updateCronOccurrence: {
                requestId,
                context: this.stream.mapFunctionContext(functionContext),
            },
        }, signal);
    }

    async getCronTickerOccurrenceRequest(tickerId: string, signal?: AbortSignal): Promise<Buffer | null> {
        const requestId = this.stream.newRequestId();
        return this.awaitBytes(requestId, {
            getCronOccurrenceRequest: {
                requestId,
                tickerId,
            },
        }, signal);
    }

    private qualifyTickers<T extends TickerWithRequest>(tickers: T[]): T[] {
        return tickers.map((ticker) => ({
            ...ticker,
            function: qualifyFunctionName(ticker.function, this.options.nodeName),
            request: encodeRequestPayload(ticker.request),
        }));
    }

    private async awaitOperation(requestId: string, event: Record<string, unknown>, signal?: AbortSignal): Promise<number> {
        const result = await this.stream.sendAndAwaitOperation(requestId, event, this.options.timeoutMs, signal);
        if (!result.success) {
            throw new Error(result.error || 'TickerQ Scheduler reported operation failure.');
        }
        return result.affected ?? 0;
    }

    private async awaitBytes(requestId: string, event: Record<string, unknown>, signal?: AbortSignal): Promise<Buffer | null> {
        const result = await this.stream.sendAndAwaitBytes(requestId, event, this.options.timeoutMs, signal);
        if (!result.success) {
            throw new Error(result.error || 'TickerQ Scheduler reported bytes operation failure.');
        }
        return result.found ? Buffer.from(result.payload ?? []) : null;
    }

    // ─── Not Supported (server-side only) ───────────────────────────────

    queueTimeTickers(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    queueTimedOutTimeTickers(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    releaseAcquiredTimeTickers(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    getEarliestTimeTickers(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    migrateDefinedCronTickers(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    getAllCronTickerExpressions(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    releaseDeadNodeTimeTickerResources(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    getEarliestAvailableCronOccurrence(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    queueCronTickerOccurrences(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    queueTimedOutCronTickerOccurrences(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    releaseAcquiredCronTickerOccurrences(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    updateCronTickerOccurrencesWithUnifiedContext(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    releaseDeadNodeOccurrenceResources(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    getTimeTickerById(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    getTimeTickers(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    getTimeTickersPaginated(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    getCronTickerById(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    getCronTickers(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    getCronTickersPaginated(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    getAllCronTickerOccurrences(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    getAllCronTickerOccurrencesPaginated(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    insertCronTickerOccurrences(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    removeCronTickerOccurrences(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    acquireImmediateTimeTickersAsync(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }

    acquireImmediateCronOccurrencesAsync(): never {
        throw new Error('NotSupported: This operation requires direct database access. Use the Hub dashboard or the local persistence provider.');
    }
}
