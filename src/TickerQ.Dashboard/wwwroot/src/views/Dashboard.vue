<script lang="ts" setup>
import { tickerService } from '@/http/services/tickerService'
import { formatDate } from '@/utilities/dateTimeParser'
import { computed, onMounted, onUnmounted, ref, watch, type Ref } from 'vue'
import TickerNotificationHub, { methodName } from '@/hub/tickerNotificationHub'
import { useFunctionNameStore } from '@/stores/functionNames'
import { useDashboardStore } from '@/stores/dashboardStore'
import { useTimeZoneStore } from '@/stores/timeZoneStore'
import { Status } from '@/http/services/types/base/baseHttpResponse.types'

const getNextPlannedTicker = tickerService.getNextPlannedTicker()
const getOptions = tickerService.getOptions()
const getMachineJobs = tickerService.getMachineJobs()
const getJobStatusesPastWeek = tickerService.getJobStatusesPastWeek()
const getJobStatusesOverall = tickerService.getJobStatusesOverall()
const functionNamesStore = useFunctionNameStore()
const dashboardStore = useDashboardStore()
const timeZoneStore = useTimeZoneStore()

const activeThreads = ref(0)

onMounted(async () => {
  await getNextPlannedTicker.requestAsync()
  // Initialize dashboard store with the fetched data
  if (getNextPlannedTicker.response.value?.nextOccurrence) {
    dashboardStore.setNextOccurrence(getNextPlannedTicker.response.value.nextOccurrence)
  }
  await getOptions.requestAsync()

  if (getOptions.response.value?.schedulerTimeZone) {
    timeZoneStore.setSchedulerTimeZone(getOptions.response.value.schedulerTimeZone)
  }
  await getJobStatusesOverall.requestAsync().then((res) => {
    const total = res.reduce((sum, item) => sum + item.item2, 0)

    res.forEach((item) => {
      const status = statuses.value.find((x) => x.name === Status[item.item1 as any])

      if (status) {
        status.count = item.item2
        // Calculate percentage of total
        const raw = ((item.item2 / total) * 100).toFixed(1)
        status.percentage = raw.endsWith('.0') ? raw.slice(0, -2) : raw
      }
    })
  })

  await getJobStatusesPastWeek.requestAsync()

  await getMachineJobs.requestAsync().then((res) => {
    machineItems.value = []
    res.forEach((item) => {
      machineItems.value.push({
        machine: item.item1,
        locked: `${item.item2} Jobs`,
      })
    })
    currentMachinesPage.value = 1
  })
  await functionNamesStore.loadData().then((res) => {
    functionItems.value = [] // Clear existing items
    res.value?.forEach((item) => {
      const request = item.functionRequestNamespace == '' ? 'N/A' : item.functionRequestNamespace
      functionItems.value.push({
        function: item.functionName,
        request: request,
        priority: tickerTaskPriority[item.priority],
      })
    })
    // Reset pagination to first page when data changes
    currentFunctionsPage.value = 1
  })

  TickerNotificationHub.onReceiveThreadsActive((threads: number) => {
    activeThreads.value = threads
  })

  TickerNotificationHub.onReceiveNextOccurrence((nextOccurrence: string) => {
    getNextPlannedTicker.updateProperty('nextOccurrence', nextOccurrence)
    dashboardStore.setNextOccurrence(nextOccurrence)
  })

  TickerNotificationHub.onReceiveHostExceptionMessage((message: string) => {
    getOptions.updateProperty('lastHostExceptionMessage', message)
  })
})

// Function to calculate successful count (only Done and DueDone jobs)
function getSuccessfulCount(): number {
  const doneStatus = statuses.value.find((s) => s.name === 'Done')
  const dueDoneStatus = statuses.value.find((s) => s.name === 'DueDone')

  const doneCount = doneStatus ? doneStatus.count : 0
  const dueDoneCount = dueDoneStatus ? dueDoneStatus.count : 0

  return doneCount + dueDoneCount
}

// Function to calculate total final jobs count (Done, DueDone, Failed, Cancelled)
function getTotalFinalJobsCount(): number {
  const doneStatus = statuses.value.find((s) => s.name === 'Done')
  const dueDoneStatus = statuses.value.find((s) => s.name === 'DueDone')
  const failedStatus = statuses.value.find((s) => s.name === 'Failed')
  const cancelledStatus = statuses.value.find((s) => s.name === 'Cancelled')

  const doneCount = doneStatus ? doneStatus.count : 0
  const dueDoneCount = dueDoneStatus ? dueDoneStatus.count : 0
  const failedCount = failedStatus ? failedStatus.count : 0
  const cancelledCount = cancelledStatus ? cancelledStatus.count : 0

  return doneCount + dueDoneCount + failedCount + cancelledCount
}

