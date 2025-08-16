import TickerNotificationHub from './tickerNotificationHub'
import { ref, computed } from 'vue'

class ConnectionManager {
  private isInitialized = ref(false)
  private connectionPromise: Promise<void> | null = null
  private healthCheckInterval: NodeJS.Timeout | null = null
  private connectionStore: any = null

  // Set the connection store (called after Pinia is initialized)
  setConnectionStore(store: any) {
    this.connectionStore = store
  }

  // Check if connection store is available
  private isStoreAvailable(): boolean {
    return !!this.connectionStore
  }

  // Computed property to check if connection is active AND backend is healthy
  readonly isConnected = computed(() => {
    if (!this.connectionStore) return false
    return this.connectionStore.isConnected
  })

  // Computed property to check if connection is active (regardless of backend health)
  readonly isWebSocketConnected = computed(() => {
    if (!this.connectionStore) return false
    return this.connectionStore.isWebSocketConnected
  })

  // Computed property to check connection state
  readonly connectionState = computed(() => {
    if (!this.connectionStore) return 'Disconnected'
    return this.connectionStore.connectionState
  })

  // Computed property to check if currently connecting
  readonly isConnecting = computed(() => {
    if (!this.connectionStore) return false
    return this.connectionStore.isConnecting
  })

  // Initialize connection once for the entire app
  async initializeConnection(): Promise<void> {
    if (this.isInitialized.value) {
      return
    }

    if (this.connectionStore.isConnecting && this.connectionPromise) {
      return this.connectionPromise
    }

    this.connectionStore.setConnecting(true)
    this.connectionPromise = this._establishConnection()
    
    try {
      await this.connectionPromise
      this.isInitialized.value = true
    } catch (error) {
      console.error('Failed to initialize WebSocket connection:', error)
      this.isInitialized.value = false
      this.connectionStore.setBackendHealthy(false)
      throw error
    } finally {
      this.connectionStore.setConnecting(false)
      this.connectionPromise = null
    }
  }

  private async _establishConnection(): Promise<void> {
    try {
      // Add timeout to prevent hanging
      const connectionPromise = TickerNotificationHub.startConnection()
      const timeoutPromise = new Promise((_, reject) => {
        setTimeout(() => reject(new Error('Connection timeout after 10 seconds')), 10000)
      })
      
      await Promise.race([connectionPromise, timeoutPromise])
      
      // Set up connection event handlers
      this._setupConnectionHandlers()
      
      // Start health check after successful connection
      this._startHealthCheck()
    } catch (error) {
      console.error('Failed to establish WebSocket connection:', error)
      // Ensure connecting state is reset on failure
      this.connectionStore.setConnecting(false)
      throw error
    }
  }

  private _setupConnectionHandlers(): void {
    const connection = TickerNotificationHub.connection
    
    if (!connection) {
      return
    }

    // Handle connection state changes
    connection.onclose((error) => {
      this.isInitialized.value = false
      this.connectionStore.setBackendHealthy(false)
      this.connectionStore.setConnecting(false) // Ensure connecting state is reset
      this.connectionStore.setConnectionState('Disconnected')
      this._stopHealthCheck()
      
      // Attempt to reconnect after a delay if it wasn't a manual close
      if (error) {
        setTimeout(() => {
          this._attemptReconnection()
        }, 3000)
      }
    })

    connection.onreconnecting((error) => {
      this.connectionStore.setBackendHealthy(false)
      this.connectionStore.setConnecting(true) // Set connecting state during reconnection
      this.connectionStore.setConnectionState('Connecting')
    })

    connection.onreconnected((connectionId) => {
      this.isInitialized.value = true
      this.connectionStore.setConnecting(false) // Reset connecting state after reconnection
      this.connectionStore.setBackendHealthy(true) // Mark as healthy after successful reconnection
      this.connectionStore.setConnectionState('Connected')
      // Start health check after reconnection
      this._startHealthCheck()
    })
  }

  private async _attemptReconnection(): Promise<void> {
    if (this.connectionStore.isConnecting || this.isInitialized.value) {
      return
    }

    try {
      await this.initializeConnection()
    } catch (error) {
      console.error('Reconnection failed:', error)
      // Schedule another attempt
      setTimeout(() => {
        this._attemptReconnection()
      }, 5000)
    }
  }

  // Get connection status for debugging
  getConnectionStatus() {
    const connection = TickerNotificationHub.connection
    const status = {
      isInitialized: this.isInitialized.value,
      isConnecting: this.connectionStore.isConnecting,
      connectionState: this.connectionStore.connectionState,
      isConnected: this.connectionStore.isConnected,
      signalRState: connection?.state,
      backendHealthy: this.connectionStore.isBackendHealthy,
      lastHealthCheck: this.connectionStore.lastHealthCheck
    }
    
    return status
  }

  // Get raw connection state (bypassing computed properties)
  getRawConnectionState() {
    const connection = TickerNotificationHub.connection
    return {
      rawSignalRState: connection?.state,
      rawBackendHealthy: this.connectionStore._isBackendHealthy,
      rawIsConnecting: this.connectionStore._isConnecting,
      rawIsInitialized: this.isInitialized.value
    }
  }



  // Check connection health
  isConnectionHealthy(): boolean {
    return this.isConnected.value && this.isInitialized.value
  }

  // Force reconnection (useful for debugging or manual recovery)
  async forceReconnection(): Promise<void> {
    // Stop current connection if it exists
    try {
      await TickerNotificationHub.stopConnection()
    } catch (error) {
      console.warn('Error stopping connection during force reconnection:', error)
    }
    
    // Reset states using store
    this.connectionStore.resetConnection()
    this._stopHealthCheck()
    
    // Reinitialize connection
    await this.initializeConnection()
    
    // Wait for connection to fully establish
    await new Promise(resolve => setTimeout(resolve, 2000))
    
    // Force immediate status update
    await this._forceStatusUpdate()
  }

