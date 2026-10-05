import type { IncomingMessage, ServerResponse } from 'http';
import { createHash, randomUUID } from 'crypto';
import { generateResponseSignature, validateSignature } from '../utils/TickerQSignature';
import { TickerSdkOptions } from '../TickerSdkOptions';
import { TickerFunctionProvider } from '../infrastructure/TickerFunctionProvider';
import { TickerQFunctionSyncService } from '../infrastructure/TickerQFunctionSyncService';
import { TickerQTaskScheduler } from '../worker/TickerQTaskScheduler';
import { TickerFunctionConcurrencyGate } from '../worker/TickerFunctionConcurrencyGate';
import { TickerQRemotePersistenceProvider } from '../persistence/TickerQRemotePersistenceProvider';
import { normalizeExecutionContext, type RemoteExecutionContext } from '../models/RemoteExecutionContext';
import { createFunctionContext, type FunctionResultSink, type TickerFunctionContext } from '../models/TickerFunctionContext';
import type { TickerFunctionResultContractInfo } from '../infrastructure/TickerFunctionProvider';
import type { InternalFunctionContext } from '../models/InternalFunctionContext';
import { TickerType, TickerStatus, TickerTaskPriority, RunCondition } from '../enums';
import type { TickerQLogger } from '../client/TickerQSdkHttpClient';

const MaxRequestBodyBytes = 2 * 1024 * 1024;
const MaxRegistryEntries = 10_000;
const MaxReplayEntries = 20_000;
const AuthenticationSkewMs = 5 * 60 * 1000;
// A request timestamp may be one skew interval in the future when first accepted and remains
// valid until one skew interval after that timestamp. Retain from admission for the full span.
const ReplayRetentionMs = 2 * AuthenticationSkewMs;
const FinalizedTtlMs = ReplayRetentionMs;
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
class RequestBodyTooLargeError extends Error {}

type ExecutionIdentity = { tickerType: TickerType; tickerId: string; acquisitionToken: string; dispatchId: string; nodeEpoch: string };
type RegistryState = 'cancelPending' | 'active' | 'settled';
interface ExecutionRecord {
    identity: ExecutionIdentity;
    requestDigest?: string;
    state: RegistryState;
    controller: AbortController;
    completion: Promise<InternalFunctionContext>;
    resolve: (value: InternalFunctionContext) => void;
    outcome?: InternalFunctionContext;
}
interface ReplayEntry { digest: string; identityKey: string; at: number }
interface FinalizedTombstone { identity: ExecutionIdentity; expiresAt: number }

function buildFunctionContext(context: RemoteExecutionContext, resultContract?: TickerFunctionResultContractInfo): TickerFunctionContext<unknown> & { readonly resultSink: FunctionResultSink } {
    return createFunctionContext(context, context.hasRequest ? context.request : undefined,
        resultContract ? { contractId: resultContract.fingerprint, contractType: resultContract.typeName, mediaType: resultContract.mediaType } : undefined);
}
function buildInternalContext(context: RemoteExecutionContext, registration: { priority: TickerTaskPriority; maxConcurrency: number }): InternalFunctionContext {
    return { parametersToUpdate: [], cachedPriority: registration.priority, cachedMaxConcurrency: registration.maxConcurrency,
        functionName: context.functionName, tickerId: context.id, acquisitionToken: context.acquisitionToken,
        parentId: context.parentId, type: context.type, retries: 0, retryCount: context.retryCount,
        status: TickerStatus.InProgress, elapsedTime: 0, exceptionDetails: null,
        executedAt: new Date().toISOString(), retryIntervals: [], releaseLock: false,
        executionTime: context.scheduledFor, runCondition: RunCondition.OnSuccess, timeTickerChildren: [] };
}
function serializeException(err: unknown): string {
    return JSON.stringify(err instanceof Error ? { type: err.constructor.name, message: err.message, stackTrace: err.stack ?? null }
        : { type: 'Unknown', message: String(err), stackTrace: null });
}

