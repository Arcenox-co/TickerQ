<script setup lang="ts">
import { useAuthStore } from './stores/authStore'
import DashboardLayout from './components/layout/DashboardLayout.vue'
import { ref, computed, onMounted, nextTick } from 'vue'
import { useConnectionStore } from './stores/connectionStore'

const authStore = useAuthStore()
const connectionStore = useConnectionStore()

// Form validation
const form = ref()
const isLoading = ref(false)
const showPassword = ref(false)

// Validation rules
const rules = {
  username: [
    (v: string) => !!v || 'Username is required',
    (v: string) => v.length >= 3 || 'Username must be at least 3 characters'
  ],
  password: [
    (v: string) => !!v || 'Password is required',
    (v: string) => v.length >= 1 || 'Password is required'
  ]
}

// Initialize auth and connection on mount
onMounted(async () => {
  await authStore.initializeAuth()
  
  // Wait for next tick to ensure Pinia stores are fully initialized
  await nextTick()
  
  try {
    // Check if connection store is ready
    if (connectionStore.isReady) {
      // Initialize WebSocket connection with retry logic
      await connectionStore.initializeConnectionWithRetry()
      
      // Set up visibility handling for better connection management
      connectionStore.setupVisibilityHandling()
    } else {
      // Retry after a delay
      setTimeout(async () => {
        try {
          await connectionStore.initializeConnectionWithRetry()
          connectionStore.setupVisibilityHandling()
        } catch (error) {
          // Connection initialization failed after retry
        }
      }, 2000)
    }
  } catch (error) {
    // Error setting up connection store
  }
})

// Handle form submission
const handleSubmit = async () => {
  const { valid } = await form.value.validate()
  
  if (valid) {
    isLoading.value = true
    try {
      await authStore.login()
    } catch (error) {
      // Login failed
    } finally {
      isLoading.value = false
    }
  }
}

// Clear error when user starts typing
const clearError = () => {
  if (authStore.errorMessage) {
    authStore.clearError()
  }
}
</script>

<template>
  <!-- Loading state while initializing auth -->
  <div v-if="!authStore.isInitialized" class="loading-container">
    <div class="loading-content">
      <div class="loading-spinner">
        <v-progress-circular
          indeterminate
          size="64"
          color="primary"
          width="6"
        ></v-progress-circular>
      </div>
      <h2 class="loading-title">Initializing TickerQ...</h2>
      <p class="loading-subtitle">Please wait while we set up your dashboard</p>
    </div>
  </div>
  
  <!-- Dashboard when authenticated -->
  <span v-else-if="authStore.isLoggedIn">
    <DashboardLayout>
      <template #default>
        <RouterView :key="String(authStore.isLoggedIn)" />
      </template>
    </DashboardLayout>
  </span>
  
  <!-- Login form when not authenticated -->
  <span v-else>
    <div class="auth-container">
      <div class="auth-card">
        <!-- Logo and Title -->
        <div class="auth-header">
          <div class="logo-container">
            <img
              src="https://arcenox.com/assets/imgs/main/arcenox-logo.svg"
              alt="Arcenox"
              class="auth-logo"
            />
          </div>
          <h1 class="auth-title">TickerQ</h1>
          <p class="auth-subtitle">Sign in to access your dashboard</p>
        </div>

        <!-- Login Form -->
        <v-form
          ref="form"
          @submit.prevent="handleSubmit"
          class="auth-form"
        >
          <v-text-field
            v-model="authStore.credentials.username"
            label="Username"
            variant="outlined"
            density="comfortable"
            :rules="rules.username"
            prepend-inner-icon="mdi-account"
            @input="clearError"
            :disabled="isLoading"
            class="auth-input"
          ></v-text-field>

          <v-text-field
            v-model="authStore.credentials.password"
            label="Password"
            variant="outlined"
            density="comfortable"
            :type="showPassword ? 'text' : 'password'"
            :rules="rules.password"
            prepend-inner-icon="mdi-lock"
            append-inner-icon="showPassword ? 'mdi-eye-off' : 'mdi-eye'"
            @click:append-inner="showPassword = !showPassword"
            @input="clearError"
            :disabled="isLoading"
            class="auth-input"
          ></v-text-field>

          <!-- Error Message -->
          <div v-if="authStore.errorMessage" class="error-message">
            <v-icon color="error" size="small" class="mr-2">mdi-alert-circle</v-icon>
            {{ authStore.errorMessage }}
          </div>

          <!-- Submit Button -->
          <v-btn
            type="submit"
            color="primary"
            size="large"
            block
            :loading="isLoading"
            :disabled="isLoading"
            class="auth-submit-btn"
          >
            <v-icon v-if="!isLoading" class="mr-2">mdi-login</v-icon>
            {{ isLoading ? 'Signing In...' : 'Sign In' }}
          </v-btn>
        </v-form>

        <!-- Footer -->
        <div class="auth-footer">
          <p class="footer-text">
            Secure access to your TickerQ dashboard
          </p>
        </div>
      </div>
    </div>
  </span>
