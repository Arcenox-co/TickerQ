/**
 * Clean, simple SignalR service for TickerQ Dashboard
 */

import * as signalR from '@microsoft/signalr';
import { authService } from './auth';

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export interface SignalRStatus {
  state: ConnectionState;
  authenticated: boolean;
  username?: string;
  error?: string;
}

class SignalRService {
  private connection: signalR.HubConnection | null = null;
  private state: ConnectionState = 'disconnected';
  private listeners: Map<string, Function[]> = new Map();

  /**
   * Initialize and start SignalR connection
   */
  async start(): Promise<void> {
    try {
      if (this.connection) {
        await this.stop();
      }

      this.setState('connecting');
      this.connection = this.createConnection();
      
      // Set up event handlers
      this.setupEventHandlers();
      
      // Start connection
      await this.connection.start();
      this.setState('connected');
      
      console.log('✅ SignalR connected successfully');
      
      // Get connection status
      await this.getStatus();
      
    } catch (error) {
      console.error('❌ SignalR connection failed:', error);
      this.setState('disconnected');
      this.emit('error', error);
      throw error;
    }
  }

  /**
   * Stop SignalR connection
   */
  async stop(): Promise<void> {
    if (this.connection) {
      try {
        await this.connection.stop();
      } catch (error) {
        console.error('Error stopping SignalR:', error);
      }
      this.connection = null;
    }
    this.setState('disconnected');
  }

  /**
   * Join a notification group
   */
  async joinGroup(groupName: string): Promise<void> {
    if (!this.isConnected()) {
      throw new Error('SignalR not connected');
    }
    await this.connection!.invoke('JoinGroup', groupName);
  }

  /**
   * Leave a notification group
   */
  async leaveGroup(groupName: string): Promise<void> {
    if (!this.isConnected()) {
      throw new Error('SignalR not connected');
    }
    await this.connection!.invoke('LeaveGroup', groupName);
  }

  /**
   * Get connection status
   */
  async getStatus(): Promise<void> {
    if (!this.isConnected()) {
      throw new Error('SignalR not connected');
    }
    await this.connection!.invoke('GetStatus');
  }

  /**
   * Add event listener
   */
  on(event: string, callback: Function): void {
    if (!this.listeners.has(event)) {
      this.listeners.set(event, []);
    }
    this.listeners.get(event)!.push(callback);
  }

  /**
   * Remove event listener
   */
  off(event: string, callback?: Function): void {
    if (!callback) {
      this.listeners.delete(event);
      return;
    }
    
    const callbacks = this.listeners.get(event);
    if (callbacks) {
      const index = callbacks.indexOf(callback);
      if (index > -1) {
        callbacks.splice(index, 1);
      }
    }
  }

  /**
   * Get current connection state
   */
  getState(): ConnectionState {
    return this.state;
  }

  /**
   * Check if connected
   */
  isConnected(): boolean {
    return this.state === 'connected' && 
           this.connection?.state === signalR.HubConnectionState.Connected;
  }

  private createConnection(): signalR.HubConnection {
    const config = window.TickerQConfig;
    const basePath = config?.basePath || '/tickerq/dashboard';
    const backendDomain = config?.backendDomain;
    
    // Build hub URL
    let hubUrl: string;
    if (backendDomain) {
      hubUrl = `${backendDomain}/ticker-notification-hub`;
    } else {
      hubUrl = `${basePath}/ticker-notification-hub`;
    }

    // Get access token for authentication
    const accessToken = authService.getAccessToken();
    
    // Build connection URL with auth token if needed
    let connectionUrl = hubUrl;
    if (accessToken) {
      connectionUrl = `${hubUrl}?access_token=${encodeURIComponent(accessToken)}`;
    }

    return new signalR.HubConnectionBuilder()
      .withUrl(connectionUrl, {
        transport: signalR.HttpTransportType.WebSockets
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: retryContext => {
          // Exponential backoff with max 3 retries
          if (retryContext.previousRetryCount >= 3) {
            console.log('🛑 SignalR: Max retry attempts reached');
            return null;
          }
          
          const delay = Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
          console.log(`🔄 SignalR: Retrying in ${delay}ms (attempt ${retryContext.previousRetryCount + 1}/3)`);
          return delay;
        }
      })
      .configureLogging(signalR.LogLevel.Information)
      .build();
  }

  private setupEventHandlers(): void {
    if (!this.connection) return;

    // Connection state events
    this.connection.onreconnecting(() => {
      this.setState('reconnecting');
      console.log('🔄 SignalR reconnecting...');
    });

    this.connection.onreconnected(() => {
      this.setState('connected');
      console.log('✅ SignalR reconnected');
      this.emit('reconnected');
    });

    this.connection.onclose((error) => {
      this.setState('disconnected');
      console.log('❌ SignalR connection closed:', error);
      this.emit('disconnected', error);
    });

    // Server events
    this.connection.on('Status', (status) => {
      console.log('📊 SignalR Status:', status);
      this.emit('status', status);
    });

    this.connection.on('GroupJoined', (groupName) => {
      console.log(`✅ Joined group: ${groupName}`);
      this.emit('groupJoined', groupName);
    });

    this.connection.on('GroupLeft', (groupName) => {
      console.log(`❌ Left group: ${groupName}`);
      this.emit('groupLeft', groupName);
    });

    this.connection.on('Error', (error) => {
      console.error('❌ SignalR Error:', error);
      this.emit('error', error);
    });

    // Ticker notifications
    this.connection.on('TickerUpdate', (data) => {
      this.emit('tickerUpdate', data);
    });

    this.connection.on('TickerCompleted', (data) => {
      this.emit('tickerCompleted', data);
    });

    this.connection.on('TickerFailed', (data) => {
      this.emit('tickerFailed', data);
    });
  }

  private setState(newState: ConnectionState): void {
    if (this.state !== newState) {
      this.state = newState;
      this.emit('stateChanged', newState);
    }
  }

  private emit(event: string, ...args: any[]): void {
    const callbacks = this.listeners.get(event);
    if (callbacks) {
      callbacks.forEach(callback => {
        try {
          callback(...args);
        } catch (error) {
          console.error(`Error in SignalR event handler for ${event}:`, error);
        }
      });
    }
  }
}

// Export singleton instance
export const signalRService = new SignalRService();

