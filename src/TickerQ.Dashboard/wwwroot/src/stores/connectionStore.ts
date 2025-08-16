import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useConnectionStore = defineStore('connection', () => {
  // State
  const _isConnecting = ref(false)
  const _isBackendHealthy = ref(false)
  const _lastHealthCheck = ref<Date | null>(null)
  const _connectionState = ref<'Disconnected' | 'Connecting' | 'Connected'>('Disconnected')

  // Getters
  const isConnecting = computed(() => _isConnecting.value)
  const isBackendHealthy = computed(() => _isBackendHealthy.value)
  const lastHealthCheck = computed(() => _lastHealthCheck.value)
  const connectionState = computed(() => _connectionState.value)

  // Computed properties for connection status
  const isConnected = computed(() => {
    return _connectionState.value === 'Connected' && _isBackendHealthy.value
  })

  const isWebSocketConnected = computed(() => {
    return _connectionState.value === 'Connected'
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

  return {
    // State
    _isConnecting,
    _isBackendHealthy,
    _lastHealthCheck,
    _connectionState,
    
    // Getters
    isConnecting,
    isBackendHealthy,
    lastHealthCheck,
    connectionState,
    isConnected,
    isWebSocketConnected,
    
    // Actions
    setConnecting,
    setBackendHealthy,
    setConnectionState,
    setLastHealthCheck,
    resetConnection,
    forceStatusUpdate
  }
}) 