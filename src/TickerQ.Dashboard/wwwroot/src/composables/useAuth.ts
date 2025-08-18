import { computed, ref, watch, readonly } from 'vue'
import { useAuthStore } from '../stores/authStore'
import { authService } from '../services/authService'

// Define the types locally since they're not exported from authService anymore
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
      return localStorage.getItem('username') || authStore.credentials.username
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
      // Use the new authService method for basic auth login
      const success = await authService.handleBasicAuthLogin(
        credentials.username,
        credentials.password
      )
      
      if (!success) {
        error.value = 'Invalid username or password'
        // Also update the store error message for consistency
        try {
          const authStore = useAuthStore()
          authStore.errorMessage = 'Invalid username or password'
        } catch {
          // Store not available, ignore
        }
        return { success: false, error: 'Invalid username or password' }
      } else {
        // Clear any previous errors on success
        error.value = ''
        try {
          const authStore = useAuthStore()
          authStore.clearError()
        } catch {
          // Store not available, ignore
        }
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
      authService.logout()
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