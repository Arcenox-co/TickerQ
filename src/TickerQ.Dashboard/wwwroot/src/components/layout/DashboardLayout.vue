<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import { sleep } from '../../utilities/sleep'
import { useDialog } from '../../composables/useDialog'
import { ConfirmDialogProps } from '../common/ConfirmDialog.vue'
import { useDashboardStore } from '../../stores/dashboardStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useTimeZoneStore } from '@/stores/timeZoneStore'
import AuthHeader from '../common/AuthHeader.vue'

const navigationLinks = [
  { icon: 'mdi-view-dashboard', text: 'Dashboard', path: '/' },
  { icon: 'mdi-alarm', text: 'Time Tickers', path: '/time-tickers' },
  { icon: 'mdi-calendar-sync', text: 'Cron Tickers', path: '/cron-tickers' },
]

// Reactive state
const tickerHostStatus = ref(false)
const currentMachine = ref('Loading...')
const lastHostExceptionMessage = ref('')
const isServicesReady = ref(false)

// Animation state for restart button
const restartIsAnimating = ref(false)

// Dashboard store
const dashboardStore = useDashboardStore()

// Connection store
const connectionStore = useConnectionStore()

// Time zone store
const timeZoneStore = useTimeZoneStore()

// Time zone menu state
const isTimeZoneMenuOpen = ref(false)

// Router
const router = useRouter()

// Lazy-loaded stores and services
let tickerService: any = null

// Lazy-loaded service functions
let getOptions: any = null
let getTickerHostStatus: any = null
let startTicker: any = null
let restartTicker: any = null
let stopTicker: any = null



  // Confirm dialog (legacy lazy instance, not used for stop)
  const confirmDialog = useDialog<ConfirmDialogProps>().withComponent(
    () => import('../common/ConfirmDialog.vue'),
  )


// Initialize stores and services lazily
const initializeServices = async () => {
  try {
    // Initialize the WebSocket connection using the connection store
    try {
      if (!connectionStore.isReady) {
        // Connection store not ready, retrying in 2 seconds...
        setTimeout(async () => {
          try {
            await connectionStore.initializeConnectionWithRetry()
            connectionStore.setupVisibilityHandling()
          } catch (error) {
            // Connection initialization failed after retry
          }
        }, 2000)
        return
      }

      await connectionStore.initializeConnection()
    } catch (error) {
      // Failed to initialize WebSocket connection
    }

    const tickerServiceModule = await import('../../http/services/tickerService')
    tickerService = tickerServiceModule.tickerService

    // Initialize ticker services
    getOptions = tickerService.getOptions()
    getTickerHostStatus = tickerService.getTickerHostStatus()
    startTicker = tickerService.startTicker()
    restartTicker = tickerService.restartTicker()
    stopTicker = tickerService.stopTicker()

    // Load initial data
    await loadInitialData()

    // Mark services as ready
    isServicesReady.value = true
  } catch (error) {
    // Failed to initialize services
    // Even if there's an error, mark as ready to prevent infinite loading
    isServicesReady.value = true
  }
}

// Load initial data
const loadInitialData = async () => {
  if (!getOptions || !getTickerHostStatus) return

  try {
    // Wait for options to be loaded
    await getOptions.requestAsync()

    // Check if response is available before accessing it
    if (getOptions.response?.value) {
      const options = getOptions.response.value
      currentMachine.value = options.currentMachine || 'Unknown'
      lastHostExceptionMessage.value = options.lastHostExceptionMessage || ''

      // Initialize scheduler time zone from server options
      if (options.schedulerTimeZone) {
        timeZoneStore.setSchedulerTimeZone(options.schedulerTimeZone)
      }
    } else {
      currentMachine.value = 'Loading...'
      lastHostExceptionMessage.value = ''
    }

    // Wait for host status to be loaded
    await getTickerHostStatus.requestAsync()

    // Check if response is available before accessing it
    if (getTickerHostStatus.response?.value) {
      tickerHostStatus.value = getTickerHostStatus.response.value.isRunning
    }
  } catch (error) {
    // Failed to load initial data
    currentMachine.value = 'Error'
    tickerHostStatus.value = false
  }
}