  // Force immediate status update
  private async _forceStatusUpdate(): Promise<void> {
    // Get current connection state
    const connection = TickerNotificationHub.connection
    const currentState = connection?.state
    
    // Update states based on actual connection
    if (currentState === 'Connected') {
      this.connectionStore.setBackendHealthy(true)
      this.connectionStore.setConnectionState('Connected')
      this.isInitialized.value = true
    } else {
      this.connectionStore.setBackendHealthy(false)
      this.connectionStore.setConnectionState('Disconnected')
    }
    
    // Force reactive updates using store method
    this.connectionStore.forceStatusUpdate()
  }

  // Update connection with new auth token
  async updateAuthToken(newAuthToken: string): Promise<void> {
    // Stop current connection
    if (this.isInitialized.value) {
      await TickerNotificationHub.stopConnection()
      this.isInitialized.value = false
    }
    
    // Rebuild the connection with the new auth token
    TickerNotificationHub.rebuildConnection()
    
    // Reinitialize connection
    await this.initializeConnection()
  }

  // Handle app visibility changes
  setupVisibilityHandling(): void {
    document.addEventListener('visibilitychange', () => {
      if (!document.hidden) {
        this._checkConnectionHealth()
      }
    })
  }

  private async _checkConnectionHealth(): Promise<void> {
    if (!this.isConnectionHealthy()) {
      await this._attemptReconnection()
    }
  }

  // Cleanup method for app shutdown
  async cleanup(): Promise<void> {
    this._stopHealthCheck()
    if (this.isInitialized.value) {
      await TickerNotificationHub.stopConnection()
      this.isInitialized.value = false
    }
  }



  // Check if the connection manager is ready to use
  isReady(): boolean {
    return typeof window !== 'undefined' && typeof localStorage !== 'undefined'
  }

  // Start periodic health check
  private _startHealthCheck(): void {
    if (this.healthCheckInterval) {
      clearInterval(this.healthCheckInterval)
    }

    // Perform initial health check
    this._performHealthCheck()

    // Set up periodic health check every 30 seconds
    this.healthCheckInterval = setInterval(() => {
      this._performHealthCheck()
    }, 30000)
  }

  // Stop health check
  private _stopHealthCheck(): void {
    if (this.healthCheckInterval) {
      clearInterval(this.healthCheckInterval)
      this.healthCheckInterval = null
    }
  }

  // Perform backend health check
  private async _performHealthCheck(): Promise<void> {
    try {
      const connection = TickerNotificationHub.connection
      if (connection && connection.state === 'Connected') {
        // Simple health check: if the connection state is 'Connected' and we can access it,
        // consider the backend healthy
        this.connectionStore.setBackendHealthy(true)
        this.connectionStore.setLastHealthCheck(new Date())
      } else {
        this.connectionStore.setBackendHealthy(false)
      }
    } catch (error) {
      this.connectionStore.setBackendHealthy(false)
      this.connectionStore.setLastHealthCheck(new Date())
      console.warn('Backend health check failed:', error)
    }
  }

  // Get backend health status
  getBackendHealthStatus() {
    return {
      isHealthy: this.connectionStore.isBackendHealthy,
      lastCheck: this.connectionStore.lastHealthCheck,
      connectionState: TickerNotificationHub.connection?.state
    }
  }

  // Manual health check (useful for testing)
  async performManualHealthCheck(): Promise<boolean> {
    await this._performHealthCheck()
    return this.connectionStore.isBackendHealthy
  }

  // Force refresh connection status (useful for UI updates)
  async refreshConnectionStatus(): Promise<void> {
    // Perform immediate health check
    await this._performHealthCheck()
    
    // Get current connection state
    const connection = TickerNotificationHub.connection
    const currentState = connection?.state
    
    // Force reactive updates using store method
    this.connectionStore.forceStatusUpdate()
    
    // Also force update the initialized state
    if (currentState === 'Connected') {
      const tempInit = this.isInitialized.value
      this.isInitialized.value = false
      this.isInitialized.value = tempInit
    }
  }

  // Force UI refresh by triggering all computed properties
  forceUIUpdate(): void {
    // Sync current SignalR state with store
    const currentSignalRState = TickerNotificationHub.connection?.state
    if (currentSignalRState) {
      this.connectionStore.setConnectionState(currentSignalRState as 'Disconnected' | 'Connecting' | 'Connected')
    }
    
    // Trigger all computed properties by accessing them
    this.isConnected.value
    this.isWebSocketConnected.value
    this.connectionState.value
  }

  // Reset connecting state (useful for debugging stuck states)
  resetConnectingState(): void {
    this.connectionStore.setConnecting(false)
    this.connectionPromise = null
  }

  // Get detailed connection information for debugging
  getDetailedConnectionInfo() {
    const connection = TickerNotificationHub.connection
    return {
      ...this.getConnectionStatus(),
      connectionId: connection?.connectionId,
      baseUrl: connection?.baseUrl,
      serverTimeoutInMilliseconds: connection?.serverTimeoutInMilliseconds,
      keepAliveIntervalInMilliseconds: connection?.keepAliveIntervalInMilliseconds,
      // Note: lastError property doesn't exist on HubConnection type
      isConnecting: this.connectionStore.isConnecting,
      isInitialized: this.isInitialized.value,
      isBackendHealthy: this.connectionStore.isBackendHealthy
    }
  }
}

// Export as singleton
export const connectionManager = new ConnectionManager()
export default connectionManager 