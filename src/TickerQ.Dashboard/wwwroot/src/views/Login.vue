<template>
  <div class="login-container">
    <div class="login-card">
      <div class="login-header">
        <div class="logo-section">
          <img src="@/assets/arcenox-logo.svg" alt="TickerQ" class="logo" />
          <h1 class="app-title">TickerQ Dashboard</h1>
        </div>
        <p class="login-subtitle">Please authenticate to access the dashboard</p>
      </div>

      <!-- Error Alert -->
      <v-alert
        v-if="authStore.errorMessage"
        type="error"
        variant="tonal"
        class="mb-4"
        closable
        @click:close="authStore.clearError()"
      >
        {{ authStore.errorMessage }}
      </v-alert>

      <!-- Basic Auth Form -->
      <v-form
        v-if="authMode === 'basic'"
        ref="form"
        @submit.prevent="handleLogin"
        class="login-form"
      >
        <v-text-field
          v-model="authStore.credentials.username"
          label="Username"
          placeholder="Enter your username"
          prepend-inner-icon="mdi-account-circle"
          :rules="rules.username"
          variant="outlined"
          class="mb-4"
          :disabled="authStore.isLoading"
          @input="clearError"
          autofocus
        />

        <v-text-field
          v-model="authStore.credentials.password"
          :type="showPassword ? 'text' : 'password'"
          label="Password"
          placeholder="Enter your password"
          prepend-inner-icon="mdi-lock"
          :append-inner-icon="showPassword ? 'mdi-eye' : 'mdi-eye-off'"
          :rules="rules.password"
          variant="outlined"
          class="mb-6"
          :disabled="authStore.isLoading"
          @click:append-inner="showPassword = !showPassword"
          @input="clearError"
          @keyup.enter="handleLogin"
        />

        <v-btn
          type="submit"
          color="primary"
          size="x-large"
          block
          :loading="authStore.isLoading"
          :disabled="!isFormValid"
          class="login-btn"
          elevation="0"
        >
          <v-icon start>mdi-login-variant</v-icon>
          {{ authStore.isLoading ? 'Signing In...' : 'Sign In' }}
        </v-btn>

        <div class="auth-help-text">
          <v-icon size="small" class="mr-1">mdi-information-outline</v-icon>
          Enter your dashboard credentials to access TickerQ
        </div>
      </v-form>

      <!-- API Key Form -->
      <v-form
        v-else-if="authMode === 'apikey'"
        ref="form"
        @submit.prevent="handleLogin"
        class="login-form"
      >
        <v-text-field
          v-model="authStore.credentials.apiKey"
          label="API Key"
          placeholder="Enter your API key or access token"
          prepend-inner-icon="mdi-key-variant"
          :rules="rules.apiKey"
          variant="outlined"
          class="mb-6"
          :disabled="authStore.isLoading"
          @input="clearError"
          @keyup.enter="handleLogin"
          autofocus
        />

        <v-btn
          type="submit"
          color="primary"
          size="x-large"
          block
          :loading="authStore.isLoading"
          :disabled="!isFormValid"
          class="login-btn"
          elevation="0"
        >
          <v-icon start>mdi-shield-key</v-icon>
          {{ authStore.isLoading ? 'Authenticating...' : 'Authenticate' }}
        </v-btn>

        <div class="auth-help-text">
          <v-icon size="small" class="mr-1">mdi-information-outline</v-icon>
          Enter your API key or access token to access the dashboard
        </div>
      </v-form>

      <!-- Host Auth Form -->
      <div v-else-if="authMode === 'host'" class="host-auth-section">
        <div class="host-auth-message">
          <v-icon size="48" color="info" class="mb-3">mdi-shield-account</v-icon>
          <h3>Host Authentication</h3>
          <p>This dashboard uses your application's existing authentication system.</p>
        </div>

        <v-form
          ref="form"
          @submit.prevent="handleLogin"
          class="login-form mt-4"
        >
          <v-text-field
            v-model="authStore.credentials.hostAccessKey"
            label="Access Key"
            placeholder="Bearer xyz123 or ApiKey abc456"
            prepend-inner-icon="mdi-key-variant"
            :rules="rules.hostAccessKey"
            variant="outlined"
            class="mb-6 login-input"
            :disabled="authStore.isLoading"
            @input="authStore.clearError()"
            @keyup.enter="handleLogin"
            autofocus
          />

          <v-btn
            type="submit"
            color="primary"
            size="large"
            block
            :loading="authStore.isLoading"
            :disabled="!isFormValid || authStore.isLoading"
            class="login-btn"
          >
            <v-icon start>mdi-shield-key</v-icon>
            {{ authStore.isLoading ? 'Setting Access Key...' : 'Set Access Key' }}
          </v-btn>

          <div class="auth-help-text">
            <v-icon size="small" class="mr-1">mdi-information-outline</v-icon>
            Enter your full access key (including Bearer/ApiKey prefix) for API calls
          </div>
        </v-form>
      </div>

      <!-- No Auth Message -->
      <div v-else class="no-auth-message">
        <v-icon size="48" color="success" class="mb-3">mdi-check-circle</v-icon>
        <h3>Public Dashboard</h3>
        <p>No authentication required.</p>
        <v-btn color="primary" @click="$router.push('/')">
          Continue to Dashboard
        </v-btn>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { getAuthMode } from '@/utilities/pathResolver'