// Initialize connection on mount
onMounted(async () => {
  // Wait for next tick to ensure Pinia stores are fully initialized
  await nextTick()

  try {
    // Check if connection store is ready
    if (!connectionStore.isReady) {
      // Connection store not ready, retrying in 2 seconds...
      setTimeout(async () => {
        try {
          await connectionStore.initializeConnectionWithRetry()
          connectionStore.setupVisibilityHandling()
        } catch (error) {
          // Connection initialization failed after retry
        }
      }, 2000)
      return
    }

    await connectionStore.initializeConnection()

    // After connection is established, initialize services and load data
    await initializeServices()
  } catch (error) {
    // Error initializing connection
  }
})

// Restart handler ensures at least 1s animation and until request completes
async function handleRestart() {
  if (!restartTicker) return
  restartIsAnimating.value = true
  try {
    await Promise.all([
      restartTicker.requestAsync(),
      sleep(1000)
    ])
  } finally {
    restartIsAnimating.value = false
  }
}

// Get WebSocket status text with backend health information
function getWebSocketStatusText() {
  const isConnecting = connectionStore.isConnecting
  const isConnected = connectionStore.isWebSocketConnected

  if (isConnecting) {
    return 'WebSocket Connecting...'
  }

  if (isConnected) {
    return 'WebSocket Connected'
  }

  return 'WebSocket Disconnected'
}

// Handle reconnection with health check
async function handleReconnect() {
  try {
    await connectionStore.forceReconnection()

    // Wait for connection to stabilize
    await new Promise(resolve => setTimeout(resolve, 3000))

    // Perform health check and refresh
    await connectionStore.performManualHealthCheck()
    await connectionStore.refreshConnectionStatus()

    // Force UI update
    connectionStore.forceUIUpdate()

  } catch (error) {
    // Reconnection failed
  }
}

// Navigate to dashboard
function navigateToDashboard() {
  // Use router to navigate to dashboard
  router.push('/')
}

// Auth event handlers
function handleAuthLogin(success: boolean) {
  if (success) {
    // Reload initial data after successful login
    loadInitialData()
  }
}

function handleAuthLogout() {
  // Handle logout - could redirect to login page or refresh
  if (typeof window !== 'undefined') {
    window.location.reload()
  }
}

// Connection status display
const getConnectionStatus = computed(() => {
  if (connectionStore.isConnected) {
    return 'Connected'
  } else if (connectionStore.isConnecting) {
    return 'Connecting...'
  } else {
    return 'Disconnected'
  }
})

// Connection management methods
const handleForceReconnection = async () => {
  if (!connectionStore) return

  try {
    await connectionStore.forceReconnection()
  } catch (error) {
    // Force reconnection failed
  }
}

const handleManualHealthCheck = async () => {
  try {
    await connectionStore.performManualHealthCheck()
    await connectionStore.refreshConnectionStatus()
  } catch (error) {
    // Manual health check failed
  }
}

const handleForceUIUpdate = () => {
  connectionStore.forceUIUpdate()
}


</script>

