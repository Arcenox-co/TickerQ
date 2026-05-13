import { TickerType } from '../enums';

export interface TickerExecutionLogger {
    trace(message: string, category?: string): void;
    debug(message: string, category?: string): void;
    info(message: string, category?: string): void;
    warn(message: string, category?: string): void;
    error(message: string, category?: string): void;
    critical(message: string, category?: string): void;
}

/**
 * Base context passed to every ticker function handler.
 *
 * When TRequest is provided, the `request` property carries the deserialized payload.
 * When omitted (defaults to `never`), `request` is not present.
 */
export interface TickerFunctionContext<TRequest = never> {
    id: string;
    type: TickerType;
    retryCount: number;
    isDue: boolean;
    scheduledFor: Date;
    functionName: string;
    request: TRequest;
    /** Forward execution-scoped log lines to the TickerQ dashboard. */
    log: TickerExecutionLogger;
}
