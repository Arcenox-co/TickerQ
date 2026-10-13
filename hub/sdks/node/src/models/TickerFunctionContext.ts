import { TickerType } from '../enums';
import type { RemoteExecutionContext } from './RemoteExecutionContext';
import {
    createJsonResultEnvelope,
    deserializeJsonResult,
    type ResultEnvelope,
} from './ResultEnvelope';

export interface FunctionResultSink {
    readonly hasResult: boolean;
    readonly resultEnvelope?: ResultEnvelope;
}

/**
 * Base context passed to every ticker function handler.
 *
 * When TRequest is provided, the `request` property carries the deserialized payload.
 * When omitted (defaults to `never`), `request` is not present.
 */
export interface TickerFunctionContext<TRequest = never, TResult = unknown> {
    id: string;
    parentId: string | null;
    type: TickerType;
    retryCount: number;
    isDue: boolean;
    scheduledFor: Date;
    functionName: string;
    request: TRequest;
    /** True only when the direct parent supplied a result envelope. */
    readonly hasParentResult: boolean;
    /** Deserialize the direct parent's JSON result, or return undefined when absent. */
    getParentResult<TParentResult = unknown>(): TParentResult | undefined;
    /** Set this attempt's result. Last call wins; explicit null is a present result. */
    setResult<TValue extends TResult = TResult>(value: TValue): void;
}

/** @internal Creates a fresh, per-attempt context and result sink. */
export function createFunctionContext<TRequest = unknown, TResult = unknown>(
    context: RemoteExecutionContext,
    request?: TRequest,
    resultContract?: { contractId?: string; contractType?: string; mediaType?: string },
): TickerFunctionContext<TRequest, TResult> & { readonly resultSink: FunctionResultSink } {
    let resultEnvelope: ResultEnvelope | undefined;
    const resultSink: FunctionResultSink = {
        get hasResult() { return resultEnvelope !== undefined; },
        get resultEnvelope() { return resultEnvelope; },
    };
    return {
        id: context.id,
        parentId: context.parentId,
        type: context.type,
        retryCount: context.retryCount,
        isDue: context.isDue,
        scheduledFor: new Date(context.scheduledFor),
        functionName: context.functionName,
        request: request as TRequest,
        hasParentResult: context.parentResult !== undefined,
        getParentResult<TParentResult = unknown>(): TParentResult | undefined {
            return context.parentResult === undefined
                ? undefined
                : deserializeJsonResult<TParentResult>(context.parentResult);
        },
        setResult<TValue extends TResult = TResult>(value: TValue): void {
            resultEnvelope = createJsonResultEnvelope(value, resultContract);
        },
        resultSink,
    };
}