<template>
  <v-app id="inspire">
    <!-- Dashboard Header -->
    <v-app-bar class="main-header">
      <div class="header-container">
        <div class="header-content">
            <div class="header-left">
            <div class="logo-container clickable" @click="navigateToDashboard">
              <img
                src="@/assets/arcenox-logo.svg"
                alt="Arcenox"
                class="logo-image"
              />
            </div>
            <div class="app-title-container clickable" @click="navigateToDashboard">
              <h1 class="app-title">
                <strong>TickerQ</strong>
              </h1>
            </div>

            <!-- Time Zone Menu -->
            <div class="timezone-menu">
              <v-menu
                v-model="isTimeZoneMenuOpen"
                location="bottom"
                :close-on-content-click="false"
              >
                <template #activator="{ props }">
                  <v-btn
                    v-bind="props"
                    size="small"
                    variant="text"
                    density="comfortable"
                    class="timezone-button"
                    prepend-icon="mdi-earth"
                  >
                    <span class="d-none d-sm-inline">
                      {{ timeZoneStore.effectiveTimeZone }}
                    </span>
                  </v-btn>
                </template>

                <v-card elevation="4" class="timezone-card">
                  <v-card-title class="text-subtitle-2">
                    Display Time Zone
                  </v-card-title>
                  <v-card-text class="pt-2">
                    <v-select
                      density="compact"
                      variant="outlined"
                      hide-details="auto"
                      :items="[
                        { label: `Scheduler (${timeZoneStore.schedulerTimeZone || 'UTC'})`, value: null },
                        ...timeZoneStore.availableTimeZones.map(tz => ({ label: tz, value: tz }))
                      ]"
                      item-title="label"
                      item-value="value"
                      v-model="timeZoneStore.selectedTimeZone"
                    />
                    <v-btn
                      class="mt-3"
                      size="small"
                      variant="text"
                      prepend-icon="mdi-restore"
                      @click="timeZoneStore.setSelectedTimeZone(null)"
                    >
                      Reset to server timezone
                    </v-btn>
                  </v-card-text>
                </v-card>
              </v-menu>
            </div>
          </div>

          <div class="header-center">
            <div class="header-divider"></div>
          </div>

          <div class="header-right">
            <div class="navigation-links">
              <v-btn
                v-for="link in navigationLinks"
                :key="link.path"
                :text="link.text"
                :to="link.path"
                variant="text"
                class="nav-link"
                :prepend-icon="link.icon"
              ></v-btn>
            </div>

            <!-- Auth Header Component -->
            <div class="auth-container">
              <AuthHeader
                :show-login-form="true"
                :show-user-info="true"
                :show-logout="true"
                @login="handleAuthLogin"
                @logout="handleAuthLogout"
              />
            </div>
          </div>
        </div>
      </div>
    </v-app-bar>

    <!-- Main Content Area -->
    <v-main>
      <!-- Global Status Header -->
      <div class="status-header">
        <div class="header-content">
          <div class="status-section">
            <div class="status-indicator">
              <div
                class="status-pulse"
                :class="{ 'pulse-active': tickerHostStatus, 'pulse-inactive': !tickerHostStatus }"
              ></div>
              <div class="status-info">
                <div class="system-details">
                  <span class="machine-name">{{ currentMachine }}</span>
                  <span class="status-divider">•</span>
                  <span
                    class="status-text"
                    :class="{ 'status-online': tickerHostStatus, 'status-offline': !tickerHostStatus }"
                  >
                    {{ tickerHostStatus ? 'Online' : 'Offline' }}
                  </span>
                </div>
              </div>
            </div>

            <!-- WebSocket Connection Status -->
            <div v-if="isServicesReady" class="websocket-status">
              <div
                class="websocket-indicator"
                :class="{
                  'websocket-connected': connectionStore.isWebSocketConnected,
                  'websocket-connecting': connectionStore.isConnecting,
                  'websocket-disconnected': !connectionStore.isWebSocketConnected && !connectionStore.isConnecting
                }"
              ></div>
              <span class="websocket-text">
                {{ getWebSocketStatusText() }}
              </span>
              <v-btn
                v-if="!connectionStore.isWebSocketConnected"
                size="x-small"
                variant="text"
                class="reconnect-btn"
                @click="handleReconnect()"
              >
                Reconnect
              </v-btn>

            </div>

            <!-- Loading State for WebSocket -->
            <div v-else class="websocket-status">
              <div class="websocket-indicator websocket-connecting"></div>
              <span class="websocket-text">Initializing...</span>
            </div>
          </div>

          <div class="action-section">
            <div class="action-buttons">
                <v-btn
                v-if="!tickerHostStatus && isServicesReady"
                  color="success"
                variant="elevated"
                size="small"
                prepend-icon="mdi-play-circle"
                  @click="startTicker?.requestAsync().then(() => {
                    // Reset forced state when starting system
                    dashboardStore.resetForceState();
                    startTicker.loader.value = true;
                    sleep(1000).then(() => {
                      loadInitialData();
                      startTicker.loader.value = false;
                    })
                  })"
                  :loading="startTicker?.loader?.value"
                class="action-btn start-btn"
              >
                Start System
              </v-btn>

              <template v-if="tickerHostStatus && isServicesReady">
                <v-btn
                  color="warning"
                  variant="elevated"
                  size="small"
                  @click="handleRestart"
                  :loading="restartTicker?.loader?.value"
                  class="action-btn restart-btn"
                  :disabled="restartIsAnimating"
                  :class="{ 'restart-animating': restartIsAnimating }"
                >
                  <span class="btn-content">
                    <v-icon
                      class="restart-icon"
                      :class="{ 'rotating': restartIsAnimating }"
                    >
                      mdi-restart
                    </v-icon>
                    <span class="btn-text">Restart</span>
                  </span>

                  <!-- Ripple Effect -->
                  <div class="ripple-container">
                    <div class="ripple" v-if="restartIsAnimating"></div>
                  </div>
                </v-btn>

                <v-btn
                  color="error"
                  variant="elevated"
                  size="small"
                  prepend-icon="mdi-stop-circle"
                  @click="confirmDialog?.open({...new ConfirmDialogProps(), confirmText: 'Stop' })"
                  :loading="stopTicker?.loader?.value"
                  class="action-btn stop-btn"
                >
                  Stop System
                </v-btn>
            </template>

            <!-- Loading State for Actions -->
            <div v-if="!isServicesReady" class="action-loading">
              <v-progress-circular indeterminate size="20" color="primary"></v-progress-circular>
              <span class="loading-text">Loading...</span>
            </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Main Content Slot -->
      <slot />
    </v-main>

    <!-- Footer -->
    <v-footer class="main-footer">
      <div class="footer-content">
        <v-divider class="footer-divider" thickness="2" width="50"></v-divider>
        <div class="footer-text">
          2025 — <strong>Arcenox</strong>
        </div>
      </div>
    </v-footer>

  </v-app>



  <!-- Confirm Dialog - Portal to body to avoid layout issues -->
  <Teleport to="body">
    <component
      v-if="confirmDialog && confirmDialog.isOpen"
      :is="confirmDialog.Component"
      :is-open="confirmDialog.isOpen"
      @close="confirmDialog.close()"
      :dialog-props="confirmDialog.propData"
      @confirm="
        stopTicker.requestAsync().then(() => {
          confirmDialog.close();
          // Force next occurrence to show 'Not Scheduled'
          dashboardStore.forceNotScheduled();
          stopTicker.loader.value = true;
          sleep(1000).then(() => {
            loadInitialData();
            stopTicker.loader.value = false;
          })
        })
      "
    />
  </Teleport>