onUnmounted(() => {
  TickerNotificationHub.stopReceiver(methodName.onReceiveThreadsActive)
  TickerNotificationHub.stopReceiver(methodName.onReceiveNextOccurrence)
  TickerNotificationHub.stopReceiver(methodName.onReceiveHostStatus)
  TickerNotificationHub.stopReceiver(methodName.onReceiveHostExceptionMessage)
})

const statuses: Ref<Array<{ name: string; count: number; percentage: string }>> = ref([
  { name: 'Idle', count: 0, percentage: '0' },
  { name: 'Queued', count: 0, percentage: '0' },
  { name: 'InProgress', count: 0, percentage: '0' },
  { name: 'Done', count: 0, percentage: '0' },
  { name: 'DueDone', count: 0, percentage: '0' },
  { name: 'Failed', count: 0, percentage: '0' },
  { name: 'Cancelled', count: 0, percentage: '0' },
  { name: 'Skipped', count: 0, percentage: '0' },
])

const hasError = computed(
  () =>
    getOptions.response?.value?.lastHostExceptionMessage &&
    getOptions.response.value.lastHostExceptionMessage !== '',
)

const warningMessage = ref(false)

watch(
  () => activeThreads.value === getOptions.response.value?.maxConcurrency,
  (isMax) => {
    if (isMax) {
      warningMessage.value = true
    } else {
      setTimeout(() => {
        warningMessage.value = false
      }, 3000)
    }
  },
  { immediate: true },
)

const seriesColors: { [key: string]: string } = {
  Idle: '#A9A9A9', // Dark Gray
  Queued: '#00CED1', // Dark Turquoise
  InProgress: '#6495ED', // Royal Blue
  Done: '#32CD32', // Lime Green
  DueDone: '#008000', // Green
  Failed: '#FF0000', // Red
  Cancelled: '#FFD700', // Gold/Yellow
  Skipped: '#BA68C8', // Medium Orchid (Purple)
}

const machineItems: Ref<Array<{ machine: string; locked: string }>> = ref([])

// Machines pagination
const machinesPerPage = ref(10)
const currentMachinesPage = ref(1)

const totalMachinePages = computed(() =>
  Math.max(1, Math.ceil(machineItems.value.length / machinesPerPage.value)),
)

const pagedMachineItems = computed(() => {
  const start = (currentMachinesPage.value - 1) * machinesPerPage.value
  const end = start + machinesPerPage.value
  return machineItems.value.slice(start, end)
})

const currentMachineName = computed(() => getOptions.response.value?.currentMachine ?? '')

const isCurrentMachine = (name: string) =>
  !!name && !!currentMachineName.value && name === currentMachineName.value

const tickerTaskPriority: { [key: number]: string } = {
  0: 'LongRunning',
  1: 'High',
  2: 'Normal',
  3: 'Low',
}

const functionItems: Ref<
  Array<{
    function: string
    request: string
    priority: string
  }>
> = ref([])

// Pagination for Registered Functions
const functionsPerPage = ref(10)
const currentFunctionsPage = ref(1)

// Available page sizes
const availablePageSizes = [5, 10, 20, 50]

// Table headers for Vuetify data table
const functionHeaders = [
  { title: 'Function Name', key: 'function', sortable: true },
  { title: 'Request Namespace', key: 'request', sortable: true },
  { title: 'Priority', key: 'priority', sortable: true },
]

// Computed properties for pagination
const totalFunctionsPages = computed(() =>
  Math.ceil(functionItems.value.length / functionsPerPage.value),
)

// Handle Vuetify table options update
const handleTableOptionsUpdate = (options: any) => {
  if (options.itemsPerPage !== undefined) {
    functionsPerPage.value = options.itemsPerPage
  }
  if (options.page !== undefined) {
    currentFunctionsPage.value = options.page
  }
}

