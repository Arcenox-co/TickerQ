<script setup lang="ts">
import { useAlertStore } from '@/stores/alertStore'
import { computed} from 'vue'

const alertStore = useAlertStore()

// Get alerts that are visible
const visibleAlerts = computed(() => {
  return alertStore.alerts.filter(alert => alert.visible)
})

// Get icon for alert type
const getAlertIcon = (type: string) => {
  switch (type) {
    case 'error':
      return 'mdi-alert-circle'
    case 'warning':
      return 'mdi-alert'
    case 'success':
      return 'mdi-check-circle'
    case 'info':
    default:
      return 'mdi-information'
  }
}
// Handle alert close
const handleClose = (alertId: string) => {
  alertStore.dismissAlert(alertId)
}
</script>

<template>
  <div class="global-alerts">
    <!-- Display alerts as custom components -->
    <div>
      <div
        v-for="alert in visibleAlerts"
        :key="alert.id"
        :class="['alert-item', `alert-${alert.type}`]"
        @click.stop
      >
        <div class="alert-content">
          <!-- Alert Icon -->
          <div class="alert-icon">
            <v-icon :icon="getAlertIcon(alert.type)" size="18" />
          </div>
          
          <!-- Alert Text -->
          <div class="alert-text">
            <div v-if="alert.title" class="alert-title">
              {{ alert.title }}
            </div>
            <div class="alert-message">
              {{ alert.message }}
            </div>
          </div>

          <!-- Close Button -->
          <button
            v-if="alert.closable"
            @click="handleClose(alert.id)"
            class="alert-close-btn"
            aria-label="Close alert"
          >
            <v-icon icon="mdi-close" size="16" />
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.global-alerts {
  position: fixed;
  top: 16px;
  right: 16px;
  z-index: 9999;
  pointer-events: none;
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-width: 350px;
}

.alert-item {
  pointer-events: auto;
  min-width: 280px;
  background: rgba(66, 66, 66, 0.95);
  backdrop-filter: blur(10px);
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
  overflow: hidden;
  border-left: 4px solid;
  transition: all 0.2s ease;
}

.alert-item:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
}

.alert-content {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 12px 14px;
}

.alert-icon {
  flex-shrink: 0;
  margin-top: 1px;
}

.alert-text {
  flex: 1;
  min-width: 0;
}

.alert-title {
  font-weight: 600;
  font-size: 0.75rem;
  line-height: 1.2;
  margin-bottom: 3px;
  color: rgba(255, 255, 255, 0.95);
}

.alert-message {
  font-size: 0.75rem;
  line-height: 1.3;
  color: rgba(255, 255, 255, 0.85);
  word-wrap: break-word;
  overflow-wrap: break-word;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.alert-close-btn {
  background: none;
  border: none;
  color: rgba(255, 255, 255, 0.7);
  cursor: pointer;
  padding: 2px;
  border-radius: 4px;
  transition: all 0.2s ease;
  display: flex;
  align-items: center;
  justify-content: center;
}

.alert-close-btn:hover {
  background: rgba(255, 255, 255, 0.1);
  color: rgba(255, 255, 255, 0.9);
}

.alert-actions {
  padding: 0 14px 12px;
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}

.alert-action-btn {
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.2);
  color: rgba(255, 255, 255, 0.9);
  border-radius: 6px;
  padding: 6px 12px;
  font-size: 0.7rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
}

.alert-action-btn:hover {
  background: rgba(255, 255, 255, 0.2);
  border-color: rgba(255, 255, 255, 0.3);
}

/* Alert type specific styling */
.alert-error {
  border-left-color: #f44336;
  background: linear-gradient(135deg, rgba(244, 67, 54, 0.1) 0%, rgba(211, 47, 47, 0.1) 100%);
}

.alert-error .alert-icon {
  color: #f44336;
}

.alert-success {
  border-left-color: #4caf50;
  background: linear-gradient(135deg, rgba(76, 175, 80, 0.1) 0%, rgba(56, 142, 60, 0.1) 100%);
}

.alert-success .alert-icon {
  color: #4caf50;
}

.alert-warning {
  border-left-color: #ff9800;
  background: linear-gradient(135deg, rgba(255, 152, 0, 0.1) 0%, rgba(245, 124, 0, 0.1) 100%);
}

.alert-warning .alert-icon {
  color: #ff9800;
}

.alert-info {
  border-left-color: #2196f3;
  background: linear-gradient(135deg, rgba(33, 150, 243, 0.1) 0%, rgba(25, 118, 210, 0.1) 100%);
}

.alert-info .alert-icon {
  color: #2196f3;
}

/* Action button color variants */
.alert-action-primary {
  background: rgba(33, 150, 243, 0.2);
  border-color: rgba(33, 150, 243, 0.4);
  color: #2196f3;
}

.alert-action-primary:hover {
  background: rgba(33, 150, 243, 0.3);
  border-color: rgba(33, 150, 243, 0.6);
}

.alert-action-error {
  background: rgba(244, 67, 54, 0.2);
  border-color: rgba(244, 67, 54, 0.4);
  color: #f44336;
}

.alert-action-error:hover {
  background: rgba(244, 67, 54, 0.3);
  border-color: rgba(244, 67, 54, 0.6);
}

.alert-action-warning {
  background: rgba(255, 152, 0, 0.2);
  border-color: rgba(255, 152, 0, 0.4);
  color: #ff9800;
}

.alert-action-warning:hover {
  background: rgba(255, 152, 0, 0.3);
  border-color: rgba(255, 152, 0, 0.6);
}

/* Transition animations */
.alert-enter-active {
  transition: all 0.3s ease-out;
}

.alert-leave-active {
  transition: all 0.3s ease-in;
}

.alert-enter-from {
  transform: translateX(100%);
  opacity: 0;
}

.alert-leave-to {
  transform: translateX(100%);
  opacity: 0;
}

.alert-move {
  transition: transform 0.3s ease;
}

/* Responsive adjustments */
@media (max-width: 600px) {
  .global-alerts {
    left: 8px;
    right: 8px;
    top: 8px;
    max-width: none;
  }
  
  .alert-item {
    min-width: auto;
  }
  
  .alert-message {
    font-size: 0.7rem;
  }
  
  .alert-title {
    font-size: 0.7rem;
  }
  
  .alert-content {
    padding: 10px 12px;
  }
  
  .alert-actions {
    padding: 0 12px 10px;
  }
}
</style>
