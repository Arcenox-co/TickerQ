<template>
  <div class="app-container">
    <!-- Debug info -->
    <div v-if="showDebug" class="debug-info">
      <p>Current route: {{ route.name }} ({{ route.path }})</p>
      <p>Show dashboard layout: {{ showDashboardLayout }}</p>
    </div>
    
    <!-- Router handles all authentication logic -->
    <div v-if="showDashboardLayout" class="dashboard-wrapper">
      <DashboardLayout>
        <template #default>
          <RouterView />
        </template>
      </DashboardLayout>
      <GlobalAlerts />
    </div>
    
    <!-- Login and other standalone pages -->
    <div v-else class="standalone-wrapper">
      <RouterView />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, nextTick, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useConnectionStore } from './stores/connectionStore'
import DashboardLayout from './components/layout/DashboardLayout.vue'
import { defineAsyncComponent } from 'vue'

const GlobalAlerts = defineAsyncComponent(() => import('./components/common/GlobalAlerts.vue'))

const route = useRoute()
const connectionStore = useConnectionStore()
const showDebug = ref(false) // Disable debug info

// Determine if we should show the dashboard layout
const showDashboardLayout = computed(() => {
  return route.name !== 'Login'
})

// Initialize connection on mount (auth is handled by router)
onMounted(async () => {
  // Wait for next tick to ensure router has processed
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
          console.error('Failed to initialize connection after retry:', error)
        }
      }, 1000)
    }
  } catch (error) {
    console.error('Failed to initialize connection:', error)
  }
})
</script>

<style scoped>
.app-container {
  min-height: 100vh;
}

.debug-info {
  position: fixed;
  top: 10px;
  right: 10px;
  background: rgba(0, 0, 0, 0.8);
  color: white;
  padding: 10px;
  border-radius: 4px;
  font-size: 12px;
  z-index: 9999;
}

.dashboard-wrapper,
.standalone-wrapper {
  min-height: 100vh;
}

.standalone-wrapper {
  display: flex;
  flex-direction: column;
}
</style>