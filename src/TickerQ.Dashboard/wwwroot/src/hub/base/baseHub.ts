import * as signalR from "@microsoft/signalr";

const baseTag = document.querySelector<HTMLBaseElement>('base');

class BaseHub {
    public connection: signalR.HubConnection;

    constructor() {
        this.connection = this.createConnection();
    }

    private createConnection(): signalR.HubConnection {
        const basePath = import.meta.env.PROD
            ? baseTag?.href
            : 'http://localhost:5083/tickerq-dashboard';

        // Get auth token lazily when building the connection
        const getAuthToken = () => {
            try {
                // Access localStorage directly instead of using the store during initialization
                return localStorage.getItem('auth') || 'ZHVtbXk6ZHVtbXk=';
            } catch (error) {
                // Could not access auth token, using default
                return 'ZHVtbXk6ZHVtbXk=';
            }
        };

        return new signalR.HubConnectionBuilder()
            .withUrl(`${basePath}/ticker-notification-hub?auth=${encodeURIComponent(getAuthToken())}`)
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Information)
            .build();
    }

    // Method to rebuild connection with new auth token
    public rebuildConnection(): void {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            this.connection.stop();
        }
        this.connection = this.createConnection();
    }

    // Send a message to the server
    protected async sendMessage(methodName: string): Promise<void> {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            try {
                await this.connection.invoke(methodName);
            } catch (err) {
                // Error sending message
            }
        } else {
            // Cannot send message: SignalR connection is not active.
        }
    }

    // Start Connection
    async startConnectionAsync(): Promise<void> {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            return;
        }
        
        if (this.connection.state === signalR.HubConnectionState.Connecting) {
            return;
        }
        
        try {
            await this.connection.start();
        } catch (err) {
            // SignalR Connection Error
            throw err;
        }
    }

    async stopConnectionAsync(): Promise<void> {
        try {
            await this.connection.stop();
        } catch (err) {
            // Error stopping SignalR connection
        }
    }

    joinGroup(groupName: string): void {
        this.connection.invoke("JoinGroup", groupName);
    }

    leaveGroup(groupName: string): void {
        this.connection.invoke("LeaveGroup", groupName);
    }

    // Subscribe to messages from the server
    onReceiveMessageAsSingle<T>(methodName: string, callback: (response: T) => void): void {
        this.connection.on(methodName, (responseFromHub: any) => {
            if (Array.isArray(responseFromHub)) {
                responseFromHub.forEach((response) => {
                    callback(response);
                });
            } else {
                callback(responseFromHub);
            }
        });
    }
}

export default BaseHub;