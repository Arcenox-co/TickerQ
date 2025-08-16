import { computed, ref, watch, readonly } from 'vue'
import { useAuthStore } from '../stores/authStore'
import { authService, type AuthCredentials, type AuthResult } from '../services/authService'

export function useAuth() {
  // Local state
  const isLoading = ref(false)
  const error = ref('')

  // Computed properties - these will be reactive to store changes
  const isAuthenticated = computed(() => {
    try {
      const authStore = useAuthStore()
      return authStore.isLoggedIn
    } catch {
      return false
    }
  })
  
  const username = computed(() => {
    try {
      const authStore = useAuthStore()
      return authStore.credentials.username
    } catch {
      return ''
    }
  })
  
  const authToken = computed(() => {
    try {
      const authStore = useAuthStore()
      return authStore.auth
    } catch {
      return ''
    }
  })
  
  const errorMessage = computed(() => {
    try {
      const authStore = useAuthStore()
      return authStore.errorMessage
    } catch {
      return ''
    }
  })

  // Methods
  const login = async (credentials: AuthCredentials): Promise<AuthResult> => {
    isLoading.value = true
    error.value = ''
    
    try {
      const result = await authService.login(credentials)
      
      if (!result.success) {
        error.value = result.error || 'Login failed'
        // Also update the store error message for consistency
        try {
          const authStore = useAuthStore()
          authStore.errorMessage = result.error || 'Login failed'
        } catch {
          // Store not available, ignore
        }
      } else {
        // Clear any previous errors on success
        error.value = ''
        try {
          const authStore = useAuthStore()
          authStore.clearError()
        } catch {
          // Store not available, ignore
        }
      }
      
      return result
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'An unexpected error occurred'
      error.value = errorMsg
      return { success: false, error: errorMsg }
    } finally {
      isLoading.value = false
    }
  }

  const logout = async (): Promise<void> => {
    isLoading.value = true
    error.value = ''
    
    try {
      await authService.logout()
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Logout failed'
      error.value = errorMsg
      throw err
    } finally {
      isLoading.value = false
    }
  }

  const clearError = (): void => {
    error.value = ''
    try {
      const authStore = useAuthStore()
      authStore.clearError()
    } catch {
      // Store not available, just clear local error
    }
  }

  const updateWebSocketConnection = async (): Promise<void> => {
    try {
      await authService.updateWebSocketConnection()
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Failed to update WebSocket connection'
      error.value = errorMsg
      throw err
    }
  }

  // Auto-clear error after 5 seconds
  const autoClearError = (): void => {
    if (error.value) {
      setTimeout(() => {
        clearError()
      }, 5000)
    }
  }

  // Watch for error changes to auto-clear
  watch(error, autoClearError)
  watch(errorMessage, autoClearError)

  return {
    // State
    isLoading: readonly(isLoading),
    error: readonly(error),
    
    // Computed
    isAuthenticated,
    username,
    authToken,
    errorMessage,
    
    // Methods
    login,
    logout,
    clearError,
    updateWebSocketConnection
  }
} 