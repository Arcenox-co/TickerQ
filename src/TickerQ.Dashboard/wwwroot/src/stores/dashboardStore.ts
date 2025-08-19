import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useDashboardStore = defineStore('dashboard', () => {
  // State for next occurrence
  const nextOccurrence = ref<string | null>(null)
  const isNextOccurrenceForced = ref(false)

  // Computed to determine if we should show "Not Scheduled"
  const displayNextOccurrence = computed(() => {
    if (isNextOccurrenceForced.value) {
      return 'Not Scheduled'
    }
    return nextOccurrence.value
  })

  // Actions
  const setNextOccurrence = (value: string | null) => {
    nextOccurrence.value = value
    isNextOccurrenceForced.value = false
  }

  const forceNotScheduled = () => {
    isNextOccurrenceForced.value = true
  }

  const resetForceState = () => {
    isNextOccurrenceForced.value = false
  }

  return {
    nextOccurrence,
    isNextOccurrenceForced,
    displayNextOccurrence,
    setNextOccurrence,
    forceNotScheduled,
    resetForceState
  }
}) 