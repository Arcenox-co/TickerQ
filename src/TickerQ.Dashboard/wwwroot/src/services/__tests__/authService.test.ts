import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { AuthService } from '../authService'

// Mock the auth store
vi.mock('../../stores/authStore', () => ({
  useAuthStore: vi.fn()
}))

describe('AuthService', () => {
  let authService: AuthService
  let mockAuthStore: any

  beforeEach(() => {
    // Create a fresh Pinia instance for each test
    const pinia = createPinia()
    setActivePinia(pinia)
    
    // Reset mocks
    vi.clearAllMocks()
    
    // Create mock store
    mockAuthStore = {
      credentials: {
        username: '',
        password: ''
      },
      auth: '',
      isLoggedIn: false,
      errorMessage: '',
      login: vi.fn(),
      logout: vi.fn(),
      clearError: vi.fn(),
      updateWebSocketConnection: vi.fn()
    }
    
    // Mock the useAuthStore function
    const { useAuthStore } = require('../../stores/authStore')
    useAuthStore.mockReturnValue(mockAuthStore)
    
    // Create new service instance
    authService = new AuthService()
  })

  describe('login', () => {
    it('should successfully login with valid credentials', async () => {
      const credentials = { username: 'testuser', password: 'testpass' }
      mockAuthStore.login.mockResolvedValue(undefined)
      
      const result = await authService.login(credentials)
      
      expect(result.success).toBe(true)
      expect(mockAuthStore.credentials.username).toBe('testuser')
      expect(mockAuthStore.credentials.password).toBe('testpass')
      expect(mockAuthStore.login).toHaveBeenCalled()
    })

    it('should return error for missing username', async () => {
      const credentials = { username: '', password: 'testpass' }
      
      const result = await authService.login(credentials)
      
      expect(result.success).toBe(false)
      expect(result.error).toBe('Username and password are required')
    })

    it('should return error for missing password', async () => {
      const credentials = { username: 'testuser', password: '' }
      
      const result = await authService.login(credentials)
      
      expect(result.success).toBe(false)
      expect(result.error).toBe('Username and password are required')
    })

    it('should handle login errors', async () => {
      const credentials = { username: 'testuser', password: 'testpass' }
      const error = new Error('Invalid credentials')
      mockAuthStore.login.mockRejectedValue(error)
      
      const result = await authService.login(credentials)
      
      expect(result.success).toBe(false)
      expect(result.error).toBe('Invalid credentials')
    })
  })

  describe('logout', () => {
    it('should successfully logout', async () => {
      mockAuthStore.logout.mockResolvedValue(undefined)
      
      await authService.logout()
      
      expect(mockAuthStore.logout).toHaveBeenCalled()
    })

    it('should handle logout errors gracefully', async () => {
      const error = new Error('Logout failed')
      mockAuthStore.logout.mockRejectedValue(error)
      
      // Should not throw
      await expect(authService.logout()).resolves.toBeUndefined()
    })
  })

  describe('state queries', () => {
    it('should return authentication status', () => {
      mockAuthStore.isLoggedIn = true
      expect(authService.isAuthenticated()).toBe(true)
      
      mockAuthStore.isLoggedIn = false
      expect(authService.isAuthenticated()).toBe(false)
    })

    it('should return current username', () => {
      mockAuthStore.credentials.username = 'testuser'
      expect(authService.getCurrentUsername()).toBe('testuser')
    })

    it('should return auth token', () => {
      mockAuthStore.auth = 'test-token'
      expect(authService.getAuthToken()).toBe('test-token')
    })

    it('should return error message', () => {
      mockAuthStore.errorMessage = 'Test error'
      expect(authService.getErrorMessage()).toBe('Test error')
    })
  })

  describe('error handling', () => {
    it('should clear errors', () => {
      authService.clearError()
      expect(mockAuthStore.clearError).toHaveBeenCalled()
    })

    it('should update WebSocket connection', async () => {
      mockAuthStore.updateWebSocketConnection.mockResolvedValue(undefined)
      
      await authService.updateWebSocketConnection()
      
      expect(mockAuthStore.updateWebSocketConnection).toHaveBeenCalled()
    })

    it('should handle WebSocket connection errors', async () => {
      const error = new Error('Connection failed')
      mockAuthStore.updateWebSocketConnection.mockRejectedValue(error)
      
      // Should not throw
      await expect(authService.updateWebSocketConnection()).resolves.toBeUndefined()
    })
  })
}) 