</template>

<style scoped>
/* Main Header */
.main-header {
  background: rgba(33, 33, 33, 0.95) !important;
  backdrop-filter: blur(20px) !important;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1) !important;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.3) !important;
  transition: all 0.3s ease !important;
  padding: 0 !important;
}

.main-header:hover {
  background: rgba(33, 33, 33, 0.98) !important;
  box-shadow: 0 6px 25px rgba(0, 0, 0, 0.4) !important;
}

.header-container {
  width: 100%;
  max-width: 1400px;
  margin: 0 auto;
  padding: 0 24px;
}

.header-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  height: 64px;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 20px;
  flex-shrink: 0;
}

.logo-container {
  display: flex;
  align-items: center;
  padding: 8px 0;
  cursor: pointer;
  transition: all 0.3s ease;
}

.logo-container:hover {
  transform: translateY(-1px);
}

.logo-image {
  height: 40px;
  width: auto;
  transition: transform 0.3s ease;
}

.logo-container:hover .logo-image {
  transform: scale(1.05);
}

.app-title-container {
  display: flex;
  align-items: center;
  cursor: pointer;
  transition: all 0.3s ease;
  padding: 8px 12px;
  border-radius: 8px;
}

.app-title-container:hover {
  background: rgba(255, 255, 255, 0.1);
  transform: translateY(-1px);
}

.clickable {
  user-select: none;
}

.app-title {
  color: #e0e0e0 !important;
  font-size: 1.5rem !important;
  font-weight: 700 !important;
  letter-spacing: -0.5px !important;
  margin: 0 !important;
}

.header-center {
  flex: 1;
  display: flex;
  justify-content: center;
  align-items: center;
}

.header-divider {
  width: 1px;
  height: 32px;
  background: rgba(255, 255, 255, 0.1);
  border-radius: 1px;
}

.header-right {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-shrink: 0;
  gap: 16px;
}

.auth-container {
  display: flex;
  align-items: center;
  margin-left: 16px;
  padding-left: 16px;
  border-left: 1px solid rgba(255, 255, 255, 0.1);
}

.navigation-links {
  display: flex;
  align-items: center;
  gap: 8px;
}

.nav-link {
  color: #bdbdbd !important;
  font-weight: 500 !important;
  text-transform: none !important;
  letter-spacing: 0.5px !important;
  border-radius: 8px !important;
  transition: all 0.3s ease !important;
  padding: 8px 16px !important;
}

.nav-link:hover {
  color: #e0e0e0 !important;
  background: rgba(255, 255, 255, 0.1) !important;
  transform: translateY(-1px) !important;
}

.nav-link.v-btn--active {
  color: var(--v-theme-primary) !important;
  background: rgba(var(--v-theme-primary), 0.1) !important;
}

/* Dashboard Header (Legacy) */
.dashboard-header {
  position: sticky;
  top: 0;
  z-index: 100;
  background: rgba(33, 33, 33, 0.95);
  backdrop-filter: blur(20px);
  border-radius: 0;
  padding: 12px 20px;
  margin: 0;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.3);
  border: none;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  transition: all 0.3s ease;
}

