<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useAuth } from '../../composables/useAuth'

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

// Auth composable
const {
  isAuthenticated,
  username,
  isLoading,
  errorMessage,
  login,
  logout,
  clearError
} = useAuth()

// Local state
const isLoginFormVisible = ref(false)
const localUsername = ref('')
const localPassword = ref('')

// Methods
const toggleLoginForm = () => {
  isLoginFormVisible.value = !isLoginFormVisible.value
}

const handleLogin = async () => {
  // Get credentials from the form
  if (!localUsername.value || !localPassword.value) {
    return
  }

  try {
    const result = await login({
      username: localUsername.value,
      password: localPassword.value
    })
    
    if (result.success) {
      isLoginFormVisible.value = false
      emit('login', true)
    } else {
      // Don't emit login event on failure, just show the error
    }
  } catch (error) {
    // Login error
  }
}

const handleLogout = async () => {
  try {
    await logout()
    emit('logout')
  } catch (error) {
    // Logout error
    emit('logout')
  }
}

// No need for local clearError since it's provided by the composable

// Watch for auth changes to auto-hide login form
watch(isAuthenticated, (newValue) => {
  if (newValue) {
    isLoginFormVisible.value = false
  }
})
</script>

<template>
  <div class="auth-header">
    <!-- Login Form -->
    <div v-if="!isAuthenticated && showLoginForm" class="auth-section">
      <div v-if="!isLoginFormVisible" class="login-prompt">
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
        <div class="form-row">
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
          <v-btn
            color="primary"
            variant="elevated"
            size="small"
            @click="handleLogin"
            :loading="isLoading"
            :disabled="!localUsername || !localPassword"
            class="submit-btn"
          >
            <v-icon start>mdi-login</v-icon>
            Login
          </v-btn>
          <v-btn
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

.form-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.username-field,
.password-field {
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
  .password-field {
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