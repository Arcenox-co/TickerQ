import { IConnection } from "./IConnection";
import { AckMessage, HubMessage, IHubProtocol, SequenceMessage } from "./IHubProtocol";
/** @private */
export declare class MessageBuffer {
    private readonly _protocol;
    private readonly _connection;
    private readonly _bufferSize;
    private _messages;
    private _totalMessageCount;
    private _waitForSequenceMessage;
    private _nextReceivingSequenceId;
    private _latestReceivedSequenceId;
    private _bufferedByteCount;
    private _reconnectInProgress;
    private _ackTimerHandle?;
    constructor(protocol: IHubProtocol, connection: IConnection, bufferSize: number);
    _send(message: HubMessage): Promise<void>;
    _ack(ackMessage: AckMessage): void;
    _shouldProcessMessage(message: HubMessage): boolean;
    _resetSequence(message: SequenceMessage): void;
    _disconnected(): void;
    _resend(): Promise<void>;
    _dispose(error?: Error): void;
    private _isInvocationMessage;
    private _ackTimer;
}