.dashboard-header:hover {
  background: rgba(33, 33, 33, 0.98);
  box-shadow: 0 6px 25px rgba(0, 0, 0, 0.4);
}

/* Status Header */
.status-header {
  position: sticky;
  top: 64px; /* Account for the app bar height */
  z-index: 99;
  background: rgba(33, 33, 33, 0.95);
  backdrop-filter: blur(20px);
  border-radius: 0;
  padding: 0px 20px;
  margin: 0;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.3);
  border: none;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  transition: all 0.3s ease;
}

.status-header:hover {
  background: rgba(33, 33, 33, 0.98);
  box-shadow: 0 6px 25px rgba(0, 0, 0, 0.4);
}

.header-content {
  max-width: 1400px;
  margin: 0 auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 16px;
}

/* Status Section */
.status-section {
  display: flex;
  align-items: center;
  gap: 20px;
}

.status-indicator {
  display: flex;
  align-items: center;
  gap: 12px;
}

.status-pulse {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  flex-shrink: 0;
}

.pulse-active {
  background: #4caf50;
  animation: pulse-success 2s infinite;
  box-shadow: 0 0 0 0 rgba(76, 175, 80, 0.7);
}

.pulse-inactive {
  background: #f44336;
  animation: pulse-error 2s infinite;
  box-shadow: 0 0 0 0 rgba(244, 67, 54, 0.7);
}

@keyframes pulse-success {
  0% { box-shadow: 0 0 0 0 rgba(76, 175, 80, 0.7); }
  70% { box-shadow: 0 0 0 10px rgba(76, 175, 80, 0); }
  100% { box-shadow: 0 0 0 0 rgba(76, 175, 80, 0); }
}

@keyframes pulse-error {
  0% { box-shadow: 0 0 0 0 rgba(244, 67, 54, 0.7); }
  70% { box-shadow: 0 0 0 10px rgba(244, 67, 54, 0); }
  100% { box-shadow: 0 0 0 0 rgba(244, 67, 54, 0); }
}

.system-details {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.875rem;
}

.machine-name {
  font-weight: 600;
  color: #e0e0e0;
}

.status-divider {
  color: #757575;
}

.status-online {
  color: #4caf50;
  font-weight: 600;
}

.status-offline {
  color: #f44336;
  font-weight: 600;
}

/* WebSocket Status */
.websocket-status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.websocket-indicator {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}

.websocket-connected {
  background: #4caf50;
  animation: pulse-success 2s infinite;
  box-shadow: 0 0 0 0 rgba(76, 175, 80, 0.7);
}

.websocket-connecting {
  background: #ff9800;
  animation: pulse-warning 1.5s infinite;
  box-shadow: 0 0 0 0 rgba(255, 152, 0, 0.7);
}

.websocket-disconnected {
  background: #f44336;
  animation: pulse-error 2s infinite;
  box-shadow: 0 0 0 0 rgba(244, 67, 54, 0.7);
}

.websocket-warning {
  background: #ff9800;
  animation: pulse-warning 1.5s infinite;
  box-shadow: 0 0 0 0 rgba(255, 152, 0, 0.7);
}

@keyframes pulse-warning {
  0% { box-shadow: 0 0 0 0 rgba(255, 152, 0, 0.7); }
  70% { box-shadow: 0 0 0 6px rgba(255, 152, 0, 0); }
  100% { box-shadow: 0 0 0 0 rgba(255, 152, 0, 0); }
}

.websocket-text {
  font-size: 0.75rem;
  color: #bdbdbd;
  font-weight: 500;
}

.reconnect-btn {
  margin-left: 8px;
  font-size: 0.7rem;
  text-transform: none;
  min-width: auto;
  padding: 2px 8px;
}

/* Action Section */
.action-section {
  display: flex;
  align-items: center;
}

.action-buttons {
  display: flex;
  gap: 12px;
}

.action-btn {
  font-weight: 600;
  text-transform: none;
  letter-spacing: 0.5px;
  border-radius: 12px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  transition: all 0.3s ease;
}

.action-btn:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 25px rgba(0, 0, 0, 0.2);
}

