import { defineStore } from 'pinia';
import { ref, computed, reactive } from 'vue';
import { authService, type AuthStatus, type LoginCredentials } from '@/services/auth';

export const useAuthStore = defineStore('auth', () => {
  // State
  const isInitialized = ref(false);
  const authStatus = ref<AuthStatus>({
    authenticated: false,
    username: '',
    message: ''
  });
  const errorMessage = ref('');
  const isLoading = ref(false);
  const forceUpdate = ref(0); // Force reactivity trigger

  // Form credentials
  const credentials = reactive<LoginCredentials>({
    username: '',
    password: '',
    apiKey: '',
    hostAccessKey: ''
  });

  // Computed properties
  const isLoggedIn = computed(() => {
    forceUpdate.value; // Force reactivity
    if (!isInitialized.value) return false;
    return authStatus.value.authenticated;
  });

  const username = computed(() => authStatus.value.username || '');

  // Actions
  const initializeAuth = async () => {
    try {
      isLoading.value = true;
      
      // Initialize the auth service
      await authService.initialize();
      
      // Get the current auth status
      authStatus.value = authService.getStatus();
      
    } catch (error) {
      console.error('Auth initialization failed:', error);
      authStatus.value = {
        authenticated: false,
        username: '',
        message: 'Authentication service unavailable'
      };
      errorMessage.value = 'Failed to initialize authentication';
    } finally {
      isInitialized.value = true;
      isLoading.value = false;
    }
  };

  const login = async (): Promise<boolean> => {
    try {
      isLoading.value = true;
      errorMessage.value = '';
      
      console.log('🔐 Attempting login...');
      
      const success = await authService.login(credentials);
      
      if (success) {
        authStatus.value = authService.getStatus();
        console.log('✅ Login successful:', authStatus.value);
        
        // Clear form
        credentials.username = '';
        credentials.password = '';
        credentials.apiKey = '';
        
        return true;
      } else {
        authStatus.value = authService.getStatus();
        errorMessage.value = authStatus.value.message || 'Login failed';
        console.log('❌ Login failed:', authStatus.value);
        return false;
      }
    } catch (error) {
      console.error('❌ Login error:', error);
      errorMessage.value = 'Login failed';
      authStatus.value = {
        authenticated: false,
        username: '',
        message: 'Login failed'
      };
      return false;
    } finally {
      isLoading.value = false;
    }
  };

  const logout = async () => {
    try {
      console.log('🚪 Logging out...');
      
      await authService.logout();
      authStatus.value = authService.getStatus();
      
      // Clear form and errors
      credentials.username = '';
      credentials.password = '';
      credentials.apiKey = '';
      errorMessage.value = '';
      
      console.log('✅ Logout successful');
    } catch (error) {
      console.error('❌ Logout error:', error);
    }
  };

  const revalidate = async () => {
    try {
      console.log('🔄 Revalidating authentication...');
      
      await authService.validateStoredCredentials();
      authStatus.value = authService.getStatus();
      
      console.log('✅ Revalidation complete:', authStatus.value);
    } catch (error) {
      console.error('❌ Revalidation failed:', error);
      authStatus.value = {
        authenticated: false,
        username: '',
        message: 'Revalidation failed'
      };
    }
  };

  const handle401Error = () => {
    console.log('🚨 Handling 401 error - clearing credentials');
    
    // Clear credentials and status
    authService.logout();
    authStatus.value = {
      authenticated: false,
      username: '',
      message: 'Session expired. Please log in again.'
    };
    
    // Clear form
    credentials.username = '';
    credentials.password = '';
    credentials.apiKey = '';
    errorMessage.value = 'Session expired. Please log in again.';
    
    // Force reactivity update
    forceUpdate.value++;
  };

  const clearError = () => {
    errorMessage.value = '';
  };

  return {
    // State
    isInitialized,
    authStatus,
    errorMessage,
    isLoading,
    credentials,
    
    // Computed
    isLoggedIn,
    username,
    
    // Actions
    initializeAuth,
    login,
    logout,
    revalidate,
    handle401Error,
    clearError
  };
});