/** Exact-byte authenticated callback endpoint with replay-safe retained execution outcomes. */
export class SdkExecutionEndpoint {
    private readonly executions = new Map<string, ExecutionRecord>();
    private readonly finalized = new Map<string, FinalizedTombstone>();
    private readonly replay = new Map<string, ReplayEntry>();
    private stopping = false;
    constructor(private readonly options: TickerSdkOptions, private readonly syncService: TickerQFunctionSyncService,
        private readonly scheduler: TickerQTaskScheduler, private readonly concurrencyGate: TickerFunctionConcurrencyGate,
        private readonly persistenceProvider: TickerQRemotePersistenceProvider, private readonly logger: TickerQLogger | null = null) {}

    createHandler(prefix = ''): (req: IncomingMessage, res: ServerResponse) => void {
        const paths = { [`${prefix}/execute`]: 'execute', [`${prefix}/cancel`]: 'cancel', [`${prefix}/finalize`]: 'finalize', [`${prefix}/resync`]: 'resync' } as const;
        return async (req, res) => {
            if (req.method?.toUpperCase() !== 'POST') { res.writeHead(405); res.end('Method Not Allowed'); return; }
            const kind = paths[req.url as keyof typeof paths];
            if (kind === 'execute') await this.handleNative(req, res, 'execute');
            else if (kind === 'cancel') await this.handleNative(req, res, 'cancel');
            else if (kind === 'finalize') await this.handleNative(req, res, 'finalize');
            else if (kind === 'resync') await this.handleResync(req, res);
            else { res.writeHead(404); res.end('Not Found'); }
        };
    }

    expressHandlers(prefix = ''): {
        execute: (req: any, res: any) => Promise<void>; cancel: (req: any, res: any) => Promise<void>;
        finalize: (req: any, res: any) => Promise<void>; resync: (req: any, res: any) => Promise<void>;
        mount: (app: { post: (path: string, handler: any) => void }) => void;
    } {
        const execute = (req: any, res: any) => this.handleExpress(req, res, 'execute');
        const cancel = (req: any, res: any) => this.handleExpress(req, res, 'cancel');
        const finalize = (req: any, res: any) => this.handleExpress(req, res, 'finalize');
        const resync = (req: any, res: any) => this.handleResync(req, res);
        return { execute, cancel, finalize, resync, mount: app => {
            app.post(`${prefix}/execute`, execute); app.post(`${prefix}/cancel`, cancel);
            app.post(`${prefix}/finalize`, finalize); app.post(`${prefix}/resync`, resync);
        }};
    }

    /** Express json({ verify: endpoint.expressRawBodyCapture }) compatible exact-byte capture. */
    readonly expressRawBodyCapture = (req: any, _res: any, buffer: Buffer): void => { req.rawBody = Buffer.from(buffer); };

    beginShutdown(): void {
        if (this.stopping) return;
        this.stopping = true; this.scheduler.freeze();
        for (const record of this.executions.values()) if (record.state === 'active') record.controller.abort();
        this.scheduler.abortAll();
    }
    async shutdown(timeoutMs = 30_000): Promise<boolean> {
        this.beginShutdown();
        const deadline = Date.now() + Math.max(0, timeoutMs);
        const active = [...this.executions.values()]
            .filter(x => x.state === 'active' || x.state === 'cancelPending')
            .map(x => x.completion.catch(() => undefined));
        const settled = await Promise.race([
            Promise.all(active).then(() => true),
            new Promise<boolean>(resolve => setTimeout(() => resolve(false), Math.max(0, deadline - Date.now()))),
        ]);
        if (!settled) return false;
        const drained = await this.scheduler.waitForRunningTasks(Math.max(0, deadline - Date.now()));
        if (!drained) return false;
        this.scheduler.dispose();
        return true;
    }

