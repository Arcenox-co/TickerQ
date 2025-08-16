// stores/authStore.ts
import { defineStore } from 'pinia';
import { ref, computed, reactive, watch, type Ref } from 'vue';
import { validateCredentials } from '../config/auth.config';

export const useAuthStore = defineStore('auth', () => {
  // 1. Authentication state
  const auth = ref('')
  const isInitialized = ref(false)
  const errorMessage = ref('')
  
  // 2. Reactive credentials for the login form
  const credentials = reactive({
    username: '',
    password: ''
  })

  // 3. Initialize auth state from localStorage
  const initializeAuth = async () => {
    try {
      // Longer delay to ensure loading state is visible and prevent flash
      await new Promise(resolve => setTimeout(resolve, 800))
      
      const storedAuth = localStorage.getItem('auth')
      if (storedAuth) {
        auth.value = storedAuth
      }
    } catch (error) {
      console.error('Failed to initialize auth from localStorage:', error)
    } finally {
      isInitialized.value = true
    }
  }

  // 4. Sync to localStorage and update WebSocket connection
  watch(auth, async (newVal) => {
    if (newVal) {
      localStorage.setItem('auth', newVal)
      // Update WebSocket connection with new auth token
      try {
        // Dynamic import to avoid circular dependency
        const { connectionManager } = await import('../hub/connectionManager')
        await connectionManager.updateAuthToken(newVal)
      } catch (error) {
        console.error('Failed to update WebSocket connection with new auth token:', error)
      }
    } else {
      localStorage.removeItem('auth')
      // Disconnect WebSocket when logging out
      try {
        // Dynamic import to avoid circular dependency
        const { connectionManager } = await import('../hub/connectionManager')
        await connectionManager.cleanup()
      } catch (error) {
        console.error('Failed to disconnect WebSocket on logout:', error)
      }
    }
  })

  // 5. Computed login state - only true if initialized and has auth
  const isLoggedIn = computed(() => isInitialized.value && !!auth.value)

  // 5. Login method
  const login = async () => {
    try {
      // Validate credentials using the config
      const validation = validateCredentials(credentials.username, credentials.password)
      
      if (validation.isValid) {
        const authHeader = btoa(`${credentials.username}:${credentials.password}`);
        auth.value = authHeader;
        errorMessage.value = '';
        return true;
      } else {
        errorMessage.value = validation.error || 'Invalid credentials. Please try again.';
        throw new Error(validation.error || 'Invalid credentials');
      }
    } catch (error) {
      if (!errorMessage.value) {
        errorMessage.value = 'Login failed. Please try again.';
      }
      throw error;
    }
  };

  // 6. Set to localStorage (legacy method)
  const setToLocalStorage = () => {
    const authHeader = btoa(`${credentials.username}:${credentials.password}`);
    auth.value = authHeader;
  };

  // 7. Logout
  const logout = () => {
    auth.value = '';
    credentials.username = '';
    credentials.password = '';
    errorMessage.value = '';
  };

  // 8. Clear error message
  const clearError = () => {
    errorMessage.value = '';
  };

  // 9. Manually update WebSocket connection (useful for testing)
  const updateWebSocketConnection = async () => {
    if (auth.value) {
      try {
        const { connectionManager } = await import('../hub/connectionManager');
        await connectionManager.updateAuthToken(auth.value);
      } catch (error) {
        console.error('Failed to manually update WebSocket connection:', error);
      }
    }
  };

  return {
    auth,
    credentials,
    isLoggedIn,
    isInitialized,
    initializeAuth,
    login,
    setToLocalStorage,
    logout,
    errorMessage,
    clearError,
    updateWebSocketConnection
  }
});