/* Loading States */
.action-loading {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.loading-text {
  font-size: 0.75rem;
  color: #bdbdbd;
  font-weight: 500;
}

/* Restart Button Animations */
.restart-btn {
  position: relative;
  overflow: hidden;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  transform-origin: center;
}

.restart-btn:hover {
  transform: translateY(-2px) scale(1.02);
  box-shadow: 0 8px 25px rgba(255, 152, 0, 0.3);
}

.restart-btn:active {
  transform: translateY(0) scale(0.98);
  transition: transform 0.1s ease;
}

.restart-btn.restart-animating {
  animation: restart-pulse 1.5s ease-in-out infinite;
  background: linear-gradient(45deg, #ff9800, #ff5722, #ff9800);
  background-size: 200% 200%;
  animation: restart-pulse 1.5s ease-in-out infinite, gradient-shift 2s ease-in-out infinite;
}

.btn-content {
  display: flex;
  align-items: center;
  gap: 6px;
  position: relative;
  z-index: 1;
}

.restart-icon {
  transition: transform 0.3s ease;
}

.restart-icon.rotating {
  animation: rotate-360 1s linear infinite;
}

.btn-text {
  font-weight: 600;
  transition: all 0.3s ease;
}

.restart-btn.restart-animating .btn-text {
  animation: text-glow 1.5s ease-in-out infinite;
}

/* Restart Button Keyframe Animations */
@keyframes restart-pulse {
  0%, 100% {
    transform: scale(1);
    box-shadow: 0 4px 12px rgba(255, 152, 0, 0.3);
  }
  50% {
    transform: scale(1.05);
    box-shadow: 0 8px 25px rgba(255, 152, 0, 0.5);
  }
}

@keyframes rotate-360 {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

@keyframes gradient-shift {
  0%, 100% {
    background-position: 0% 50%;
  }
  50% {
    background-position: 100% 50%;
  }
}

@keyframes text-glow {
  0%, 100% {
    text-shadow: 0 0 5px rgba(255, 152, 0, 0.5);
  }
  50% {
    text-shadow: 0 0 20px rgba(255, 152, 0, 0.8), 0 0 30px rgba(255, 152, 0, 0.6);
  }
}

/* Ripple Effect */
.ripple-container {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  border-radius: inherit;
  overflow: hidden;
  pointer-events: none;
}

.ripple {
  position: absolute;
  top: 50%;
  left: 50%;
  width: 0;
  height: 0;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.3);
  transform: translate(-50%, -50%);
  animation: ripple-expand 1.5s ease-out infinite;
}

@keyframes ripple-expand {
  0% {
    width: 0;
    height: 0;
    opacity: 1;
  }
  100% {
    width: 300px;
    height: 300px;
    opacity: 0;
  }
}

/* Responsive Design */
@media (max-width: 1400px) {
  .header-container {
    padding: 0 32px;
  }
}

@media (max-width: 1024px) {
  .header-container {
    padding: 0 24px;
  }
}

@media (max-width: 768px) {
  .header-container {
    padding: 0 16px;
  }

  .header-content {
    height: auto;
    min-height: 64px;
    padding: 12px 0;
  }

  .header-left {
    flex-direction: column;
    gap: 12px;
    align-items: center;
  }

  .header-center {
    display: none;
  }

  .header-right {
    justify-content: center;
    flex-direction: column;
    gap: 12px;
  }

  .navigation-links {
    flex-wrap: wrap;
    justify-content: center;
  }

  .auth-container {
    margin-left: 0;
    padding-left: 0;
    border-left: none;
    border-top: 1px solid rgba(255, 255, 255, 0.1);
    padding-top: 12px;
    width: 100%;
    justify-content: center;
  }
}

@media (max-width: 480px) {
  .header-container {
    padding: 0 12px;
  }

  .app-title {
    font-size: 1.25rem !important;
  }

  .logo-image {
    height: 32px;
  }
}

/* Main Footer */
.main-footer {
  background: rgba(33, 33, 33, 0.95) !important;
  backdrop-filter: blur(20px) !important;
  border-top: 1px solid rgba(255, 255, 255, 0.1) !important;
  box-shadow: 0 -2px 12px rgba(0, 0, 0, 0.3) !important;
  padding: 16px 0 !important;
}

.footer-content {
  max-width: 1400px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 0 24px;
}

.footer-divider {
  border-color: rgba(255, 255, 255, 0.2) !important;
  opacity: 0.6;
}

.footer-text {
  color: #bdbdbd !important;
  font-size: 0.875rem !important;
  font-weight: 500 !important;
  text-align: center;
}

.footer-text strong {
  color: #e0e0e0 !important;
  font-weight: 600 !important;
}

/* Responsive Footer */
@media (max-width: 768px) {
  .footer-content {
    padding: 0 16px;
  }

  .main-footer {
    padding: 20px 0 !important;
  }
}
</style>
