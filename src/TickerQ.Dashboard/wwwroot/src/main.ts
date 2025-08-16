import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import 'vuetify/styles'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import '@mdi/font/css/materialdesignicons.css'
import 'vuetify/styles'
import App from './App.vue'
import router from './router'
import { VDateInput } from 'vuetify/labs/VDateInput'
import { VPullToRefresh } from 'vuetify/labs/VPullToRefresh'
import VueTheMask from 'vue-the-mask'
import { connectionManager } from './hub/connectionManager'

if (localStorage.getItem('auth') == null) {
  localStorage.setItem('auth', 'ZHVtbXk6ZHVtbXk=');
}
const app = createApp(App)
const vuetify = createVuetify({
  components: {
    ...components,
    VDateInput,
    VPullToRefresh
  },
  directives,
  theme: {
    defaultTheme: 'dark'
  }
})

app.use(VueTheMask as any);
app.use(createPinia())
app.use(router)
app.use(vuetify)

app.mount('#app')

// Wait for app to be fully mounted and Pinia to be ready
setTimeout(() => {
  // Check if connection manager is ready
  if (connectionManager.isReady()) {
    // Initialize WebSocket connection after app is mounted
    connectionManager.initializeConnection().catch((error) => {
      console.error('Failed to initialize WebSocket connection:', error)
    })

    // Set up visibility handling for better connection management
    connectionManager.setupVisibilityHandling()

    // Expose connection manager to window for debugging
    if (import.meta.env.DEV) {
      (window as any).connectionManager = connectionManager
      console.log('WebSocket Connection Manager available at window.connectionManager')
      console.log('Use connectionManager.logConnectionStatus() to debug connection issues')
    }
  } else {
    console.warn('Connection manager not ready, skipping WebSocket initialization')
  }
}, 100)
