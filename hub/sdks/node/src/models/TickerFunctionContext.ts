import { TickerType } from '../enums';

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
}
