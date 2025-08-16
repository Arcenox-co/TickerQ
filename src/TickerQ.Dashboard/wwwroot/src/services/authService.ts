import { useAuthStore } from '../stores/authStore'
import { validateCredentials } from '../config/auth.config'

export interface AuthCredentials {
  username: string
  password: string
}

export interface AuthResult {
  success: boolean
  error?: string
}

export class AuthService {
  /**
   * Get the auth store instance
   */
  private getAuthStore() {
    return useAuthStore()
  }

  /**
   * Authenticate user with username and password
   */
  async login(credentials: AuthCredentials): Promise<AuthResult> {
    try {
      // Validate credentials first
      const validation = validateCredentials(credentials.username, credentials.password)
      
      if (!validation.isValid) {
        return {
          success: false,
          error: validation.error || 'Invalid credentials'
        }
      }

      const authStore = this.getAuthStore()

      // Update store credentials
      authStore.credentials.username = credentials.username
      authStore.credentials.password = credentials.password

      // Attempt login
      await authStore.login()
      
      return { success: true }
    } catch (error) {
      // Get the error message from the store if available
      const storeErrorMessage = this.getAuthStore().errorMessage
      const errorMessage = storeErrorMessage || (error instanceof Error ? error.message : 'Login failed')
      
      return {
        success: false,
        error: errorMessage
      }
    }
  }

  /**
   * Logout current user
   */
  async logout(): Promise<void> {
    try {
      const authStore = this.getAuthStore()
      authStore.logout()
    } catch (error) {
      // Failed to update WebSocket connection
    }
  }

  /**
   * Check if user is currently authenticated
   */
  isAuthenticated(): boolean {
    const authStore = this.getAuthStore()
    return authStore.isLoggedIn
  }

  /**
   * Get current username
   */
  getCurrentUsername(): string {
    const authStore = this.getAuthStore()
    return authStore.credentials.username
  }

  /**
   * Get current auth token
   */
  getAuthToken(): string {
    const authStore = this.getAuthStore()
    return authStore.auth
  }

  /**
   * Clear any error messages
   */
  clearError(): void {
    const authStore = this.getAuthStore()
    authStore.clearError()
  }

  /**
   * Get current error message
   */
  getErrorMessage(): string {
    const authStore = this.getAuthStore()
    return authStore.errorMessage
  }

  /**
   * Update WebSocket connection with current auth token
   */
  async updateWebSocketConnection(): Promise<void> {
    try {
      const authStore = this.getAuthStore()
      await authStore.updateWebSocketConnection()
    } catch (error) {
      // Failed to update WebSocket connection
    }
  }
}

// Export singleton instance
export const authService = new AuthService() 