    private async handleNative(req: IncomingMessage, res: ServerResponse, kind: 'execute'|'cancel'|'finalize'): Promise<void> {
        let body: Buffer;
        try { body = await readBody(req); }
        catch (e) { if (e instanceof RequestBodyTooLargeError) { res.writeHead(413); res.end('Payload Too Large'); return; } throw e; }
        const path = req.url ?? `/${kind}`;
        const auth = this.authenticate(req.headers as Record<string, any>, path, body);
        if (auth.error) { res.writeHead(401); res.end('Unauthorized'); return; }
        const result = await this.dispatch(kind, body, auth.nonce!, path);
        this.sendSignedNative(res, result.status, path, auth.nonce!, result.body);
    }
    private async handleExpress(req: any, res: any, kind: 'execute'|'cancel'|'finalize'): Promise<void> {
        if (!req.rawBody) { res.status(400).send('Exact rawBody is required; configure express.json({ verify: endpoint.expressRawBodyCapture }).'); return; }
        const body = Buffer.isBuffer(req.rawBody) ? req.rawBody : Buffer.from(req.rawBody);
        if (body.length > MaxRequestBodyBytes) { res.status(413).send('Payload Too Large'); return; }
        const path = req.originalUrl ?? req.url ?? `/${kind}`;
        const auth = this.authenticate(req.headers ?? {}, path, body);
        if (auth.error) { res.status(401).send('Unauthorized'); return; }
        const result = await this.dispatch(kind, body, auth.nonce!, path);
        const timestamp = Math.floor(Date.now() / 1000); const secret = this.options.webhookSignature!;
        res.set('content-type', 'application/json'); res.set('x-response-timestamp', String(timestamp));
        res.set('x-request-nonce', auth.nonce!); res.set('x-tickerq-signature', generateResponseSignature(secret, result.status, path, timestamp, auth.nonce!, result.body));
        res.status(result.status).send(result.body);
    }

    private authenticate(headers: Record<string, any>, path: string, body: Buffer): { error?: string; nonce?: string } {
        const header = (name: string): string | undefined => { const value = headers[name] ?? headers[name.toLowerCase()]; return Array.isArray(value) ? value[0] : value?.toString(); };
        const nonce = header('x-request-nonce');
        if (!nonce || !uuid.test(nonce) || zeroUuid(nonce)) return { error: 'Missing or invalid request nonce.' };
        const normalizedNonce = nonce.toLowerCase();
        const error = validateSignature(this.options.webhookSignature, 'POST', path, header('x-timestamp'), header('x-tickerq-signature'), body, normalizedNonce);
        if (error) return { error };
        return { nonce: normalizedNonce };
    }

    private async dispatch(kind: 'execute'|'cancel'|'finalize', body: Buffer, nonce: string, path: string): Promise<{ status: number; body: Buffer }> {
        this.cleanup();
        let raw: Record<string, unknown>;
        try { raw = JSON.parse(body.toString('utf8')); } catch { return response(400, { error: 'invalid_json' }); }
        let identity: ExecutionIdentity;
        try { identity = normalizeIdentity(raw); } catch { return response(400, { error: 'invalid_identity' }); }
        if (identity.nodeEpoch !== this.options.nodeEpoch.toLowerCase()) {
            if (kind === 'finalize') {
                let requestedControlNonce: string;
                try { requestedControlNonce = readUuid(raw, 'controlNonce', 'ControlNonce'); }
                catch { return response(400, { error: 'invalid_control_nonce' }); }
                return response(409, { error: 'node_epoch_mismatch', controlNonce: requestedControlNonce, identity });
            }
            return response(409, { error: 'node_epoch_mismatch' });
        }
        const key = identityKey(identity); const digest = createHash('sha256').update(body).digest('hex');
        const seen = this.replay.get(nonce);
        if (seen && (seen.digest !== digest || seen.identityKey !== key)) return response(409, { error: 'replay_conflict' });
        if (!seen) {
            // Never create a false negative inside the signature-validity window. Cleanup ran
            // before this check, so a full map consists only of protected nonces and new
            // admission must fail closed rather than evicting one a captured request can reuse.
            if (this.replay.size >= MaxReplayEntries) return response(429, { error: 'replay_capacity' });
            this.replay.set(nonce, { digest, identityKey: key, at: Date.now() });
        }
        if (kind === 'execute') return this.execute(raw, identity, key, digest);
        let controlNonce: string;
        try { controlNonce = readUuid(raw, 'controlNonce', 'ControlNonce'); } catch { return response(400, { error: 'invalid_control_nonce' }); }
        if (kind === 'cancel') return this.cancel(identity, key, controlNonce);
        return this.finalize(identity, key, controlNonce);
    }