// Function to get visible page numbers for pagination
const getVisiblePageNumbers = () => {
  const total = totalFunctionsPages.value
  const current = currentFunctionsPage.value
  const delta = 2 // Number of pages to show on each side of current page

  let start = Math.max(1, current - delta)
  let end = Math.min(total, current + delta)

  // Adjust start and end to always show delta*2 + 1 pages when possible
  if (end - start < delta * 2) {
    if (start === 1) {
      end = Math.min(total, start + delta * 2)
    } else {
      start = Math.max(1, end - delta * 2)
    }
  }

  const pages = []
  for (let i = start; i <= end; i++) {
    pages.push(i)
  }

  return pages
}
</script>
<template>
  <div class="dashboard-container">
    <!-- Content Section -->
    <div class="dashboard-content">
      <!-- Key Metrics Grid -->
      <div class="metrics-grid">
        <!-- Next Execution Metric -->
        <div class="metric-card primary-metric">
          <div class="metric-icon">
            <v-icon size="28" color="primary">mdi-clock-time-four-outline</v-icon>
          </div>
          <div class="metric-content">
            <h3 class="metric-label">Next Execution</h3>
            <p
              class="metric-value primary-text"
              v-if="getNextPlannedTicker.response.value !== undefined"
            >
              {{
                dashboardStore.displayNextOccurrence === 'Not Scheduled' ||
                dashboardStore.displayNextOccurrence == undefined
                  ? 'Not Scheduled'
                  : formatDate(dashboardStore.displayNextOccurrence, true, timeZoneStore.effectiveTimeZone)
              }}
            </p>
            <div v-else class="skeleton-text metric-value-skeleton"></div>
          </div>
        </div>

        <!-- Max Concurrency Metric -->
        <div class="metric-card info-metric">
          <div class="metric-icon">
            <v-icon size="28" color="info">mdi-cog-sync-outline</v-icon>
          </div>
          <div class="metric-content">
            <h3 class="metric-label">Max Concurrency</h3>
            <p class="metric-value info-text" v-if="getOptions.response.value !== undefined">
              {{ getOptions.response.value?.maxConcurrency || 0 }}
            </p>
            <div v-else class="skeleton-text metric-value-skeleton"></div>
          </div>
        </div>

        <!-- Active Threads Metric -->
        <div class="metric-card" :class="warningMessage ? 'warning-metric' : 'success-metric'">
          <div class="metric-icon">
            <v-icon size="28" :color="warningMessage ? 'warning' : 'success'">
              {{ warningMessage ? 'mdi-alert-circle-outline' : 'mdi-pulse' }}
            </v-icon>
          </div>
          <div class="metric-content">
            <h3 class="metric-label">Active Threads</h3>
            <p class="metric-value" :class="warningMessage ? 'warning-text' : 'success-text'">
              {{ activeThreads }}
            </p>
          </div>
        </div>

        <!-- Success Rate Metric -->
        <div class="metric-card success-metric">
          <div class="metric-icon">
            <v-icon size="28" color="success">mdi-chart-line-variant</v-icon>
          </div>
          <div class="metric-content">
            <h3 class="metric-label">Success Rate</h3>
            <p
              class="metric-value success-text"
              v-if="getJobStatusesPastWeek.response.value !== undefined"
            >
              {{
                getTotalFinalJobsCount() > 0
                  ? Math.round((getSuccessfulCount() / getTotalFinalJobsCount()) * 100)
                  : 0
              }}%
            </p>
            <div v-else class="skeleton-text metric-value-skeleton"></div>
          </div>
        </div>
      </div>

      <!-- Statistics Section -->
      <div class="stats-section">
        <h2 class="section-title">
          <v-icon class="section-icon" color="primary">mdi-chart-bar</v-icon>
          Job Statistics (Past 7 Days)
        </h2>

        <div class="stats-grid">
          <div class="stat-card success-stat">
            <div class="stat-header">
              <v-icon color="success" size="20">mdi-check-circle</v-icon>
              <span class="stat-title">Successful</span>
            </div>
            <div
              class="stat-number success-number"
              v-if="getJobStatusesPastWeek.response.value !== undefined"
            >
              {{ getSuccessfulCount() }}
            </div>
            <div v-else class="skeleton-text stat-number-skeleton"></div>
            <div class="stat-trend">
              <v-icon size="14" color="success">mdi-trending-up</v-icon>
              <span class="trend-text">Jobs completed</span>
            </div>
          </div>

          <div class="stat-card error-stat">
            <div class="stat-header">
              <v-icon color="error" size="20">mdi-close-circle</v-icon>
              <span class="stat-title">Failed</span>
            </div>
            <div
              class="stat-number error-number"
              v-if="getJobStatusesPastWeek.response.value !== undefined"
            >
              {{ getJobStatusesPastWeek.response.value[1].item2 }}
            </div>
            <div v-else class="skeleton-text stat-number-skeleton"></div>
            <div class="stat-trend">
              <v-icon size="14" color="error">mdi-trending-down</v-icon>
              <span class="trend-text">Jobs failed</span>
            </div>
          </div>

          <div class="stat-card info-stat">
            <div class="stat-header">
              <v-icon color="info" size="20">mdi-information</v-icon>
              <span class="stat-title">Final Jobs</span>
            </div>
            <div
              class="stat-number info-number"
              v-if="getJobStatusesPastWeek.response.value !== undefined"
            >
              {{ getTotalFinalJobsCount() }}
            </div>
            <div v-else class="skeleton-text stat-number-skeleton"></div>
            <div class="stat-trend">
              <v-icon size="14" color="info">mdi-chart-line</v-icon>
              <span class="trend-text">Final job states</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Main Content Grid -->
      <div class="content-grid">
        <!-- Status Overview -->
        <div class="content-card status-overview">
          <div class="card-header">
            <h2 class="card-title">
              <v-icon class="title-icon" color="primary">mdi-chart-donut-variant</v-icon>
              Status Distribution
            </h2>
            <p class="card-subtitle">Current job status breakdown</p>
          </div>

          <div class="status-compact-list">
            <template v-if="getJobStatusesOverall.response.value !== undefined">
              <div v-for="status in statuses" :key="status.name" class="status-compact-row">
                <div class="status-compact-info">
                  <div
                    class="status-mini-dot"
                    :style="{ backgroundColor: seriesColors[status.name] }"
                  ></div>
                  <span class="status-compact-name">{{ status.name }}</span>
                </div>
                <div class="status-compact-values">
                  <span class="status-compact-count">{{ status.count }}</span>
                  <span class="status-compact-percentage">{{ status.percentage }}%</span>
                </div>
              </div>
            </template>
            <template v-else>
              <div v-for="i in 8" :key="i" class="status-compact-row">
                <div class="status-compact-info">
                  <div class="skeleton-circle status-dot-skeleton"></div>
                  <div class="skeleton-text status-name-skeleton"></div>
                </div>
                <div class="status-compact-values">
                  <div class="skeleton-text status-count-skeleton"></div>
                  <div class="skeleton-text status-percentage-skeleton"></div>
                </div>
              </div>
            </template>
          </div>
        </div>

        <!-- Functions Table -->
        <div class="content-card functions-table">
          <div class="card-header">
            <h2 class="card-title">
              <v-icon class="title-icon" color="primary">mdi-function-variant</v-icon>
              Registered Functions
            </h2>
            <p class="card-subtitle">{{ functionItems.length }} active function handlers</p>
          </div>

          <div class="table-container">
            <v-data-table
              :headers="functionHeaders"
              :items="functionItems"
              :items-per-page="functionsPerPage"
              :page="currentFunctionsPage"
              :items-per-page-options="availablePageSizes"
              class="functions-table-vuetify"
              density="compact"
              hover
              @update:options="handleTableOptionsUpdate"
            >
              <!-- Function Name Column -->
              <template v-slot:item.function="{ item }">
                <span class="function-name">{{ item.function }}</span>
              </template>

              <!-- Request Namespace Column -->
              <template v-slot:item.request="{ item }">
                <span class="namespace-text" :title="item.request">
                  {{ item.request }}
                </span>
              </template>

              <!-- Priority Column -->
              <template v-slot:item.priority="{ item }">
                <div
                  class="priority-badge"
                  :class="`priority-${item.priority.toLowerCase().replace('longrunning', 'long')}`"
                >
                  {{ item.priority }}
                </div>
              </template>

              <!-- Loading State -->
              <template v-slot:loading>
                <div class="loading-skeleton">
                  <div v-for="i in 5" :key="i" class="skeleton-row">
                    <div class="skeleton-text function-name-skeleton"></div>
                    <div class="skeleton-text namespace-skeleton"></div>
                    <div class="skeleton-text priority-skeleton"></div>
                  </div>
                </div>
              </template>
            </v-data-table>
          </div>
        </div>
      </div>

      <!-- Bottom Section -->
      <div class="bottom-grid">
        <!-- Machines -->
        <div class="content-card machines-card">
          <div class="card-header">
            <h2 class="card-title">
              <v-icon class="title-icon" color="primary">mdi-server-network</v-icon>
              Machines
            </h2>
            <p class="card-subtitle">{{ machineItems.length }} machine instances</p>
          </div>

          <div class="machines-list">
            <template v-if="machineItems.length > 0">
              <div
                v-for="machine in pagedMachineItems"
                :key="machine.machine"
                class="machine-item"
                :class="{ 'machine-item-current': isCurrentMachine(machine.machine) }"
              >
                <div class="machine-info">
                  <span class="machine-name">
                    {{ machine.machine }}
                    <span
                      v-if="isCurrentMachine(machine.machine)"
                      class="machine-current-pill"
                    >
                      Current
                    </span>
                  </span>
                  <span class="machine-jobs">{{ machine.locked }}</span>
                </div>
              </div>

              <div class="machines-pagination" v-if="totalMachinePages > 1">
                <v-btn
                  size="x-small"
                  variant="text"
                  icon="mdi-chevron-left"
                  :disabled="currentMachinesPage <= 1"
                  @click="currentMachinesPage = Math.max(1, currentMachinesPage - 1)"
                />
                <span class="machines-page-label">
                  Page {{ currentMachinesPage }} / {{ totalMachinePages }}
                </span>
                <v-btn
                  size="x-small"
                  variant="text"
                  icon="mdi-chevron-right"
                  :disabled="currentMachinesPage >= totalMachinePages"
                  @click="currentMachinesPage = Math.min(totalMachinePages, currentMachinesPage + 1)"
                />
              </div>
            </template>
            <template v-else>
              <div v-for="i in 3" :key="i" class="machine-item">
                <div class="machine-indicator">
                  <div class="skeleton-circle machine-dot-skeleton"></div>
                </div>
                <div class="machine-info">
                  <div class="skeleton-text machine-name-skeleton"></div>
                  <div class="skeleton-text machine-jobs-skeleton"></div>
                </div>
              </div>
            </template>
          </div>
        </div>

        <!-- System Alerts -->
        <div class="content-card alerts-card">
          <div class="card-header">
            <h2 class="card-title">
              <v-icon class="title-icon" color="primary">mdi-bell-ring</v-icon>
              System Alerts
            </h2>
            <p class="card-subtitle">Real-time system notifications</p>
          </div>

          <div class="alerts-container">
            <div v-if="hasError" class="alert alert-error">
              <div class="alert-icon">
                <v-icon color="error">mdi-alert-circle</v-icon>
              </div>
              <div class="alert-content">
                <h4 class="alert-title">System Error Detected</h4>
                <p class="alert-message">
                  {{ getOptions.response.value?.lastHostExceptionMessage }}
                </p>
              </div>
            </div>

            <div v-else-if="warningMessage" class="alert alert-warning">
              <div class="alert-icon">
                <v-icon color="warning">mdi-alert</v-icon>
              </div>
              <div class="alert-content">
                <h4 class="alert-title">High Thread Usage</h4>
                <p class="alert-message">
                  All worker threads are currently active. Consider optimizing task execution or
                  increasing concurrency limits.
                </p>
              </div>
            </div>

            <div v-else class="alert alert-success">
              <div class="alert-icon">
                <v-icon color="success">mdi-check-circle</v-icon>
              </div>
              <div class="alert-content">
                <h4 class="alert-title">System Operating Normally</h4>
                <p class="alert-message">All systems are functioning within normal parameters.</p>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
