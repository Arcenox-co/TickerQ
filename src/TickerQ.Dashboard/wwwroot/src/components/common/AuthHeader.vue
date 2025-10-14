<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useAuth } from '../../composables/useAuth'
import { useAuthStore } from '@/stores/authStore'
import { authService } from '@/services/auth'

// Props
interface Props {
  showLoginForm?: boolean
  showUserInfo?: boolean
  showLogout?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  showLoginForm: true,
  showUserInfo: true,
  showLogout: true
})

// Emits
const emit = defineEmits<{
  login: [success: boolean]
  logout: []
}>()

// Auth stores and composables
const authStore = useAuthStore()
const {
  isAuthenticated,
  username,
  isLoading,
  errorMessage,
  login,
  logout,
  clearError
} = useAuth()

// Import alert composable for demonstration
import { useAlert } from '@/composables/useAlert'
const { showSuccess, showError, showWarning, showInfo } = useAlert()

// Local state
const isLoginFormVisible = ref(false)
const localUsername = ref('')
const localPassword = ref('')
const localApiKey = ref('')

// Check authentication modes
const isBasicAuthEnabled = computed(() => {
  return window.TickerQConfig?.auth?.mode === 'basic' || false
})

const isBearerAuthEnabled = computed(() => {
  return window.TickerQConfig?.auth?.mode === 'bearer' || false
})

const isHostAuthEnabled = computed(() => {
  return window.TickerQConfig?.auth?.mode === 'host' || false
})

const requiresAuth = computed(() => {
  return window.TickerQConfig?.auth?.enabled || false
})

const authMode = computed(() => {
  if (isBasicAuthEnabled.value) return 'basic'
  if (isBearerAuthEnabled.value) return 'bearer'
  if (isHostAuthEnabled.value) return 'host'
  return 'none'
})

// Show login form automatically if auth is required and user is not authenticated
const shouldShowLoginForm = computed(() => {
  if (requiresAuth.value && !isAuthenticated.value) {
    return true
  }
  return isLoginFormVisible.value
})

// Methods
const toggleLoginForm = () => {
  if (requiresAuth.value) {
    // If auth is required, we can't toggle - user must authenticate
    return
  }
  isLoginFormVisible.value = !isLoginFormVisible.value
}

const handleLogin = async () => {
  try {
    // Set credentials in auth store
    if (authMode.value === 'basic') {
      authStore.credentials.username = localUsername.value
      authStore.credentials.password = localPassword.value
    } else if (authMode.value === 'bearer') {
      authStore.credentials.apiKey = localApiKey.value
    }
    
    // Attempt login
    const success = await authStore.login()
    
    if (success) {
      isLoginFormVisible.value = false
      showSuccess('Login successful!')
      emit('login', true)
      
      // Clear local form fields
      localUsername.value = ''
      localPassword.value = ''
      localApiKey.value = ''
    } else {
      // Error message is handled by auth store
      showError(authStore.errorMessage || 'Login failed')
    }
  } catch (error) {
    console.error('Login error:', error)
    showError('Login failed. Please try again.')
  }
}

const handleLogout = async () => {
  try {
    // Use auth store logout for all modes
    authStore.logout()
    
    showInfo('Logged out successfully')
    emit('logout')
  } catch (error) {
    console.error('Logout error:', error)
    showError('Logout failed')
    emit('logout')
  }
}

// Check auth status on mount
onMounted(() => {
  // If auth is required and user is not authenticated, show login form
  if (requiresAuth.value && !isAuthenticated.value) {
    isLoginFormVisible.value = true
  }
})

// Watch for auth changes to auto-hide login form
watch(isAuthenticated, (newValue) => {
  if (newValue) {
    isLoginFormVisible.value = false
  } else if (requiresAuth.value) {
    // If auth is required, show login form when user becomes unauthenticated
    isLoginFormVisible.value = true
  }
})

// No need for local clearError since it's provided by the composable
</script>

