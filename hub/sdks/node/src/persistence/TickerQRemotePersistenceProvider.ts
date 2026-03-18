import { TickerQSdkHttpClient } from '../client/TickerQSdkHttpClient';
import type { InternalFunctionContext } from '../models/InternalFunctionContext';
import type { TimeTickerEntity } from '../models/TimeTickerEntity';
import type { CronTickerEntity } from '../models/CronTickerEntity';

const TIME_TICKERS_PATH = 'time-tickers';
const CRON_TICKERS_PATH = 'cron-tickers';

/**
 * Remote persistence provider that communicates with the TickerQ Scheduler via HTTP.
 *
 * Only CRUD operations are implemented. Query/queue operations throw NotSupportedError.
 */
export class TickerQRemotePersistenceProvider {
    private readonly client: TickerQSdkHttpClient;

    constructor(client: TickerQSdkHttpClient) {
        this.client = client;
    }

    // ─── Time Ticker CRUD ───────────────────────────────────────────────

    async addTimeTickers(tickers: TimeTickerEntity[], signal?: AbortSignal): Promise<number> {
        await this.client.postAsync(`/${TIME_TICKERS_PATH}`, tickers, signal);
        return tickers.length;
    }

    async updateTimeTickers(tickers: TimeTickerEntity[], signal?: AbortSignal): Promise<number> {
        await this.client.putAsync(`/${TIME_TICKERS_PATH}`, tickers, signal);
        return tickers.length;
    }

    async removeTimeTickers(tickerIds: string[], signal?: AbortSignal): Promise<number> {
        await this.client.postAsync(`/${TIME_TICKERS_PATH}/delete`, tickerIds, signal);
        return tickerIds.length;
    }

    async updateTimeTicker(functionContext: InternalFunctionContext, signal?: AbortSignal): Promise<void> {
        await this.client.putAsyncOrThrow(`/${TIME_TICKERS_PATH}/context`, functionContext, signal);
    }

    async updateTimeTickersWithUnifiedContext(
        timeTickerIds: string[],
        functionContext: InternalFunctionContext,
        signal?: AbortSignal,
    ): Promise<void> {
        await this.client.postAsync(
            `/${TIME_TICKERS_PATH}/unified-context`,
            { ids: timeTickerIds, context: functionContext },
            signal,
        );
    }

    async getTimeTickerRequest(id: string, signal?: AbortSignal): Promise<Buffer | null> {
        return this.client.getBytesAsync(`/${TIME_TICKERS_PATH}/request/${id}`, signal);
    }

    // ─── Cron Ticker CRUD ───────────────────────────────────────────────

    async insertCronTickers(tickers: CronTickerEntity[], signal?: AbortSignal): Promise<number> {
        await this.client.postAsync(`/${CRON_TICKERS_PATH}`, tickers, signal);
        return tickers.length;
    }

    async updateCronTickers(tickers: CronTickerEntity[], signal?: AbortSignal): Promise<number> {
        await this.client.putAsync(`/${CRON_TICKERS_PATH}`, tickers, signal);
        return tickers.length;
    }

    async removeCronTickers(cronTickerIds: string[], signal?: AbortSignal): Promise<number> {
        await this.client.postAsync(`/${CRON_TICKERS_PATH}/delete`, cronTickerIds, signal);
        return cronTickerIds.length;
    }

    // ─── Cron Ticker Occurrence ─────────────────────────────────────────

    async updateCronTickerOccurrence(functionContext: InternalFunctionContext, signal?: AbortSignal): Promise<void> {
        await this.client.putAsyncOrThrow('/cron-ticker-occurrences/context', functionContext, signal);
    }

    async getCronTickerOccurrenceRequest(tickerId: string, signal?: AbortSignal): Promise<Buffer | null> {
        return this.client.getBytesAsync(`/cron-ticker-occurrences/request/${tickerId}`, signal);
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