<style scoped>
/* Dashboard Container */
.dashboard-container {
  min-height: 100vh;
  background: linear-gradient(135deg, #212121 0%, #2d2d2d 100%);
  padding: 0;
  font-family:
    'Inter',
    -apple-system,
    BlinkMacSystemFont,
    sans-serif;
  position: relative;
  color: #e0e0e0;
}

/* Dashboard Content */
.dashboard-content {
  max-width: 1400px;
  margin: 0 auto;
  padding: 20px 24px 16px 24px;
}

/* Metrics Grid */
.metrics-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
  margin-bottom: 20px;
}

.metric-card {
  background: rgba(66, 66, 66, 0.9);
  backdrop-filter: blur(20px);
  border-radius: 12px;
  padding: 16px;
  display: flex;
  align-items: center;
  gap: 12px;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.4);
  border: 1px solid rgba(255, 255, 255, 0.1);
  transition: all 0.3s ease;
  position: relative;
  overflow: hidden;
}

.metric-card::before {
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 3px;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.3), transparent);
}

.metric-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 25px rgba(0, 0, 0, 0.6);
}

.primary-metric::before {
  background: linear-gradient(90deg, #3b82f6, #1d4ed8);
}
.info-metric::before {
  background: linear-gradient(90deg, #06b6d4, #0891b2);
}
.success-metric::before {
  background: linear-gradient(90deg, #10b981, #059669);
}
.warning-metric::before {
  background: linear-gradient(90deg, #f59e0b, #d97706);
}

.metric-icon {
  background: rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  padding: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.metric-label {
  font-size: 0.875rem;
  font-weight: 600;
  color: #bdbdbd;
  margin: 0 0 4px 0;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.metric-value {
  font-size: 1.25rem;
  font-weight: 700;
  margin: 0;
  letter-spacing: -0.5px;
  color: #e0e0e0;
}

.primary-text {
  color: #64b5f6;
}
.info-text {
  color: #4dd0e1;
}
.success-text {
  color: #4caf50;
}
.warning-text {
  color: #ffb74d;
}

/* Statistics Section */
.stats-section {
  margin-bottom: 20px;
}

.section-title {
  font-size: 1.25rem;
  font-weight: 700;
  color: #e0e0e0;
  margin: 0 0 16px 0;
  display: flex;
  align-items: center;
  gap: 8px;
}

.section-icon {
  background: rgba(100, 181, 246, 0.2);
  border-radius: 6px;
  padding: 6px;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 16px;
}

.stat-card {
  background: rgba(66, 66, 66, 0.9);
  backdrop-filter: blur(20px);
  border-radius: 12px;
  padding: 16px;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.4);
  border: 1px solid rgba(255, 255, 255, 0.1);
  transition: all 0.3s ease;
}

.stat-card:hover {
  transform: translateY(-1px);
  box-shadow: 0 6px 20px rgba(0, 0, 0, 0.6);
}

.stat-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 12px;
}

.stat-title {
  font-size: 0.875rem;
  font-weight: 600;
  color: #bdbdbd;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.stat-number {
  font-size: 1.75rem;
  font-weight: 800;
  margin-bottom: 6px;
  letter-spacing: -0.5px;
}

.success-number {
  color: #4caf50;
}
.error-number {
  color: #f44336;
}
.info-number {
  color: #64b5f6;
}

.stat-trend {
  display: flex;
  align-items: center;
  gap: 4px;
}

.trend-text {
  font-size: 0.75rem;
  color: #bdbdbd;
  font-weight: 500;
}

/* Content Grid */
.content-grid {
  display: grid;
  grid-template-columns: 1fr 2fr;
  gap: 20px;
  margin-bottom: 20px;
}

.content-card {
  background: rgba(66, 66, 66, 0.9);
  backdrop-filter: blur(20px);
  border-radius: 12px;
  padding: 20px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.4);
  border: 1px solid rgba(255, 255, 255, 0.1);
  transition: all 0.3s ease;
}

.content-card:hover {
  box-shadow: 0 8px 25px rgba(0, 0, 0, 0.6);
}

.card-header {
  margin-bottom: 16px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  padding-bottom: 12px;
}

.card-title {
  font-size: 1.125rem;
  font-weight: 700;
  color: #e0e0e0;
  margin: 0 0 6px 0;
  display: flex;
  align-items: center;
  gap: 8px;
}

.title-icon {
  background: rgba(100, 181, 246, 0.2);
  border-radius: 6px;
  padding: 4px;
}

.card-subtitle {
  font-size: 0.875rem;
  color: #bdbdbd;
  margin: 0;
  font-weight: 500;
}

/* Status Compact List */
.status-compact-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.status-compact-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 6px 8px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 6px;
  transition: all 0.2s ease;
}

.status-compact-row:hover {
  background: rgba(255, 255, 255, 0.1);
}

.status-compact-info {
  display: flex;
  align-items: center;
  gap: 8px;
}

.status-mini-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}

.status-compact-name {
  font-weight: 500;
  color: #e0e0e0;
  font-size: 0.75rem;
}

.status-compact-values {
  display: flex;
  align-items: center;
  gap: 8px;
}

.status-compact-count {
  font-weight: 600;
  color: #e0e0e0;
  font-size: 0.8rem;
  min-width: 20px;
  text-align: right;
}

.status-compact-percentage {
  font-size: 0.7rem;
  color: #bdbdbd;
  font-weight: 500;
  min-width: 30px;
  text-align: right;
}

/* Premium Table */
.table-container {
  background: rgba(0, 0, 0, 0.2);
  border-radius: 12px;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.table-scroll {
  max-height: 300px;
  overflow-y: auto;
}

.premium-table {
  width: 100%;
  border-collapse: collapse;
}

.premium-table th {
  background: rgba(100, 181, 246, 0.1);
  padding: 12px;
  text-align: left;
  font-weight: 700;
  font-size: 0.7rem;
  color: #bdbdbd;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}

.premium-table td {
  padding: 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
  font-size: 0.8rem;
  color: #e0e0e0;
}

.table-row:hover {
  background: rgba(255, 255, 255, 0.05);
}

.function-name {
  font-weight: 600;
  color: #e0e0e0;
}

.namespace-text {
  color: #bdbdbd;
  max-width: 200px;
  display: inline-block;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.priority-badge {
  padding: 6px 12px;
  border-radius: 20px;
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.priority-high {
  background: rgba(244, 67, 54, 0.2);
  color: #f44336;
}

.priority-normal {
  background: rgba(100, 181, 246, 0.2);
  color: #64b5f6;
}

.priority-low {
  background: rgba(158, 158, 158, 0.2);
  color: #9e9e9e;
}

.priority-long {
  background: rgba(255, 183, 77, 0.2);
  color: #ffb74d;
}

/* Bottom Grid */
.bottom-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
}

/* Machines */
.machines-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.machine-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 8px;
  transition: all 0.2s ease;
}

.machine-item:hover {
  background: rgba(255, 255, 255, 0.1);
  transform: translateX(2px);
}

.machine-indicator {
  display: flex;
  align-items: center;
}

.machine-dot {
  width: 10px;
  height: 10px;
  background: #4caf50;
  border-radius: 50%;
  animation: pulse-success 2s infinite;
}

.machine-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.machine-name {
  font-weight: 600;
  color: #e0e0e0;
  font-size: 0.875rem;
}

.machine-jobs {
  font-size: 0.75rem;
  color: #bdbdbd;
  font-weight: 500;
}

/* Alerts */
.alerts-container {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.alert {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 16px;
  border-radius: 8px;
  border-left: 3px solid;
  transition: all 0.2s ease;
}

.alert:hover {
  transform: translateX(2px);
}

.alert-error {
  background: rgba(244, 67, 54, 0.1);
  border-left-color: #f44336;
}

.alert-warning {
  background: rgba(255, 183, 77, 0.1);
  border-left-color: #ffb74d;
}

.alert-success {
  background: rgba(76, 175, 80, 0.1);
  border-left-color: #4caf50;
}

.alert-icon {
  flex-shrink: 0;
  background: rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  padding: 6px;
}

.alert-title {
  font-size: 0.875rem;
  font-weight: 700;
  color: #e0e0e0;
  margin: 0 0 4px 0;
}

.alert-message {
  font-size: 0.8rem;
  color: #bdbdbd;
  margin: 0;
  line-height: 1.4;
}

/* Responsive Design */
@media (max-width: 1400px) {
  .header-content,
  .dashboard-content {
    padding-left: 32px;
    padding-right: 32px;
  }
}

@media (max-width: 1024px) {
  .content-grid {
    grid-template-columns: 1fr;
  }

  .bottom-grid {
    grid-template-columns: 1fr;
  }

  .header-content,
  .dashboard-content {
    padding-left: 24px;
    padding-right: 24px;
  }
}

@media (max-width: 768px) {
  .header-content {
    flex-direction: column;
    align-items: stretch;
    text-align: center;
    padding-left: 16px;
    padding-right: 16px;
  }

  .dashboard-content {
    padding: 20px 16px 16px 16px;
  }

  .metrics-grid {
    grid-template-columns: 1fr;
  }

  .stats-grid {
    grid-template-columns: 1fr;
  }

  .action-buttons {
    justify-content: center;
  }
}

/* Skeleton Loading Animations */
@keyframes skeleton-loading {
  0% {
    background-position: -200px 0;
  }
  100% {
    background-position: calc(200px + 100%) 0;
  }
}

.skeleton-text {
  background: linear-gradient(
    90deg,
    rgba(255, 255, 255, 0.1) 25%,
    rgba(255, 255, 255, 0.2) 37%,
    rgba(255, 255, 255, 0.1) 63%
  );
  background-size: 400px 100%;
  animation: skeleton-loading 1.4s ease-in-out infinite;
  border-radius: 4px;
}

.skeleton-circle {
  background: linear-gradient(
    90deg,
    rgba(255, 255, 255, 0.1) 25%,
    rgba(255, 255, 255, 0.2) 37%,
    rgba(255, 255, 255, 0.1) 63%
  );
  background-size: 400px 100%;
  animation: skeleton-loading 1.4s ease-in-out infinite;
  border-radius: 50%;
}

/* Skeleton Specific Sizes */
.metric-value-skeleton {
  height: 20px;
  width: 80px;
}

.stat-number-skeleton {
  height: 28px;
  width: 60px;
  margin-bottom: 6px;
}

.status-dot-skeleton {
  width: 8px;
  height: 8px;
}

.status-name-skeleton {
  height: 12px;
  width: 60px;
}

.status-count-skeleton {
  height: 12px;
  width: 20px;
}

.status-percentage-skeleton {
  height: 12px;
  width: 30px;
}

.function-name-skeleton {
  height: 14px;
  width: 120px;
}

.namespace-skeleton {
  height: 14px;
  width: 150px;
}

.priority-skeleton {
  height: 18px;
  width: 60px;
  border-radius: 12px;
}

.machine-dot-skeleton {
  width: 10px;
  height: 10px;
}

.machine-name-skeleton {
  height: 14px;
  width: 100px;
  margin-bottom: 4px;
}

.machine-jobs-skeleton {
  height: 12px;
  width: 60px;
}

/* Scrollbar Styling */
.table-scroll::-webkit-scrollbar {
  width: 6px;
}

.table-scroll::-webkit-scrollbar-track {
  background: rgba(var(--v-theme-surface), 0.1);
  border-radius: 3px;
}

.table-scroll::-webkit-scrollbar-thumb {
  background: rgba(var(--v-theme-primary), 0.3);
  border-radius: 3px;
}

.table-scroll::-webkit-scrollbar-thumb:hover {
  background: rgba(var(--v-theme-primary), 0.5);
}

/* Vuetify Table Styles */
.functions-table-vuetify {
  background: transparent !important;
  border-radius: 12px !important;
  overflow: hidden !important;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.4) !important;
  border: 1px solid rgba(255, 255, 255, 0.1) !important;
}

.functions-table-vuetify .v-data-table__wrapper {
  background: transparent !important;
}

.functions-table-vuetify .v-data-table-header {
  background: rgba(66, 66, 66, 0.9) !important;
  backdrop-filter: blur(20px) !important;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1) !important;
  padding: 0 !important;
}

.functions-table-vuetify .v-data-table-header th {
  color: #bdbdbd !important;
  font-weight: 600 !important;
  font-size: 0.875rem !important;
  text-transform: uppercase !important;
  letter-spacing: 0.5px !important;
  padding: 16px !important;
  border-bottom: none !important;
  background: transparent !important;
}

.functions-table-vuetify .v-data-table__tr {
  background: transparent !important;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05) !important;
  transition: all 0.3s ease !important;
}

.functions-table-vuetify .v-data-table__tr:hover {
  background: rgba(255, 255, 255, 0.05) !important;
  transform: translateY(-1px) !important;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2) !important;
}