<template>
  <div class="auth-header">
    <!-- Login Form -->
    <div v-if="!isAuthenticated && showLoginForm" class="auth-section">
      <div v-if="!shouldShowLoginForm" class="login-prompt">
        <v-btn
          color="primary"
          variant="outlined"
          size="small"
          @click="toggleLoginForm"
          class="login-btn"
        >
          <v-icon start>mdi-login</v-icon>
          Login
        </v-btn>
      </div>
      
      <div v-else class="login-form">
        <div v-if="requiresAuth" class="auth-notice">
          <v-alert
            type="info"
            variant="tonal"
            density="compact"
            class="info-alert"
          >
            <span v-if="authMode === 'basic'">Basic Authentication Required</span>
            <span v-else-if="authMode === 'bearer'">API Key Authentication Required</span>
            <span v-else-if="authMode === 'host'">Host Authentication Required</span>
            <span v-else>Authentication Required</span>
          </v-alert>
        </div>
        
        <div class="form-row">
          <!-- Basic Auth Fields -->
          <template v-if="authMode === 'basic'">
            <v-text-field
              v-model="localUsername"
              label="Username"
              variant="outlined"
              density="compact"
              size="small"
              class="username-field"
              :disabled="isLoading"
              @keyup.enter="handleLogin"
            />
            <v-text-field
              v-model="localPassword"
              label="Password"
              type="password"
              variant="outlined"
              density="compact"
              size="small"
              class="password-field"
              :disabled="isLoading"
              @keyup.enter="handleLogin"
            />
          </template>
          
          <!-- Bearer Token Field -->
          <template v-else-if="authMode === 'bearer'">
            <v-text-field
              v-model="localApiKey"
              label="API Key"
              type="password"
              variant="outlined"
              density="compact"
              size="small"
              class="api-key-field"
              :disabled="isLoading"
              @keyup.enter="handleLogin"
              placeholder="Enter your API key"
            />
          </template>
          <v-btn
            color="primary"
            variant="elevated"
            size="small"
            @click="handleLogin"
            :loading="isLoading"
            :disabled="(authMode === 'basic' && (!localUsername || !localPassword)) || (authMode === 'bearer' && !localApiKey)"
            class="submit-btn"
          >
            <v-icon start>mdi-login</v-icon>
            Login
          </v-btn>
          <v-btn
            v-if="!requiresAuth"
            variant="text"
            size="small"
            @click="toggleLoginForm"
            :disabled="isLoading"
            class="cancel-btn"
          >
            Cancel
          </v-btn>
        </div>
        
        <!-- Error Message -->
        <div v-if="errorMessage" class="error-message">
          <v-alert
            type="error"
            variant="tonal"
            density="compact"
            class="error-alert"
            @click="clearError"
          >
            {{ errorMessage }}
          </v-alert>
        </div>
      </div>
    </div>

    <!-- User Info and Logout -->
    <div v-if="isAuthenticated && showUserInfo" class="auth-section">
      <div class="user-info">
        <v-icon class="user-icon">mdi-account-circle</v-icon>
        <span class="username">{{ username }}</span>
        <v-divider vertical class="divider" />
        <v-btn
          v-if="showLogout"
          color="error"
          variant="text"
          size="small"
          @click="handleLogout"
          class="logout-btn"
        >
          <v-icon start>mdi-logout</v-icon>
          Logout
        </v-btn>
      </div>
    </div>
  </div>
</template>

<style scoped>
.auth-header {
  display: flex;
  align-items: center;
  gap: 16px;
}

.auth-section {
  display: flex;
  align-items: center;
}

/* Login Form Styles */
.login-prompt {
  display: flex;
  align-items: center;
}

.login-btn {
  font-weight: 500;
  text-transform: none;
  letter-spacing: 0.5px;
  border-radius: 8px;
  transition: all 0.3s ease;
}

.login-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 400px;
}

.auth-notice {
  margin-bottom: 8px;
}

.info-alert {
  font-size: 0.875rem;
}

.form-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.username-field,
.password-field,
.api-key-field {
  flex: 1;
  min-width: 120px;
}

.submit-btn {
  font-weight: 600;
  text-transform: none;
  letter-spacing: 0.5px;
  border-radius: 8px;
  transition: all 0.3s ease;
}

.submit-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.cancel-btn {
  font-weight: 500;
  text-transform: none;
  color: #757575;
}

.error-message {
  margin-top: 4px;
}

.error-alert {
  cursor: pointer;
  transition: all 0.3s ease;
}

.error-alert:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

/* User Info Styles */
.user-info {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.user-icon {
  color: #4caf50;
  font-size: 1.25rem;
}

.username {
  font-weight: 600;
  color: #e0e0e0;
  font-size: 0.875rem;
}

.divider {
  border-color: rgba(255, 255, 255, 0.2);
  height: 20px;
}

.logout-btn {
  font-weight: 500;
  text-transform: none;
  letter-spacing: 0.5px;
  border-radius: 6px;
  transition: all 0.3s ease;
}

.logout-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

/* Responsive Design */
@media (max-width: 768px) {
  .login-form {
    min-width: 300px;
  }
  
  .form-row {
    flex-direction: column;
    align-items: stretch;
    gap: 8px;
  }
  
  .username-field,
  .password-field,
  .api-key-field {
    min-width: auto;
  }
}

@media (max-width: 480px) {
  .login-form {
    min-width: 250px;
  }
  
  .auth-header {
    gap: 12px;
  }
}
</style> 