</template>

<style scoped>
/* Loading State */
.loading-container {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #212121 0%, #2d2d2d 100%);
  padding: 20px;
  font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
}

.loading-content {
  text-align: center;
  color: white;
}

.loading-spinner {
  margin-bottom: 24px;
}

.loading-title {
  font-size: 2rem;
  font-weight: 700;
  margin-bottom: 12px;
  color: #e0e0e0;
}

.loading-subtitle {
  font-size: 1.1rem;
  opacity: 0.9;
  color: #bdbdbd;
}

/* Auth Container */
.auth-container {
  min-height: 100vh;
  background: linear-gradient(135deg, #212121 0%, #2d2d2d 100%);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
  font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
}

.auth-card {
  background: rgba(66, 66, 66, 0.9);
  backdrop-filter: blur(20px);
  border-radius: 16px;
  padding: 40px;
  width: 100%;
  max-width: 400px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
  border: 1px solid rgba(255, 255, 255, 0.1);
  transition: all 0.3s ease;
}

.auth-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 12px 40px rgba(0, 0, 0, 0.5);
}

.auth-header {
  text-align: center;
  margin-bottom: 32px;
}

.logo-container {
  display: flex;
  justify-content: center;
  margin-bottom: 16px;
}

.auth-logo {
  height: 48px;
  width: auto;
  transition: transform 0.3s ease;
}

.auth-logo:hover {
  transform: scale(1.05);
}

.auth-title {
  color: #e0e0e0;
  font-size: 2rem;
  font-weight: 700;
  margin: 0 0 8px 0;
  letter-spacing: -0.5px;
}

.auth-subtitle {
  color: #bdbdbd;
  font-size: 0.875rem;
  margin: 0;
  font-weight: 500;
}

.auth-form {
  margin-bottom: 24px;
}

.auth-input {
  margin-bottom: 20px;
}

.auth-input :deep(.v-field) {
  background: rgba(255, 255, 255, 0.05) !important;
  border: 1px solid rgba(255, 255, 255, 0.1) !important;
  border-radius: 8px !important;
}

.auth-input :deep(.v-field__input) {
  color: #e0e0e0 !important;
}

.auth-input :deep(.v-label) {
  color: #bdbdbd !important;
}

.auth-input :deep(.v-field--focused) {
  border-color: var(--v-theme-primary) !important;
}

.auth-submit-btn {
  font-weight: 600 !important;
  text-transform: none !important;
  letter-spacing: 0.5px !important;
  border-radius: 8px !important;
  height: 48px !important;
  font-size: 1rem !important;
  transition: all 0.3s ease !important;
}

.auth-submit-btn:hover {
  transform: translateY(-1px) !important;
  box-shadow: 0 6px 20px rgba(var(--v-theme-primary), 0.3) !important;
}

.error-message {
  display: flex;
  align-items: center;
  justify-content: center;
  color: #ff5252;
  font-size: 0.875rem;
  font-weight: 500;
  margin-bottom: 20px;
  padding: 12px;
  background: rgba(255, 82, 82, 0.1);
  border: 1px solid rgba(255, 82, 82, 0.2);
  border-radius: 8px;
}

.auth-footer {
  text-align: center;
  padding-top: 24px;
  border-top: 1px solid rgba(255, 255, 255, 0.1);
}

.footer-text {
  color: #9e9e9e;
  font-size: 0.75rem;
  margin: 0;
  font-weight: 500;
}

/* Responsive Design */
@media (max-width: 480px) {
  .auth-card {
    padding: 32px 24px;
    margin: 16px;
  }
  
  .auth-title {
    font-size: 1.75rem;
  }
  
  .auth-container {
    padding: 16px;
  }
}
</style>
