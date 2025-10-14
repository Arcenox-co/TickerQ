import { computed, ref, watch, readonly } from 'vue'
import { useAuthStore } from '../stores/authStore'

// Define the types locally
export interface AuthCredentials {
  username: string
  password: string
}

export interface AuthResult {
  success: boolean
  error?: string
}

export function useAuth() {
  // Local state
  const isLoading = ref(false)
  const error = ref('')

  // Get auth store
  const authStore = useAuthStore()

  // Computed properties - these will be reactive to store changes
  const isAuthenticated = computed(() => {
    try {
      return authStore.isLoggedIn
    } catch {
      return false
    }
  })
  
  const username = computed(() => {
    try {
      return authStore.username
    } catch {
      return ''
    }
  })
  
  const authToken = computed(() => {
    try {
      // Return the stored token based on auth mode
      const apiKey = localStorage.getItem('tickerq_api_key')
      const basicAuth = localStorage.getItem('tickerq_basic_auth')
      const hostAccessKey = localStorage.getItem('tickerq_host_access_key')
      return apiKey || basicAuth || hostAccessKey || ''
    } catch {
      return ''
    }
  })
  
  const errorMessage = computed(() => {
    try {
      return authStore.errorMessage || error.value
    } catch {
      return error.value
    }
  })

  // Methods
  const login = async (credentials: AuthCredentials): Promise<AuthResult> => {
    isLoading.value = true
    error.value = ''
    
    try {
      // Set credentials in auth store
      authStore.credentials.username = credentials.username
      authStore.credentials.password = credentials.password
      
      // Attempt login
      const success = await authStore.login()
      
      if (!success) {
        error.value = authStore.errorMessage || 'Invalid username or password'
        return { success: false, error: error.value }
      } else {
        // Clear any previous errors on success
        error.value = ''
        return { success: true }
      }
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
      authStore.logout()
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
      authStore.clearError()
    } catch {
      // Store not available, just clear local error
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
    clearError
  }
}