import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'

export const useTimeZoneStore = defineStore('timeZone', () => {
  const STORAGE_KEY = 'tickerq:dashboard:timezone'

  const schedulerTimeZone = ref<string | null>(null)
  const selectedTimeZone = ref<string | null>(null)

  // A small curated list of common IANA time zones.
  // The scheduler's configured time zone will be added dynamically if it's not in this list.
  const commonTimeZones = ref<string[]>([
    'UTC',
    'Europe/London',
    'Europe/Berlin',
    'America/New_York',
    'America/Chicago',
    'America/Denver',
    'America/Los_Angeles',
    'Asia/Tokyo',
    'Asia/Singapore',
    'Australia/Sydney'
  ])

  const availableTimeZones = computed(() => {
    const zones = new Set(commonTimeZones.value)

    if (schedulerTimeZone.value) {
      zones.add(schedulerTimeZone.value)
    }

    // Browser local time zone (if available)
    try {
      const browserTz = Intl.DateTimeFormat().resolvedOptions().timeZone
      if (browserTz) {
        zones.add(browserTz)
      }
    } catch {
      // ignore
    }

    return Array.from(zones)
  })

  const effectiveTimeZone = computed(() => {
    return selectedTimeZone.value || schedulerTimeZone.value || 'UTC'
  })

  // Initialize from localStorage (if available)
  if (typeof window !== 'undefined' && typeof window.localStorage !== 'undefined') {
    const stored = window.localStorage.getItem(STORAGE_KEY)
    if (stored) {
      selectedTimeZone.value = stored
    }
  }

  function setSchedulerTimeZone(id: string | null | undefined) {
    schedulerTimeZone.value = id || null
  }

  function setSelectedTimeZone(id: string | null) {
    selectedTimeZone.value = id
  }

  // Persist user selection to localStorage
  watch(
    selectedTimeZone,
    (val) => {
      if (typeof window === 'undefined' || typeof window.localStorage === 'undefined') {
        return
      }

      if (val) {
        window.localStorage.setItem(STORAGE_KEY, val)
      } else {
        window.localStorage.removeItem(STORAGE_KEY)
      }
    },
    { immediate: false }
  )

  return {
    schedulerTimeZone,
    selectedTimeZone,
    availableTimeZones,
    effectiveTimeZone,
    setSchedulerTimeZone,
    setSelectedTimeZone
  }
})
