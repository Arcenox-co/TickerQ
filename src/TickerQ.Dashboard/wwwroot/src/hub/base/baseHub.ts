import * as signalR from "@microsoft/signalr";
import { getBasePath, getBackendUrl } from '@/utilities/pathResolver';

class BaseHub {
    public connection: signalR.HubConnection;

    constructor() {
        this.connection = this.createConnection();
    }

    private createConnection(): signalR.HubConnection {

        const basePath = getBasePath();
        const backendUrl = getBackendUrl();

        // Get auth token and type lazily when building the connection
        const getAuthInfo = () => {
            try {
                // Check for Bearer token first
                const bearerToken = localStorage.getItem('bearer_token');
                if (bearerToken) {
                    return {
                        type: 'Bearer',
                        token: bearerToken
                    };
                }

                // Fallback to Basic auth
                const basicAuth = localStorage.getItem('auth') || 'ZHVtbXk6ZHVtbXk=';
                return {
                    type: 'Basic',
                    token: basicAuth
                };
            } catch (error) {
                // Could not access auth token, using default Basic auth
                return {
                    type: 'Basic',
                    token: 'ZHVtbXk6ZHVtbXk='
                };
            }
        };

        // Use backend domain for WebSocket if configured, otherwise use base path
        let hubUrl: string;
        if (backendUrl) {
            hubUrl = `${backendUrl}/ticker-notification-hub`;
        } else {
            hubUrl = `${basePath}/ticker-notification-hub`;
        }

        const authInfo = getAuthInfo();
        
        return new signalR.HubConnectionBuilder()
            .withUrl(hubUrl, {
                // Use headers for authentication - supports both Basic and Bearer
                headers: {
                    'Authorization': `${authInfo.type} ${authInfo.token}`
                }
            })
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