.functions-table-vuetify .v-data-table__td {
  padding: 16px !important;
  border-bottom: none !important;
  color: #e0e0e0 !important;
  background: transparent !important;
}

.functions-table-vuetify .v-data-table-footer {
  background: rgba(66, 66, 66, 0.9) !important;
  backdrop-filter: blur(20px) !important;
  border-top: 1px solid rgba(255, 255, 255, 0.1) !important;
  color: #bdbdbd !important;
  padding: 16px !important;
}

.functions-table-vuetify .v-data-table-footer .v-data-table-footer__items-per-page {
  color: #bdbdbd !important;
  font-size: 0.875rem !important;
  font-weight: 500 !important;
}

.functions-table-vuetify .v-data-table-footer .v-data-table-footer__items-per-page .v-select {
  color: #bdbdbd !important;
}

.functions-table-vuetify .v-data-table-footer .v-data-table-footer__items-per-page .v-field {
  background: rgba(255, 255, 255, 0.05) !important;
  border: 1px solid rgba(255, 255, 255, 0.1) !important;
  border-radius: 8px !important;
}

.functions-table-vuetify .v-data-table-footer .v-data-table-footer__items-per-page .v-field__input {
  color: #bdbdbd !important;
}

.functions-table-vuetify .v-data-table-footer .v-data-table-footer__pagination {
  color: #bdbdbd !important;
  font-size: 0.875rem !important;
  font-weight: 500 !important;
}

