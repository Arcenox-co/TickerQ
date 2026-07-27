import { TickerTaskPriority } from '../enums';
import type { TickerFunctionContext } from '../models/TickerFunctionContext';
import {
    TickerFunctionProvider,
    type TickerFunctionHandler,
    type TickerFunctionHandlerNoRequest,
    type TickerRequestContractDefinition,
    type TypedFunctionOptions,
} from './TickerFunctionProvider';

export interface FunctionOptions extends TypedFunctionOptions {}

/**
 * Fluent builder for registering a TickerQ function.
 *
 * ```ts
 * sdk.function('SendEmail', { priority: TickerTaskPriority.High })
 *     .withRequest({ to: '', subject: '', body: '' }, emailContract)
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
     *     .withRequest({ to: '', subject: '', body: '' }, emailContract)
     *     .handle(async (ctx, signal) => {
     *         ctx.request.to; // string
     *     });
     * ```
     */
    withRequest<T>(
        requestDefault: T,
        requestContract: TickerRequestContractDefinition,
    ): TickerFunctionBuilder<T> {
        const builder = this as unknown as TickerFunctionBuilder<T>;
        builder.requestDefault = requestDefault;
        builder.hasRequest = true;
        builder.options.requestContract = requestContract;
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
                this.options as FunctionOptions & { requestContract: TickerRequestContractDefinition },
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