    private async execute(raw: Record<string, unknown>, identity: ExecutionIdentity, key: string, digest: string): Promise<{status:number;body:Buffer}> {
        const existing = this.executions.get(key);
        if (existing) {
            if (existing.requestDigest && existing.requestDigest !== digest) return response(409, { error: 'execution_body_conflict' });
            if (!existing.requestDigest) existing.requestDigest = digest;
            if (existing.state === 'cancelPending') {
                let cancelledContext: RemoteExecutionContext;
                try { cancelledContext = normalizeExecutionContext(raw); }
                catch { return this.rejectCancelPending(key, existing, identity, 400, 'invalid_execution'); }
                const registration = TickerFunctionProvider.getFunction(cancelledContext.functionName);
                if (!registration) return this.rejectCancelPending(key, existing, identity, 404, 'function_not_found');
                this.settle(existing, cancelledOutcome(cancelledContext, registration, abortError()));
            }
            const outcome = await existing.completion;
            return response(200, { identity, ...outcome });
        }
        if (this.finalized.has(key)) return response(410, { state: 'finalized', identity });
        if (this.stopping) return response(503, { error: 'shutting_down' });
        if (this.executions.size >= MaxRegistryEntries) return response(503, { error: 'registry_full' });
        let context: RemoteExecutionContext;
        try { context = normalizeExecutionContext(raw); } catch { return response(400, { error: 'invalid_execution' }); }
        const registration = TickerFunctionProvider.getFunction(context.functionName);
        if (!registration) return response(404, { error: 'function_not_found' });
        const expectsRequest = TickerFunctionProvider.getRequestDefault(context.functionName) !== undefined;
        if (expectsRequest !== context.hasRequest) return response(400, { error: expectsRequest ? 'missing_request' : 'unexpected_request' });
        const record = createRecord(identity, 'active'); record.requestDigest = digest;
        this.executions.set(key, record);
        const functionContext = buildFunctionContext(context, registration.resultContract);
        const semaphore = this.concurrencyGate.getSemaphore(context.functionName, registration.maxConcurrency);
        void this.executeInScheduler(context, registration, functionContext, semaphore, record.controller.signal)
            .then(outcome => this.settle(record, outcome), err => this.settle(record, failedOutcome(context, registration, err)));
        const outcome = await record.completion;
        return response(200, { identity, ...outcome });
    }

    private async cancel(identity: ExecutionIdentity, key: string, controlNonce: string): Promise<{status:number;body:Buffer}> {
        let record = this.executions.get(key);
        if (!record) {
            if (this.finalized.has(key)) return response(410, { state: 'finalized', controlNonce, identity });
            if (this.stopping) return response(503, { error: 'shutting_down' });
            if (this.executions.size >= MaxRegistryEntries) return response(503, { error: 'registry_full' });
            record = createRecord(identity, 'cancelPending'); this.executions.set(key, record);
            return response(202, { state: 'cancellation_registered', controlNonce, identity });
        }
        if (record.state === 'cancelPending') return response(202, { state: 'cancellation_registered', controlNonce, identity });
        if (record.state === 'settled') return response(200, { state: record.outcome?.status === TickerStatus.Cancelled ? 'stopped' : 'completed', controlNonce, identity, outcome: record.outcome });
        if (record.state === 'active') record.controller.abort();
        const outcome = await record.completion;
        return response(200, { state: outcome.status === TickerStatus.Cancelled ? 'stopped' : 'completed', controlNonce, identity, outcome });
    }

    private finalize(identity: ExecutionIdentity, key: string, controlNonce: string): {status:number;body:Buffer} {
        const record = this.executions.get(key);
        if (!record) return this.finalized.has(key)
            ? response(200, { state: 'finalized', controlNonce, identity })
            : response(404, { state: 'unknown', controlNonce, identity });
        if (record.state === 'active' || record.state === 'cancelPending') return response(409, { state: 'not_settled', controlNonce, identity });
        // Cleanup ran before dispatch. A full tombstone map therefore contains only entries
        // whose replay protection is still live; retain the settled record and retry later.
        if (!this.finalized.has(key) && this.finalized.size >= MaxReplayEntries)
            return response(503, { error: 'finalized_capacity' });
        this.executions.delete(key);
        this.finalized.set(key, { identity, expiresAt: Date.now() + FinalizedTtlMs });
        return response(200, { state: 'finalized', controlNonce, identity });
    }

