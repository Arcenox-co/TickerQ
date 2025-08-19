import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import TickerNotificationHub from '../hub/tickerNotificationHub'

export const useConnectionStore = defineStore('connection', () => {
  // State
  const _isConnecting = ref(false)
  const _isBackendHealthy = ref(false)
  const _lastHealthCheck = ref<Date | null>(null)
  const _connectionState = ref<'Disconnected' | 'Connecting' | 'Connected'>('Disconnected')
  const _isInitialized = ref(false)
  const _connectionPromise = ref<Promise<void> | null>(null)
  const _healthCheckInterval = ref<ReturnType<typeof setInterval> | null>(null)

  // Getters
  const isConnecting = computed(() => _isConnecting.value)
  const isBackendHealthy = computed(() => _isBackendHealthy.value)
  const lastHealthCheck = computed(() => _lastHealthCheck.value)
  const connectionState = computed(() => _connectionState.value)
  const isInitialized = computed(() => _isInitialized.value)

  // Computed properties for connection status
  const isConnected = computed(() => {
    return _connectionState.value === 'Connected' && _isBackendHealthy.value
  })

  const isWebSocketConnected = computed(() => {
    return _connectionState.value === 'Connected'
  })

  // Check if the connection manager is ready to use
  const isReady = computed(() => {
    return typeof window !== 'undefined' && typeof localStorage !== 'undefined'
  })

  // Actions
  function setConnecting(value: boolean) {
    _isConnecting.value = value
    if (value) {
      _connectionState.value = 'Connecting'
    }
  }

  function setBackendHealthy(value: boolean) {
    _isBackendHealthy.value = value
    if (value && _connectionState.value === 'Connecting') {
      _connectionState.value = 'Connected'
    }
  }

  function setConnectionState(state: 'Disconnected' | 'Connecting' | 'Connected') {
    _connectionState.value = state
  }

  function setLastHealthCheck(date: Date) {
    _lastHealthCheck.value = date
  }

  function resetConnection() {
    _isConnecting.value = false
    _isBackendHealthy.value = false
    _connectionState.value = 'Disconnected'
    _lastHealthCheck.value = null
    _isInitialized.value = false
    _connectionPromise.value = null
  }

  function forceStatusUpdate() {
    // Force reactive updates by triggering change detection
    const tempConnecting = _isConnecting.value
    _isConnecting.value = false
    _isConnecting.value = tempConnecting

    const tempBackend = _isBackendHealthy.value
    _isBackendHealthy.value = false
    _isBackendHealthy.value = tempBackend

    const tempState = _connectionState.value
    _connectionState.value = 'Disconnected'
    _connectionState.value = tempState
  }

  // Initialize connection once for the entire app
  async function initializeConnection(): Promise<void> {
    if (_isInitialized.value) {
      return
    }

    try {
      await TickerNotificationHub.startConnection()
      _isInitialized.value = true
      setConnectionState('Connected')
      setBackendHealthy(true)
      _startHealthCheck()
    } catch (error) {
      // Failed to initialize WebSocket connection
      setConnectionState('Disconnected')
      setBackendHealthy(false)
      _isInitialized.value = false
    }
  }

  // Initialize connection with retry logic
  async function initializeConnectionWithRetry(maxRetries: number = 3): Promise<void> {
    if (_isInitialized.value) {
      return
    }

    for (let attempt = 1; attempt <= maxRetries; attempt++) {
      try {
        await TickerNotificationHub.startConnection()
        _isInitialized.value = true
        setConnectionState('Connected')
        setBackendHealthy(true)
        _startHealthCheck()
        return
      } catch (error) {
        // Connection attempt failed
        if (attempt === maxRetries) {
          // All connection attempts failed. WebSocket will not be available.
          setConnectionState('Disconnected')
          setBackendHealthy(false)
          _isInitialized.value = false
          return
        }
        
        // Retry after a delay
        const delay = Math.min(1000 * Math.pow(2, attempt - 1), 5000)
        await new Promise(resolve => setTimeout(resolve, delay))
      }
    }
  }

  async function _establishConnection(): Promise<void> {
    try {
      // Add timeout to prevent hanging
      const connectionPromise = TickerNotificationHub.startConnection()
      const timeoutPromise = new Promise((_, reject) => {
        setTimeout(() => reject(new Error('Connection timeout after 10 seconds')), 10000)
      })
      
      await Promise.race([connectionPromise, timeoutPromise])
      
      // Update connection state to connected
      setConnecting(false)
      setConnectionState('Connected')
      setBackendHealthy(true)
      _isInitialized.value = true
      
      // Set up connection event handlers
      _setupConnectionHandlers()
      
      // Sync state with actual SignalR connection
      _syncConnectionState()
      
      // Force UI update to ensure reactivity
      forceUIUpdate()
      
      // Start health check after successful connection
      _startHealthCheck()
    } catch (error) {
      // Ensure connecting state is reset on failure
      setConnecting(false)
      setConnectionState('Disconnected')
      setBackendHealthy(false)
      _isInitialized.value = false
      throw error
    }
  }

  // Sync our state with the actual SignalR connection state
  function _syncConnectionState(): void {
    const connection = TickerNotificationHub.connection
    if (!connection) return
    
    switch (connection.state) {
      case 'Connected':
        setConnectionState('Connected')
        setConnecting(false) // Ensure connecting state is reset
        setBackendHealthy(true)
        _isInitialized.value = true
        break
      case 'Connecting':
        setConnectionState('Connecting')
        setConnecting(true)
        break
      case 'Disconnected':
        setConnectionState('Disconnected')
        setConnecting(false) // Ensure connecting state is reset
        setBackendHealthy(false)
        _isInitialized.value = false
        break
      case 'Reconnecting':
        setConnectionState('Connecting')
        setConnecting(true)
        break
      default:
        // Unknown SignalR connection state
        setConnecting(false) // Ensure connecting state is reset
        setBackendHealthy(false)
        setConnectionState('Disconnected')
    }
  }

  function _setupConnectionHandlers(): void {
    const connection = TickerNotificationHub.connection
    
    if (!connection) {
      return
    }

    // Handle connection state changes
    connection.onclose((error) => {
      _isInitialized.value = false
      setBackendHealthy(false)
      setConnecting(false)
      setConnectionState('Disconnected')
      _stopHealthCheck()
      
      // Attempt to reconnect after a delay if it wasn't a manual close
      if (error) {
        setTimeout(() => {
          _attemptReconnection()
        }, 3000)
      }
    })

    connection.onreconnecting((error) => {
      setBackendHealthy(false)
      setConnecting(true)
      setConnectionState('Connecting')
    })

    connection.onreconnected((connectionId) => {
      _isInitialized.value = true
      setConnecting(false)
      setBackendHealthy(true)
      setConnectionState('Connected')
      // Start health check after reconnection
      _startHealthCheck()
    })

    // Handle initial connection
    connection.on('connected', () => {
      _isInitialized.value = true
      setConnecting(false)
      setBackendHealthy(true)
      setConnectionState('Connected')
      _startHealthCheck()
    })

    // Also set up the 'connected' event for initial connection
    connection.on('connected', () => {
      _isInitialized.value = true
      setConnecting(false)
      setBackendHealthy(true)
      setConnectionState('Connected')
    })
  }

  // Attempt reconnection
  async function _attemptReconnection(): Promise<void> {
    if (_isConnecting.value || _isInitialized.value) {
      return
    }

    _isConnecting.value = true
    try {
      await initializeConnection()
    } catch (error) {
      // Reconnection failed
    } finally {
      _isConnecting.value = false
      
      // Schedule next reconnection attempt
      setTimeout(() => {
        _attemptReconnection()
      }, 5000)
    }
  }

  // Force reconnection (useful for debugging or manual recovery)
  async function forceReconnection(): Promise<void> {
    // Stop current connection if it exists
    try {
      await TickerNotificationHub.stopConnection()
    } catch (error) {
      // Error stopping connection during force reconnection
    }
    
    // Reset states
    resetConnection()
    _stopHealthCheck()
    
    // Reinitialize connection
    await initializeConnection()
    
    // Wait for connection to fully establish
    await new Promise(resolve => setTimeout(resolve, 2000))
    
    // Force immediate status update
    await _forceStatusUpdate()
  }

  // Force immediate status update
  async function _forceStatusUpdate(): Promise<void> {
    // Get current connection state
    const connection = TickerNotificationHub.connection
    const currentState = connection?.state
    
    // Update states based on actual connection
    if (currentState === 'Connected') {
      setBackendHealthy(true)
      setConnectionState('Connected')
      _isInitialized.value = true
    } else {
      setBackendHealthy(false)
      setConnectionState('Disconnected')
    }
    
    // Force reactive updates
    forceStatusUpdate()
  }

  // Update connection with new auth token
  async function updateAuthToken(newAuthToken: string): Promise<void> {
    // Stop current connection
    if (_isInitialized.value) {
      await TickerNotificationHub.stopConnection()
      _isInitialized.value = false
    }
    
    // Rebuild the connection with the new auth token
    TickerNotificationHub.rebuildConnection()
    
    // Reinitialize connection
    await initializeConnection()
  }

  // Handle app visibility changes
  function setupVisibilityHandling(): void {
    document.addEventListener('visibilitychange', () => {
      if (!document.hidden) {
        _checkConnectionHealth()
      }
    })
  }

  // Check connection health
  async function _checkConnectionHealth(): Promise<void> {
    if (!isConnectionHealthy()) {
      await _attemptReconnection()
    }
  }

  // Check connection health
  function isConnectionHealthy(): boolean {
    return isConnected.value && _isInitialized.value
  }

  // Start periodic health check
  function _startHealthCheck(): void {
    if (_healthCheckInterval.value) {
      clearInterval(_healthCheckInterval.value)
    }

    // Perform initial health check
    _performHealthCheck()

    // Set up periodic health check every 30 seconds
    _healthCheckInterval.value = setInterval(() => {
      _performHealthCheck()
    }, 30000)
  }

  // Stop health check
  function _stopHealthCheck(): void {
    if (_healthCheckInterval.value) {
      clearInterval(_healthCheckInterval.value)
      _healthCheckInterval.value = null
    }
  }

  // Perform backend health check
  async function _performHealthCheck(): Promise<void> {
    try {
      const connection = TickerNotificationHub.connection
      if (connection && connection.state === 'Connected') {
        // Simple health check: if the connection state is 'Connected' and we can access it,
        // consider the backend healthy
        setBackendHealthy(true)
        setLastHealthCheck(new Date())
        
        // Also sync our state with the actual connection state
        _syncConnectionState()
      } else {
        setBackendHealthy(false)
        // Sync state even when not connected
        _syncConnectionState()
      }
    } catch (error) {
      setBackendHealthy(false)
      setLastHealthCheck(new Date())
    }
  }

  // Manual health check (useful for testing)
  async function performManualHealthCheck(): Promise<boolean> {
    await _performHealthCheck()
    return _isBackendHealthy.value
  }

  // Manual state sync (useful for debugging)
  function syncConnectionState(): void {
    _syncConnectionState()
  }

  // Force refresh connection status (useful for UI updates)
  async function refreshConnectionStatus(): Promise<void> {
    // Perform immediate health check
    await _performHealthCheck()
    
    // Get current connection state
    const connection = TickerNotificationHub.connection
    const currentState = connection?.state
    
    // Force reactive updates
    forceStatusUpdate()
    
    // Also force update the initialized state
    if (currentState === 'Connected') {
      const tempInit = _isInitialized.value
      _isInitialized.value = false
      _isInitialized.value = tempInit
    }
  }

  // Force UI refresh by triggering all computed properties
  function forceUIUpdate(): void {
    // Sync current SignalR state with store
    const currentSignalRState = TickerNotificationHub.connection?.state
    if (currentSignalRState) {
      setConnectionState(currentSignalRState as 'Disconnected' | 'Connecting' | 'Connected')
    }
    
    // Trigger all computed properties by accessing them
    isConnected.value
    isWebSocketConnected.value
    connectionState.value
    
    // Force a reactive update by temporarily changing and restoring values
    const tempConnecting = _isConnecting.value
    _isConnecting.value = !tempConnecting
    _isConnecting.value = tempConnecting
    
    const tempState = _connectionState.value
    _connectionState.value = 'Disconnected'
    _connectionState.value = tempState
    
    const tempHealthy = _isBackendHealthy.value
    _isBackendHealthy.value = !tempHealthy
    _isBackendHealthy.value = tempHealthy
  }

  // Reset connecting state (useful for debugging stuck states)
  function resetConnectingState(): void {
    setConnecting(false)
    _connectionPromise.value = null
  }

  // Get connection status for debugging
  function getConnectionStatus() {
    const connection = TickerNotificationHub.connection
    const status = {
      isInitialized: _isInitialized.value,
      isConnecting: _isConnecting.value,
      connectionState: _connectionState.value,
      isConnected: isConnected.value,
      signalRState: connection?.state,
      backendHealthy: _isBackendHealthy.value,
      lastHealthCheck: _lastHealthCheck.value
    }
    
    return status
  }

  // Get raw connection state (bypassing computed properties)
  function getRawConnectionState() {
    const connection = TickerNotificationHub.connection
    return {
      rawSignalRState: connection?.state,
      rawBackendHealthy: _isBackendHealthy.value,
      rawIsConnecting: _isConnecting.value,
      rawIsInitialized: _isInitialized.value
    }
  }

  // Get backend health status
  function getBackendHealthStatus() {
    return {
      isHealthy: _isBackendHealthy.value,
      lastCheck: _lastHealthCheck.value,
      connectionState: TickerNotificationHub.connection?.state
    }
  }

  // Get detailed connection information for debugging
  function getDetailedConnectionInfo() {
    const connection = TickerNotificationHub.connection
    return {
      ...getConnectionStatus(),
      connectionId: connection?.connectionId,
      baseUrl: connection?.baseUrl,
      serverTimeoutInMilliseconds: connection?.serverTimeoutInMilliseconds,
      keepAliveIntervalInMilliseconds: connection?.keepAliveIntervalInMilliseconds,
      isConnecting: _isConnecting.value,
      isInitialized: _isInitialized.value,
      isBackendHealthy: _isBackendHealthy.value
    }
  }

  // Cleanup method for app shutdown
  async function cleanup(): Promise<void> {
    _stopHealthCheck()
    if (_isInitialized.value) {
      await TickerNotificationHub.stopConnection()
      _isInitialized.value = false
    }
  }

  return {
    // State
    _isConnecting,
    _isBackendHealthy,
    _lastHealthCheck,
    _connectionState,
    _isInitialized,
    
    // Getters
    isConnecting,
    isBackendHealthy,
    lastHealthCheck,
    connectionState,
    isConnected,
    isWebSocketConnected,
    isInitialized,
    isReady,
    
    // Actions
    setConnecting,
    setBackendHealthy,
    setConnectionState,
    setLastHealthCheck,
    resetConnection,
    forceStatusUpdate,
    initializeConnection,
    initializeConnectionWithRetry,
    forceReconnection,
    updateAuthToken,
    setupVisibilityHandling,
    isConnectionHealthy,
    performManualHealthCheck,
    refreshConnectionStatus,
    forceUIUpdate,
    resetConnectingState,
    getConnectionStatus,
    getRawConnectionState,
    getBackendHealthStatus,
    getDetailedConnectionInfo,
    cleanup,
    syncConnectionState
  }
}) 