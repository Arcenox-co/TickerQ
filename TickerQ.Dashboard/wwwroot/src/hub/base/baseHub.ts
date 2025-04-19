import { useAuthStore } from "@/stores/authStore";
import * as signalR from "@microsoft/signalr";

const authStore = useAuthStore();

const baseTag = document.querySelector<HTMLBaseElement>('base');


class BaseHub {
    protected connection: signalR.HubConnection;

    constructor() {
        const basePath = import.meta.env.PROD
            ? baseTag?.href
            : 'http://localhost:5079/tickerq-dashboard';

        this.connection = new signalR.HubConnectionBuilder()
            .withUrl(`${basePath}/ticker-notification-hub?auth=${encodeURIComponent(authStore.auth)}`)
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Information)
            .build();
    }

    // Send a message to the server
    protected async sendMessage(methodName: string): Promise<void> {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            try {
                await this.connection.invoke(methodName);
            } catch (err) {
                console.error("Error sending message: ", err);
            }
        } else {
            console.warn("Cannot send message: SignalR connection is not active.");
        }
    }

    // Start Connection
    async startConnectionAsync(): Promise<void> {
        if (this.connection.state == signalR.HubConnectionState.Connected)
            return;
        try {
            await this.connection.start();
            console.log("Connected to SignalR");
        } catch (err) {
            console.error("SignalR Connection Error: ", err);
            setTimeout(() => this.startConnectionAsync(), 5000);
        }
    }

    async stopConnectionAsync(): Promise<void> {
        try {
            await this.connection.stop();
            console.log("Disconnected from SignalR");
        } catch (err) {
            console.error("Error stopping SignalR connection: ", err);
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