    private rejectCancelPending(key: string, record: ExecutionRecord, identity: ExecutionIdentity,
        status: number, error: string): {status:number;body:Buffer} {
        // The pending record itself is same-epoch proof that execution never started. Remove it
        // before producing the signed rejection so response loss leaves no unresolved registry
        // state; an identical cancel+execute retry safely recreates and rejects it again.
        if (this.executions.get(key) === record) this.executions.delete(key);
        return response(status, { error, state: 'rejected_not_started', identity });
    }

    private settle(record: ExecutionRecord, outcome: InternalFunctionContext): void {
        if (record.state === 'settled') return;
        record.state = 'settled'; record.outcome = outcome; record.resolve(outcome);
    }
    private executeInScheduler(context: RemoteExecutionContext, registration: any, functionContext: any, semaphore: any, signal: AbortSignal): Promise<InternalFunctionContext> {
        return new Promise((resolve, reject) => {
            this.scheduler.queueAsync(async schedulerSignal => {
                const linked = linkAbortSignals(schedulerSignal, signal);
                resolve(await this.executeAndReturnOutcome(context, registration, functionContext, semaphore, linked.signal));
            }, registration.priority, signal).catch(err => {
                if (signal.aborted) resolve(cancelledOutcome(context, registration, err)); else reject(err);
            });
        });
    }
    private async executeAndReturnOutcome(context: RemoteExecutionContext, registration: any, functionContext: any, semaphore: any, signal: AbortSignal): Promise<InternalFunctionContext> {
        const internal = buildInternalContext(context, registration); const start = performance.now(); let release: (()=>void)|null = null;
        try {
            if (signal.aborted) throw abortError();
            if (semaphore) release = await semaphore.acquire(signal);
            if (signal.aborted) throw abortError();
            await registration.delegate(functionContext, signal);
            if (signal.aborted) throw abortError();
            internal.status = context.isDue ? TickerStatus.DueDone : TickerStatus.Done;
            internal.parametersToUpdate = ['Status','ElapsedTime','ExecutedAt','ResultEnvelope'];
            internal.resultEnvelope = functionContext.resultSink.resultEnvelope ?? null;
        } catch (err) {
            internal.status = signal.aborted || (err instanceof Error && err.name === 'AbortError') ? TickerStatus.Cancelled : TickerStatus.Failed;
            internal.exceptionDetails = serializeException(err);
            internal.parametersToUpdate = ['Status','ElapsedTime','ExecutedAt','ExceptionDetails'];
        } finally { release?.(); internal.elapsedTime = Math.round(performance.now() - start); internal.executedAt = new Date().toISOString(); }
        return internal;
    }

    private sendSignedNative(res: ServerResponse, status: number, path: string, nonce: string, body: Buffer): void {
        const timestamp = Math.floor(Date.now()/1000); const secret = this.options.webhookSignature!;
        res.writeHead(status, { 'content-type': 'application/json', 'x-response-timestamp': String(timestamp), 'x-request-nonce': nonce,
            'x-tickerq-signature': generateResponseSignature(secret, status, path, timestamp, nonce, body) }); res.end(body);
    }
    private cleanup(): void {
        const now = Date.now();
        for (const [key, value] of this.finalized) if (value.expiresAt <= now) this.finalized.delete(key);
        const replayCutoff = now - ReplayRetentionMs;
        for (const [key, value] of this.replay) if (value.at < replayCutoff) this.replay.delete(key);
    }

    private async handleResync(req: any, res: any): Promise<void> {
        let body: Buffer; try { body = req.rawBody ? Buffer.from(req.rawBody) : await readBody(req); } catch { body = Buffer.alloc(0); }
        const path = req.originalUrl ?? req.url ?? '/resync';
        const error = validateSignature(this.options.webhookSignature, 'POST', path, getHeader(req,'x-timestamp'), getHeader(req,'x-tickerq-signature'), body);
        if (error) { send(res, 401, 'Unauthorized'); return; }
        try { await this.syncService.syncAsync(); send(res, 200, 'OK'); } catch { send(res, 500, 'Resync failed'); }
    }
}