const router = useRouter()
const authStore = useAuthStore()

// Form state
const form = ref()
const showPassword = ref(false)

// Auth mode detection
const authMode = computed(() => getAuthMode())

// Validation rules
const rules = {
  username: [
    (v: string) => !!v || 'Username is required',
    (v: string) => v.length >= 3 || 'Username must be at least 3 characters'
  ],
  password: [
    (v: string) => !!v || 'Password is required',
    (v: string) => v.length >= 1 || 'Password is required'
  ],
  apiKey: [
    (v: string) => !!v || 'API key is required',
    (v: string) => v.length >= 10 || 'API key must be at least 10 characters'
  ],
  hostAccessKey: [
    (v: string) => !!v || 'Access key is required',
    (v: string) => v.length >= 10 || 'Access key must be at least 10 characters'
  ]
}

// Form validation
const isFormValid = computed(() => {
  if (authMode.value === 'basic') {
    return (authStore.credentials.username?.length || 0) >= 3 &&
           (authStore.credentials.password?.length || 0) >= 1
  } else if (authMode.value === 'apikey') {
    return (authStore.credentials.apiKey?.length || 0) >= 10
  } else if (authMode.value === 'host') {
    return (authStore.credentials.hostAccessKey?.length || 0) >= 10
  }
  return false
})

// Handle login
const handleLogin = async () => {
  if (!form.value?.validate()) return

  const success = await authStore.login()
  if (success) {
    // Redirect to intended route or dashboard
    const redirect = router.currentRoute.value.query.redirect as string
    router.push(redirect || '/')
  }
}

// Clear error when user starts typing
const clearError = () => {
  if (authStore.errorMessage) {
    authStore.clearError()
  }
}

// Check if already authenticated
onMounted(() => {
  if (authStore.isLoggedIn) {
    router.push('/')
  }
})
</script>

<style scoped>
.login-container {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #1a1a2e 0%, #16213e 50%, #0f3460 100%);
  padding: 20px;
  position: relative;
  overflow: hidden;
}

.login-container::before {
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background:
    radial-gradient(circle at 20% 80%, rgba(120, 119, 198, 0.3) 0%, transparent 50%),
    radial-gradient(circle at 80% 20%, rgba(255, 119, 198, 0.15) 0%, transparent 50%),
    radial-gradient(circle at 40% 40%, rgba(120, 219, 255, 0.1) 0%, transparent 50%);
  pointer-events: none;
}