.functions-table-vuetify .v-data-table-footer .v-btn {
  color: #bdbdbd !important;
  background: rgba(255, 255, 255, 0.05) !important;
  border: 1px solid rgba(255, 255, 255, 0.1) !important;
  border-radius: 6px !important;
  transition: all 0.2s ease !important;
  min-width: 32px !important;
  height: 32px !important;
}

.functions-table-vuetify .v-data-table-footer .v-btn:hover {
  background: rgba(255, 255, 255, 0.1) !important;
  transform: translateY(-1px) !important;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2) !important;
}

.functions-table-vuetify .v-data-table-footer .v-btn--variant-elevated {
  background: var(--v-theme-primary) !important;
  color: white !important;
  border-color: var(--v-theme-primary) !important;
}

.functions-table-vuetify .v-data-table-footer .v-btn--variant-elevated:hover {
  background: var(--v-theme-primary) !important;
  transform: translateY(-1px) !important;
  box-shadow: 0 4px 12px rgba(var(--v-theme-primary), 0.3) !important;
}

.functions-table-vuetify .v-data-table-footer .v-btn:disabled {
  opacity: 0.5 !important;
  cursor: not-allowed !important;
}

/* Loading Skeleton */
.loading-skeleton {
  padding: 16px;
}

.skeleton-row {
  display: flex;
  gap: 16px;
  margin-bottom: 12px;
}

