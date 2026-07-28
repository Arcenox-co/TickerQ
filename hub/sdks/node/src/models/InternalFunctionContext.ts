import { TickerType, TickerStatus, TickerTaskPriority, RunCondition } from '../enums';
import type { ResultEnvelope } from './ResultEnvelope';

export interface InternalFunctionContext {
    parametersToUpdate: string[];
    cachedPriority: TickerTaskPriority;
    cachedMaxConcurrency: number;
    functionName: string;
    tickerId: string;
    parentId: string | null;
    type: TickerType;
    retries: number;
    retryCount: number;
    status: TickerStatus;
    elapsedTime: number;
    exceptionDetails: string | null;
    executedAt: string;
    retryIntervals: number[];
    releaseLock: boolean;
    executionTime: string;
    runCondition: RunCondition;
    timeTickerChildren: InternalFunctionContext[];
    /** Included only in a final successful status update. */
    resultEnvelope?: ResultEnvelope;
}
