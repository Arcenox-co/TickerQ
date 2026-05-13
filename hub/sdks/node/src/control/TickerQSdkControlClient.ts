import { TickerSdkOptions, TICKERQ_SDK_CONSTANTS } from '../TickerSdkOptions';
import { sdkControlProto, createCredentials, normalizeGrpcTarget } from '../grpc/GrpcContracts';
import { TickerFunctionProvider } from '../infrastructure/TickerFunctionProvider';
import type { TickerQFunctionSyncService } from '../infrastructure/TickerQFunctionSyncService';
import type { TickerQLogger } from '../logging/TickerQLogger';

const MIN_RECONNECT_DELAY_MS = 1_000;
const MAX_RECONNECT_DELAY_MS = 30_000;

export class TickerQSdkControlClient {
    private readonly options: TickerSdkOptions;
    private readonly syncService: TickerQFunctionSyncService;
    private readonly logger: TickerQLogger | null;
    private stopped = false;
    private stream: any | null = null;
    private heartbeatTimer: NodeJS.Timeout | null = null;
    private reconnectTimer: NodeJS.Timeout | null = null;
    private connecting = false;
    private ready = false;
    private readonly readyWaiters = new Set<{ resolve: () => void; reject: (reason: unknown) => void; timeout: NodeJS.Timeout }>();

    constructor(
        options: TickerSdkOptions,
        syncService: TickerQFunctionSyncService,
        logger?: TickerQLogger,
    ) {
        this.options = options;
        this.syncService = syncService;
        this.logger = logger ?? null;
    }

    async start(timeoutMs = this.options.timeoutMs): Promise<void> {
        this.stopped = false;
        this.connect(0);
        await this.waitUntilReady(timeoutMs);
    }

    stop(): void {
        this.stopped = true;
        this.ready = false;
        this.connecting = false;
        if (this.heartbeatTimer) clearInterval(this.heartbeatTimer);
        if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
        this.stream?.end?.();
        this.stream = null;
        this.rejectReadyWaiters(new Error('TickerQ SDK control stream stopped.'));
    }

    private connect(delayMs: number): void {
        if (this.stopped || this.connecting || this.reconnectTimer) return;

        this.reconnectTimer = setTimeout(() => {
            this.reconnectTimer = null;
            if (this.stopped) return;
            this.connecting = true;
            this.ready = false;

            const SdkControlService = sdkControlProto.tickerq.sdkcontrol.v1.SdkControlService;
            const client = new SdkControlService(
                normalizeGrpcTarget(this.options.hubControlUri),
                createCredentials(this.options.hubControlUri),
                {
                    'grpc.max_receive_message_length': 16 * 1024 * 1024,
                    'grpc.max_send_message_length': 16 * 1024 * 1024,
                },
            );

            const stream = client.connect();
            this.stream = stream;

            stream.on('data', (command: any) => {
                this.handleCommand(command).catch((err) => {
                    this.logger?.warn('TickerQ SDK: Control command failed:', err);
                });
            });
            stream.on('error', (err: Error) => {
                this.logger?.warn('TickerQ SDK: Control stream error:', err);
                this.scheduleReconnect(this.nextReconnectDelay(delayMs));
            });
            stream.on('end', () => this.scheduleReconnect(1_000));

            this.write({
                register: {
                    apiKey: this.options.apiKey ?? '',
                    nodeName: this.options.nodeName,
                    sdkVersion: TICKERQ_SDK_CONSTANTS.SdkVersion,
                },
            }).catch((err) => {
                this.logger?.warn('TickerQ SDK: Failed to register control stream:', err);
                this.scheduleReconnect(this.nextReconnectDelay(delayMs));
            });

            this.startHeartbeat();
        }, delayMs);
    }

    private scheduleReconnect(delayMs: number): void {
        if (this.stopped) return;
        this.connecting = false;
        this.ready = false;
        if (this.heartbeatTimer) clearInterval(this.heartbeatTimer);
        if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
        this.reconnectTimer = null;
        this.stream = null;
        this.connect(delayMs);
    }

    private async handleCommand(command: any): Promise<void> {
        if (command.registerAck) {
            this.connecting = false;
            this.ready = true;
            this.resolveReadyWaiters();
            this.logger?.info(`TickerQ SDK: Control stream registered (${command.registerAck.nodeId}).`);
            return;
        }
        if (command.heartbeat) return;
        if (command.disconnect) {
            this.logger?.warn(`TickerQ SDK: Hub disconnected control stream: ${command.disconnect.reason}`);
            this.stream?.end?.();
            this.scheduleReconnect(1_000);
            return;
        }
        if (command.webhookSignatureUpdated) {
            this.options.webhookSignature = command.webhookSignatureUpdated.signature;
            this.logger?.info('TickerQ SDK: Webhook signature updated by Hub.');
            return;
        }
        if (command.triggerResync) {
            await this.handleTriggerResync(command.triggerResync);
            return;
        }
        if (command.removeFunction) {
            await this.handleRemoveFunction(command.removeFunction);
        }
    }

    private async handleTriggerResync(command: { commandId: string }): Promise<void> {
        try {
            await this.syncService.syncAsync();
            await this.write({ ack: { commandId: command.commandId, success: true, error: '' } });
        } catch (err) {
            await this.write({
                ack: {
                    commandId: command.commandId,
                    success: false,
                    error: err instanceof Error ? err.message : String(err),
                },
            });
        }
    }

    private async handleRemoveFunction(command: { commandId: string; functionName: string }): Promise<void> {
        try {
            TickerFunctionProvider.removeFunction(command.functionName);
            await this.write({ ack: { commandId: command.commandId, success: true, error: '' } });
        } catch (err) {
            await this.write({
                ack: {
                    commandId: command.commandId,
                    success: false,
                    error: err instanceof Error ? err.message : String(err),
                },
            });
        }
    }

    private startHeartbeat(): void {
        if (this.heartbeatTimer) clearInterval(this.heartbeatTimer);
        this.heartbeatTimer = setInterval(() => {
            this.write({ heartbeat: { unixMs: Date.now() } }).catch(() => undefined);
        }, 30_000);
    }

    private async write(event: Record<string, unknown>): Promise<void> {
        if (!this.stream) {
            throw new Error('TickerQ SDK control stream is not connected.');
        }

        await new Promise<void>((resolve, reject) => {
            this.stream.write(event, (err: Error | null | undefined) => {
                if (err) reject(err);
                else resolve();
            });
        });
    }

    private waitUntilReady(timeoutMs: number): Promise<void> {
        if (this.ready) return Promise.resolve();

        return new Promise<void>((resolve, reject) => {
            const waiter = {
                resolve,
                reject,
                timeout: setTimeout(() => {
                    this.readyWaiters.delete(waiter);
                    reject(new Error(`TickerQ SDK control stream did not register within ${timeoutMs}ms.`));
                }, timeoutMs),
            };
            this.readyWaiters.add(waiter);
        });
    }

    private resolveReadyWaiters(): void {
        for (const waiter of this.readyWaiters) {
            clearTimeout(waiter.timeout);
            waiter.resolve();
        }
        this.readyWaiters.clear();
    }

    private rejectReadyWaiters(reason: unknown): void {
        for (const waiter of this.readyWaiters) {
            clearTimeout(waiter.timeout);
            waiter.reject(reason);
        }
        this.readyWaiters.clear();
    }

    private nextReconnectDelay(previousDelayMs: number): number {
        if (previousDelayMs <= 0) return MIN_RECONNECT_DELAY_MS;
        return Math.min(previousDelayMs * 2, MAX_RECONNECT_DELAY_MS);
    }
}
