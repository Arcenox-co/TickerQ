import { TickerTaskPriority } from '../enums';
import type { TickerFunctionContext } from '../models/TickerFunctionContext';
import { TickerFunctionProvider, type TickerFunctionHandler, type TickerFunctionHandlerNoRequest } from './TickerFunctionProvider';

export interface FunctionOptions {
    cronExpression?: string;
    priority?: TickerTaskPriority;
    maxConcurrency?: number;
    requestType?: string;
}

/**
 * Fluent builder for registering a TickerQ function.
 *
 * ```ts
 * sdk.function('SendEmail', { priority: TickerTaskPriority.High })
 *     .withRequest({ to: '', subject: '', body: '' })
 *     .handle(async (ctx, signal) => {
 *         ctx.request.to; // fully typed
 *     });
 *
 * sdk.function('Cleanup')
 *     .handle(async (ctx, signal) => { });
 * ```
 */
export class TickerFunctionBuilder<TRequest = never> {
    private readonly functionName: string;
    private readonly options: FunctionOptions;
    private requestDefault: unknown = undefined;
    private hasRequest = false;

    constructor(functionName: string, options?: FunctionOptions) {
        this.functionName = functionName;
        this.options = options ?? {};
    }

    /**
     * Define a typed request payload for this function.
     * The default instance provides type inference AND the example JSON for the Hub.
     *
     * ```ts
     * sdk.function('SendEmail')
     *     .withRequest({ to: '', subject: '', body: '' })
     *     .handle(async (ctx, signal) => {
     *         ctx.request.to; // string
     *     });
     * ```
     */
    withRequest<T>(requestDefault: T): TickerFunctionBuilder<T> {
        const builder = this as unknown as TickerFunctionBuilder<T>;
        builder.requestDefault = requestDefault;
        builder.hasRequest = true;
        return builder;
    }

    /**
     * Register the handler for this function.
     * Ends the builder chain and registers with TickerFunctionProvider.
     */
    handle(
        handler: [TRequest] extends [never]
            ? TickerFunctionHandlerNoRequest
            : TickerFunctionHandler<TRequest>,
    ): void {
        if (this.hasRequest) {
            TickerFunctionProvider.registerFunction(
                this.functionName,
                this.requestDefault,
                handler as TickerFunctionHandler<any>,
                this.options,
            );
        } else {
            TickerFunctionProvider.registerFunction(
                this.functionName,
                handler as TickerFunctionHandlerNoRequest,
                this.options,
            );
        }
    }
}
