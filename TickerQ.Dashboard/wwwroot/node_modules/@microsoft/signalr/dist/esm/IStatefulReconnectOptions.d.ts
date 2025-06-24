/** Options provided to the 'withStatefulReconnect' method on {@link @microsoft/signalr.HubConnectionBuilder} to configure options for Stateful Reconnect. */
export interface IStatefulReconnectOptions {
    /** Amount of bytes we'll buffer when using Stateful Reconnect until applying backpressure to sends from the client. */
    bufferSize: number;
}
