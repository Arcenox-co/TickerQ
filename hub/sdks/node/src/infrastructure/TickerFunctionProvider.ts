import { TickerTaskPriority } from '../enums';
import { TickerFunctionContext } from '../models/TickerFunctionContext';
import { canonicalizeSchema, computeContractFingerprint, SCHEMA_DIALECT_2020_12 } from './CanonicalContract';

/**
 * Handler for a function WITH a typed request payload.
 */
export type TickerFunctionHandler<TRequest, TResult = unknown> = (
    context: TickerFunctionContext<TRequest, TResult>,
    signal: AbortSignal,
) => Promise<void>;

/**
 * Handler for a function WITHOUT a request payload.
 */
export type TickerFunctionHandlerNoRequest<TResult = unknown> = (
    context: TickerFunctionContext<never, TResult>,
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
    resultContract?: TickerFunctionResultContractInfo;
}

export interface TickerRequestExampleDefinition {
    key: string;
    summary?: string;
    value: unknown;
}

export interface TickerRequestContractDefinition {
    contractVersion?: number;
    mediaType?: string;
    required?: boolean;
    schemaDialect?: string;
    schema: Record<string, unknown>;
    examples?: readonly TickerRequestExampleDefinition[];
}

/** Result contracts share the canonical request-contract schema/fingerprint algorithm. */
export interface TickerResultContractDefinition extends TickerRequestContractDefinition {}

export interface TickerFunctionRequestExampleInfo {
    key: string;
    summary?: string;
    valueJson: string;
}

export interface TickerFunctionRequestContractInfo {
    typeName: string;
    mediaType: string;
    required: boolean;
    schemaDialect: string;
    schemaJson: string;
    fingerprint: string;
    examples: readonly TickerFunctionRequestExampleInfo[];
}

export interface TickerFunctionRequestInfo {
    requestType: string;
    requestExampleJson: string;
    contractVersion: number;
    requestContract?: TickerFunctionRequestContractInfo;
}

export interface TickerFunctionResultContractInfo extends TickerFunctionRequestContractInfo {}

export interface TickerFunctionResultInfo {
    resultType: string;
    contractVersion: number;
    resultContract: TickerFunctionResultContractInfo;
}

export interface FunctionOptionsBase {
    cronExpression?: string;
    priority?: TickerTaskPriority;
    maxConcurrency?: number;
}

export interface TypedFunctionOptions extends FunctionOptionsBase {
    requestType?: string;
    requestContract?: TickerRequestContractDefinition;
    resultType?: string;
    resultContract?: TickerResultContractDefinition;
}

/** Central registry for all ticker functions. */
class TickerFunctionProviderImpl {
    private _functions: Map<string, TickerFunctionRegistration> = new Map();
    private _requestInfos: Map<string, TickerFunctionRequestInfo> = new Map();
    private _requestDefaults: Map<string, unknown> = new Map();
    private _resultInfos: Map<string, TickerFunctionResultInfo> = new Map();
    private _frozen = false;

    get tickerFunctions(): ReadonlyMap<string, TickerFunctionRegistration> { return this._functions; }
    get tickerFunctionRequestInfos(): ReadonlyMap<string, TickerFunctionRequestInfo> { return this._requestInfos; }
    get tickerFunctionResultInfos(): ReadonlyMap<string, TickerFunctionResultInfo> { return this._resultInfos; }

    registerFunction<TRequest>(
        functionName: string,
        requestDefault: TRequest,
        handler: TickerFunctionHandler<TRequest>,
        options: TypedFunctionOptions & { requestContract: TickerRequestContractDefinition },
    ): void;

    registerFunction(
        functionName: string,
        handler: TickerFunctionHandlerNoRequest,
        options?: FunctionOptionsBase,
    ): void;

