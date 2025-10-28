<template>
  <v-alert
    v-if="visible"
    type="error"
    variant="tonal"
    closable
    class="error-alert"
    @click:close="handleClose"
  >
    <v-alert-title class="font-weight-bold">
      {{ title }}
    </v-alert-title>
    <div class="error-message">
      {{ message }}
    </div>
    <div v-if="details" class="error-details mt-2">
      <v-expansion-panels variant="accordion" flat>
        <v-expansion-panel>
          <v-expansion-panel-title class="text-caption">
            Show Details
          </v-expansion-panel-title>
          <v-expansion-panel-text>
            <pre class="error-stack">{{ details }}</pre>
          </v-expansion-panel-text>
        </v-expansion-panel>
      </v-expansion-panels>
    </div>
    <div v-if="showRetry" class="mt-3">
      <v-btn
        size="small"
        variant="outlined"
        @click="handleRetry"
      >
        <v-icon start>mdi-refresh</v-icon>
        Retry
      </v-btn>
    </div>
  </v-alert>
</template>

<script lang="ts" setup>
import { ref, watch } from 'vue'

interface Props {
  error?: Error | string | null
  title?: string
  showRetry?: boolean
  autoHide?: boolean
  autoHideDelay?: number
}

const props = withDefaults(defineProps<Props>(), {
  title: 'An error occurred',
  showRetry: false,
  autoHide: false,
  autoHideDelay: 5000
})

const emit = defineEmits<{
  'retry': []
  'close': []
}>()

const visible = ref(false)
const message = ref('')
const details = ref('')

watch(() => props.error, (newError) => {
  if (newError) {
    if (typeof newError === 'string') {
      message.value = newError
      details.value = ''
    } else if (newError instanceof Error) {
      message.value = newError.message
      details.value = newError.stack || ''
    }
    visible.value = true
    
    if (props.autoHide) {
      setTimeout(() => {
        visible.value = false
      }, props.autoHideDelay)
    }
  } else {
    visible.value = false
  }
}, { immediate: true })

const handleClose = () => {
  visible.value = false
  emit('close')
}

const handleRetry = () => {
  visible.value = false
  emit('retry')
}
</script>

<style scoped>
.error-alert {
  margin: 16px 0;
  animation: slideDown 0.3s ease-out;
}

@keyframes slideDown {
  from {
    opacity: 0;
    transform: translateY(-20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.error-message {
  margin-top: 8px;
  font-size: 0.875rem;
}

.error-details {
  font-size: 0.75rem;
}

.error-stack {
  font-family: monospace;
  font-size: 0.75rem;
  background: rgba(0, 0, 0, 0.1);
  padding: 8px;
  border-radius: 4px;
  overflow-x: auto;
  white-space: pre-wrap;
  word-wrap: break-word;
}

:deep(.v-expansion-panel-title) {
  min-height: 32px !important;
  padding: 4px 8px !important;
  font-size: 0.75rem !important;
}

:deep(.v-expansion-panel-text__wrapper) {
  padding: 8px !important;
}
</style>
