import { TickerTaskPriority } from '../enums';
import { TickerFunctionContext } from '../models/TickerFunctionContext';

/**
 * Handler for a function WITH a typed request payload.
 */
export type TickerFunctionHandler<TRequest> = (
    context: TickerFunctionContext<TRequest>,
    signal: AbortSignal,
) => Promise<void>;

/**
 * Handler for a function WITHOUT a request payload.
 */
export type TickerFunctionHandlerNoRequest = (
    context: TickerFunctionContext,
    signal: AbortSignal,
) => Promise<void>;

/** Internal delegate stored in the registry (always receives unknown request). */
export type TickerFunctionDelegate = (
    context: TickerFunctionContext<unknown>,
    signal: AbortSignal,
) => Promise<void>;

export interface TickerFunctionRegistration {
    cronExpression: string | null;
    priority: TickerTaskPriority;
    delegate: TickerFunctionDelegate;
    maxConcurrency: number;
}

export interface TickerFunctionRequestInfo {
    requestType: string;
    requestExampleJson: string;
}

interface FunctionOptionsBase {
    cronExpression?: string;
    priority?: TickerTaskPriority;
    maxConcurrency?: number;
}

/**
 * Central registry for all ticker functions.
 */
class TickerFunctionProviderImpl {
    private _functions: Map<string, TickerFunctionRegistration> = new Map();
    private _requestInfos: Map<string, TickerFunctionRequestInfo> = new Map();
    private _requestDefaults: Map<string, unknown> = new Map();
    private _frozen = false;

    get tickerFunctions(): ReadonlyMap<string, TickerFunctionRegistration> {
        return this._functions;
    }

    get tickerFunctionRequestInfos(): ReadonlyMap<string, TickerFunctionRequestInfo> {
        return this._requestInfos;
    }

    /**
     * Register a function WITH a typed request.
     * The default instance serves as both the type source AND the example JSON for the Hub.
     *
     * Usage:
     * ```ts
     * provider.registerFunction('SendEmail',
     *     { to: '', subject: '', body: '' },        // ← default instance (infers TRequest)
     *     async (ctx, signal) => {
     *         ctx.request.to;   // ← fully typed
     *     },
     * );
     * ```
     */
    registerFunction<TRequest>(
        functionName: string,
        requestDefault: TRequest,
        handler: TickerFunctionHandler<TRequest>,
        options?: FunctionOptionsBase & { requestType?: string },
    ): void;

    /**
     * Register a function WITHOUT a request payload.
     *
     * Usage:
     * ```ts
     * provider.registerFunction('Cleanup', async (ctx, signal) => {
     *     // no ctx.request
     * });
     * ```
     */
    registerFunction(
        functionName: string,
        handler: TickerFunctionHandlerNoRequest,
        options?: FunctionOptionsBase,
    ): void;

    // ─── Implementation ─────────────────────────────────────────────────

    registerFunction(
        functionName: string,
        requestDefaultOrHandler: unknown | TickerFunctionHandlerNoRequest,
        handlerOrOptions?: TickerFunctionHandler<any> | FunctionOptionsBase,
        maybeOptions?: FunctionOptionsBase & { requestType?: string },
    ): void {
        if (this._frozen) {
            throw new Error(`TickerFunctionProvider is frozen. Cannot register function '${functionName}' after build().`);
        }
        if (this._functions.has(functionName)) {
            throw new Error(`TickerQ: Duplicate function name '${functionName}'. Each function must have a unique name.`);
        }

        let delegate: TickerFunctionDelegate;
        let options: (FunctionOptionsBase & { requestType?: string }) | undefined;
        let requestDefault: unknown = undefined;

        if (typeof requestDefaultOrHandler === 'function') {
            // Overload 2: registerFunction(name, handler, options?)
            delegate = requestDefaultOrHandler as TickerFunctionDelegate;
            options = handlerOrOptions as FunctionOptionsBase | undefined;
        } else {
            // Overload 1: registerFunction(name, requestDefault, handler, options?)
            requestDefault = requestDefaultOrHandler;
            delegate = handlerOrOptions as TickerFunctionDelegate;
            options = maybeOptions;
        }

        this._functions.set(functionName, {
            cronExpression: options?.cronExpression ?? null,
            priority: options?.priority ?? TickerTaskPriority.Normal,
            delegate,
            maxConcurrency: options?.maxConcurrency ?? 0,
        });

        if (requestDefault !== undefined) {
            this._requestDefaults.set(functionName, requestDefault);
            const typeName = options?.requestType
                ?? (typeof requestDefault === 'object' && requestDefault !== null
                    ? requestDefault.constructor?.name ?? 'Object'
                    : typeof requestDefault);
            this._requestInfos.set(functionName, {
                requestType: typeName,
                requestExampleJson: JSON.stringify(requestDefault, null, 2),
            });
        }
    }

    /**
     * Get the stored request default for a function (used to populate ctx.request from raw bytes).
     */
    getRequestDefault(functionName: string): unknown | undefined {
        return this._requestDefaults.get(functionName);
    }

    build(): void {
        this._frozen = true;
    }

    getFunction(functionName: string): TickerFunctionRegistration | undefined {
        return this._functions.get(functionName);
    }

    hasFunction(functionName: string): boolean {
        return this._functions.has(functionName);
    }

    reset(): void {
        this._functions.clear();
        this._requestInfos.clear();
        this._requestDefaults.clear();
        this._frozen = false;
    }
}

/** Singleton instance. */
export const TickerFunctionProvider = new TickerFunctionProviderImpl();