.login-card {
  background: rgba(30, 30, 46, 0.95);
  backdrop-filter: blur(20px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 20px;
  padding: 48px;
  box-shadow:
    0 32px 64px rgba(0, 0, 0, 0.4),
    inset 0 1px 0 rgba(255, 255, 255, 0.1);
  width: 100%;
  max-width: 420px;
  position: relative;
  z-index: 1;
}

.login-header {
  text-align: center;
  margin-bottom: 40px;
}

.logo-section {
  display: flex;
  flex-direction: column;
  align-items: center;
  margin-bottom: 20px;
}

.logo {
  width: 72px;
  height: 72px;
  margin-bottom: 20px;
  filter: drop-shadow(0 4px 8px rgba(0, 0, 0, 0.3));
}

.app-title {
  font-size: 32px;
  font-weight: 700;
  background: linear-gradient(135deg, #64b5f6 0%, #42a5f5 50%, #2196f3 100%);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  margin: 0;
  text-shadow: 0 2px 4px rgba(0, 0, 0, 0.3);
}

.login-subtitle {
  color: rgba(255, 255, 255, 0.7);
  margin: 0;
  font-size: 16px;
  font-weight: 400;
}

.login-form {
  margin-bottom: 32px;
}

.login-btn {
  height: 52px;
  font-weight: 600;
  text-transform: none;
  letter-spacing: 0.5px;
  background: linear-gradient(135deg, #2196f3 0%, #1976d2 100%) !important;
  box-shadow: 0 8px 24px rgba(33, 150, 243, 0.3);
  transition: all 0.3s ease;
}

.login-btn:hover {
  transform: translateY(-2px);
  box-shadow: 0 12px 32px rgba(33, 150, 243, 0.4);
}

.auth-help-text {
  text-align: center;
  margin-top: 16px;
  padding: 12px;
  background: rgba(33, 150, 243, 0.1);
  border: 1px solid rgba(33, 150, 243, 0.2);
  border-radius: 8px;
  color: rgba(255, 255, 255, 0.8);
  font-size: 14px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.host-auth-message,
.no-auth-message {
  text-align: center;
  padding: 40px 0;
  color: rgba(255, 255, 255, 0.9);
}

.host-auth-message h3,
.no-auth-message h3 {
  margin: 20px 0 12px 0;
  color: rgba(255, 255, 255, 0.95);
  font-weight: 600;
}

.host-auth-message p,
.no-auth-message p {
  color: rgba(255, 255, 255, 0.7);
  margin: 8px 0;
  line-height: 1.5;
}

/* Custom input styling for dark theme */
:deep(.v-field) {
  background: rgba(255, 255, 255, 0.05) !important;
  border: 1px solid rgba(255, 255, 255, 0.1) !important;
  border-radius: 12px !important;
}

:deep(.v-field:hover) {
  border-color: rgba(33, 150, 243, 0.5) !important;
}

:deep(.v-field--focused) {
  border-color: #2196f3 !important;
  box-shadow: 0 0 0 2px rgba(33, 150, 243, 0.2) !important;
}

:deep(.v-field__input) {
  color: rgba(255, 255, 255, 0.9) !important;
}

:deep(.v-field__input::placeholder) {
  color: rgba(255, 255, 255, 0.5) !important;
}

:deep(.v-label) {
  color: rgba(255, 255, 255, 0.7) !important;
}

:deep(.v-field--focused .v-label) {
  color: #2196f3 !important;
}

:deep(.v-icon) {
  color: rgba(255, 255, 255, 0.6) !important;
}

:deep(.v-field--focused .v-icon) {
  color: #2196f3 !important;
}

/* Alert styling */
:deep(.v-alert) {
  background: rgba(244, 67, 54, 0.1) !important;
  border: 1px solid rgba(244, 67, 54, 0.3) !important;
  color: #ff5722 !important;
}

/* Responsive */
@media (max-width: 480px) {
  .login-container {
    padding: 16px;
  }

  .login-card {
    padding: 32px 24px;
  }

  .app-title {
    font-size: 28px;
  }

  .logo {
    width: 64px;
    height: 64px;
  }
}

/* Loading animation */
@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.7; }
}

.login-btn.v-btn--loading {
  animation: pulse 1.5s ease-in-out infinite;
}
</style>