    registerFunction(
        functionName: string,
        requestDefaultOrHandler: unknown | TickerFunctionHandlerNoRequest,
        handlerOrOptions?: TickerFunctionHandler<any> | FunctionOptionsBase,
        maybeOptions?: TypedFunctionOptions,
    ): void {
        if (this._frozen) {
            throw new Error(`TickerFunctionProvider is frozen. Cannot register function '${functionName}' after build().`);
        }
        if (this._functions.has(functionName)) {
            throw new Error(`TickerQ: Duplicate function name '${functionName}'. Each function must have a unique name.`);
        }

        let delegate: TickerFunctionDelegate;
        let options: TypedFunctionOptions | undefined;
        let requestDefault: unknown = undefined;

        if (typeof requestDefaultOrHandler === 'function') {
            delegate = requestDefaultOrHandler as TickerFunctionDelegate;
            options = handlerOrOptions as FunctionOptionsBase | undefined;
        } else {
            requestDefault = requestDefaultOrHandler;
            delegate = handlerOrOptions as TickerFunctionDelegate;
            options = maybeOptions;
        }

        // Build (and fully validate) the request info BEFORE any publication so a policy
        // violation leaves the registry untouched. Request-bearing registrations MUST carry a
        // full canonical Draft 2020-12 schema; a schema-less request payload is rejected here,
        // and buildRequestContract rejects a missing/non-object schema or a conflicting dialect.
        let requestInfo: TickerFunctionRequestInfo | undefined;
        if (requestDefault !== undefined) {
            if (!options?.requestContract) {
                throw new Error(
                    `TickerQ: function '${functionName}' declares a request payload but no request contract. ` +
                    'Request-bearing functions must supply a full JSON Schema Draft 2020-12 contract via ' +
                    'options.requestContract (with an object-root schema).',
                );
            }
            const typeName = options?.requestType
                ?? (typeof requestDefault === 'object' && requestDefault !== null
                    ? requestDefault.constructor?.name ?? 'Object'
                    : typeof requestDefault);
            const requestExampleJson = JSON.stringify(requestDefault, null, 2);
            const contractVersion = options.requestContract.contractVersion ?? 1;
            if (!Number.isInteger(contractVersion) || contractVersion <= 0) {
                throw new Error('TickerQ: request contract version must be a positive integer.');
            }

            requestInfo = {
                requestType: typeName,
                requestExampleJson,
                contractVersion,
                requestContract: buildRequestContract(typeName, contractVersion, options.requestContract),
            };
        }

        let resultInfo: TickerFunctionResultInfo | undefined;
        if (options?.resultContract) {
            const resultType = options.resultType ?? 'Object';
            const contractVersion = options.resultContract.contractVersion ?? 1;
            if (!Number.isInteger(contractVersion) || contractVersion <= 0) {
                throw new Error('TickerQ: result contract version must be a positive integer.');
            }
            resultInfo = {
                resultType,
                contractVersion,
                resultContract: buildContract(resultType, contractVersion, options.resultContract, 'result'),
            };
        }

        this._functions.set(functionName, {
            cronExpression: options?.cronExpression ?? null,
            priority: options?.priority ?? TickerTaskPriority.Normal,
            delegate,
            maxConcurrency: options?.maxConcurrency ?? 0,
            resultContract: resultInfo?.resultContract,
        });

        if (requestDefault !== undefined) {
            this._requestDefaults.set(functionName, requestDefault);
            this._requestInfos.set(functionName, requestInfo!);
        }
        if (resultInfo) this._resultInfos.set(functionName, resultInfo);
    }

    getRequestDefault(functionName: string): unknown | undefined { return this._requestDefaults.get(functionName); }
    build(): void { this._frozen = true; }
    getFunction(functionName: string): TickerFunctionRegistration | undefined { return this._functions.get(functionName); }
    hasFunction(functionName: string): boolean { return this._functions.has(functionName); }
    reset(): void {
        this._functions.clear();
        this._requestInfos.clear();
        this._requestDefaults.clear();
        this._resultInfos.clear();
        this._frozen = false;
    }
}

function buildRequestContract(
    typeName: string,
    contractVersion: number,
    definition: TickerRequestContractDefinition,
): TickerFunctionRequestContractInfo {
    return buildContract(typeName, contractVersion, definition, 'request');
}

function buildContract(
    typeName: string,
    contractVersion: number,
    definition: TickerRequestContractDefinition,
    kind: 'request' | 'result',
): TickerFunctionRequestContractInfo {
    if (!definition.schema || Array.isArray(definition.schema) || typeof definition.schema !== 'object') {
        throw new Error(`TickerQ: ${kind} contract schema must have an object root.`);
    }

    const mediaType = definition.mediaType ?? 'application/json';
    const required = definition.required ?? true;
    const schemaDialect = definition.schemaDialect ?? SCHEMA_DIALECT_2020_12;
    if (schemaDialect !== SCHEMA_DIALECT_2020_12) {
        throw new Error(`TickerQ: unsupported ${kind} contract schema dialect '${schemaDialect}'. Only '${SCHEMA_DIALECT_2020_12}' is supported.`);
    }
    const embeddedDialect = definition.schema.$schema;
    if (embeddedDialect !== undefined && embeddedDialect !== SCHEMA_DIALECT_2020_12) {
        throw new Error(`TickerQ: embedded $schema '${String(embeddedDialect)}' conflicts with the supported '${SCHEMA_DIALECT_2020_12}' dialect.`);
    }

    const schemaJson = canonicalizeSchema(definition.schema);
    const fingerprint = computeContractFingerprint(schemaJson, mediaType, required, contractVersion);

    return {
        typeName,
        mediaType,
        required,
        schemaDialect,
        schemaJson,
        fingerprint,
        examples: (definition.examples ?? []).map(example => ({
            key: example.key,
            summary: example.summary,
            valueJson: JSON.stringify(example.value),
        })),
    };
}

export const TickerFunctionProvider = new TickerFunctionProviderImpl();