function createRecord(identity: ExecutionIdentity, state: RegistryState): ExecutionRecord {
    let resolve!: (value: InternalFunctionContext)=>void;
    const completion = new Promise<InternalFunctionContext>(r => { resolve = r; });
    return { identity, state, controller: new AbortController(), completion, resolve };
}
function response(status: number, value: unknown): {status:number;body:Buffer} { return { status, body: Buffer.from(JSON.stringify(value),'utf8') }; }
function normalizeIdentity(raw: Record<string, unknown>): ExecutionIdentity {
    const tickerType = readConsistentNumber(raw, 'tickerType', 'TickerType', 'type', 'Type');
    if (tickerType !== TickerType.TimeTicker && tickerType !== TickerType.CronTickerOccurrence) throw new TypeError('tickerType');
    return { tickerType, tickerId: readConsistentUuid(raw,'tickerId','TickerId','id','Id'), acquisitionToken: readConsistentUuid(raw,'acquisitionToken','AcquisitionToken'),
        dispatchId: readConsistentUuid(raw,'dispatchId','DispatchId','executionId','ExecutionId'), nodeEpoch: readConsistentUuid(raw,'nodeEpoch','NodeEpoch') };
}
function readUuid(raw: Record<string, unknown>, ...keys: string[]): string {
    let value: unknown; for (const key of keys) if (raw[key] !== undefined) { value = raw[key]; break; }
    if (typeof value !== 'string' || !uuid.test(value) || zeroUuid(value)) throw new TypeError(keys[0]); return value.toLowerCase();
}
function readConsistentUuid(raw: Record<string, unknown>, ...keys: string[]): string {
    const values = keys.filter(key => raw[key] !== undefined).map(key => readUuid(raw, key));
    if (values.length === 0 || values.some(value => value !== values[0])) throw new TypeError(keys[0]);
    return values[0];
}
function readConsistentNumber(raw: Record<string, unknown>, ...keys: string[]): number {
    const values = keys.filter(key => raw[key] !== undefined).map(key => Number(raw[key]));
    if (values.length === 0 || values.some(value => !Number.isInteger(value) || value !== values[0])) throw new TypeError(keys[0]);
    return values[0];
}
function zeroUuid(value: string): boolean { return value.toLowerCase() === '00000000-0000-0000-0000-000000000000'; }
function identityKey(i: ExecutionIdentity): string { return `${i.tickerType}:${i.tickerId}:${i.acquisitionToken}:${i.dispatchId}:${i.nodeEpoch}`; }
function linkAbortSignals(a: AbortSignal,b: AbortSignal): AbortController { const c=new AbortController(); const abort=()=>{if(!c.signal.aborted)c.abort();}; if(a.aborted||b.aborted)abort(); else {a.addEventListener('abort',abort,{once:true});b.addEventListener('abort',abort,{once:true});} return c; }
function abortError(): Error { return Object.assign(new Error('Callback execution was aborted.'),{name:'AbortError'}); }
function cancelledOutcome(context: RemoteExecutionContext, registration: any, err: unknown): InternalFunctionContext { const o=buildInternalContext(context,registration);o.status=TickerStatus.Cancelled;o.exceptionDetails=serializeException(err);o.parametersToUpdate=['Status','ElapsedTime','ExecutedAt','ExceptionDetails'];return o; }
function failedOutcome(context: RemoteExecutionContext, registration: any, err: unknown): InternalFunctionContext { const o=buildInternalContext(context,registration);o.status=TickerStatus.Failed;o.exceptionDetails=serializeException(err);o.parametersToUpdate=['Status','ElapsedTime','ExecutedAt','ExceptionDetails'];return o; }
function readBody(req: IncomingMessage): Promise<Buffer> { return new Promise((resolve,reject)=>{const chunks:Buffer[]=[];let n=0,done=false;req.on('data',(c:Buffer)=>{if(done)return;n+=c.length;if(n>MaxRequestBodyBytes){done=true;reject(new RequestBodyTooLargeError());}else chunks.push(c);});req.on('end',()=>{if(!done)resolve(Buffer.concat(chunks));});req.on('error',e=>{if(!done)reject(e);});}); }
function getHeader(req:any,name:string):string|undefined { const value=req.headers?.[name];return Array.isArray(value)?value[0]:value; }
function send(res:any,status:number,body:string):void { if(typeof res.status==='function')res.status(status).send(body);else{res.writeHead(status);res.end(body);} }