.skeleton-row .skeleton-text {
  flex: 1;
}

/* Pagination Styles */
.pagination-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 12px 0;
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  margin-top: 12px;
}

.pagination-info {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 16px;
  flex-wrap: wrap;
}

.pagination-text {
  font-size: 0.875rem;
  color: #bdbdbd;
  font-weight: 500;
}

.page-size-selector {
  display: flex;
  align-items: center;
  gap: 8px;
}

.page-size-label {
  font-size: 0.875rem;
  color: #bdbdbd;
  font-weight: 500;
}

.page-size-select {
  min-width: 80px;
  max-width: 100px;
}

.pagination-controls {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  justify-content: center;
}

.pagination-btn {
  font-weight: 500;
  text-transform: none;
  border-radius: 6px;
  transition: all 0.2s ease;
  min-width: auto;
  padding: 6px 12px;
  font-size: 0.875rem;
}

.pagination-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2);
}

.pagination-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.page-numbers {
  display: flex;
  align-items: center;
  gap: 2px;
}

.page-number-btn {
  min-width: 40px;
  height: 40px;
  border-radius: 8px;
  font-weight: 500;
  transition: all 0.2s ease;
}

.page-number-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2);
}

.page-number-btn:not(.v-btn--variant-elevated):hover {
  background: rgba(255, 255, 255, 0.1);
}

/* Responsive pagination */
@media (max-width: 768px) {
  .pagination-info {
    flex-direction: column;
    gap: 16px;
  }

  .pagination-controls {
    flex-direction: column;
    gap: 8px;
  }

  .page-numbers {
    order: -1;
  }

  .pagination-btn {
    width: 100%;
    max-width: 200px;
  }

  .page-size-selector {
    justify-content: center;
  }
}
</style>
