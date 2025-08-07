<script lang="ts" setup>
import { tickerService } from '@/http/services/tickerService'
import { formatDate } from '@/utilities/dateTimeParser'
import { computed, onMounted, onUnmounted, ref, watch, type Ref } from 'vue'
import TickerNotificationHub, { methodName } from '@/hub/tickerNotificationHub'
import { useFunctionNameStore } from '@/stores/functionNames'
import { Status } from '@/http/services/types/base/baseHttpResponse.types'
import { useDialog } from '@/composables/useDialog'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
import { sleep } from '@/utilities/sleep'

const getNextPlannedTicker = tickerService.getNextPlannedTicker()
const stopTicker = tickerService.stopTicker()
const startTicker = tickerService.startTicker()
const restartTicker = tickerService.restartTicker()
const getTickerHostStatus = tickerService.getTickerHostStatus()
const getOptions = tickerService.getOptions()
const getMachineJobs = tickerService.getMachineJobs()
const getJobStatusesPastWeek = tickerService.getJobStatusesPastWeek()
const getJobStatusesOverall = tickerService.getJobStatusesOverall()
const functionNamesStore = useFunctionNameStore()

const activeThreads = ref(0)
const tickerHostStatus = ref(false)

const confirmDialog = useDialog<ConfirmDialogProps>().withComponent(
  () => import('@/components/common/ConfirmDialog.vue'),
)

onMounted(async () => {
  await TickerNotificationHub.startConnection()
  await getNextPlannedTicker.requestAsync()
  await getOptions.requestAsync()
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
    res.forEach((item) => {
      machineItems.value.push({
        machine: item.item1,
        locked: `${item.item2} Jobs`,
      })
    })
  })
  await functionNamesStore.loadData().then((res) => {
    res.value?.forEach((item) => {
      const request = item.functionRequestNamespace == '' ? 'N/A' : item.functionRequestNamespace
      functionItems.value.push({
        function: item.functionName,
        request: request,
        priority: tickerTaskPriority[item.priority],
      })
    })
  })

  await getTickerHostStatus.requestAsync().then((res) => {
    tickerHostStatus.value = res.isRunning
  })

  TickerNotificationHub.onReceiveThreadsActive((threads: number) => {
    activeThreads.value = threads
  })

  TickerNotificationHub.onReceiveNextOccurrence((nextOccurrence: string) => {
    getNextPlannedTicker.updateProperty('nextOccurrence', nextOccurrence)
  })

  TickerNotificationHub.onReceiveHostStatus((status: boolean) => {
    tickerHostStatus.value = status
  })

  TickerNotificationHub.onReceiveHostExceptionMessage((message: string) => {
    getOptions.updateProperty('lastHostExceptionMessage', message)
  })
})

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
  { name: 'Batched', count: 0, percentage: '0' },
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
  Batched: '#A9A9A9', // Dark Gray
}

const machineItems: Ref<Array<{ machine: string; locked: string }>> = ref([])

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
</script>
<template>
  <div class="dashboard-container">
    <!-- Header Section -->
    <div class="dashboard-header">
      <div class="header-content">
        <div class="status-section">
          <div class="status-indicator">
            <div 
              class="status-pulse"
              :class="{ 'pulse-active': tickerHostStatus, 'pulse-inactive': !tickerHostStatus }"
            ></div>
            <div class="status-info">
              <div class="system-details">
                <span class="machine-name">{{ getOptions.response.value?.currentMachine }}</span>
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
        </div>
        
        <div class="action-section">
          <div class="action-buttons">
              <v-btn
              v-if="!tickerHostStatus"
                color="success"
              variant="elevated"
              size="small"
              prepend-icon="mdi-play-circle"
                @click="startTicker.requestAsync()"
                :loading="startTicker.loader.value"
              class="action-btn start-btn"
            >
              Start System
            </v-btn>
            
            <template v-if="tickerHostStatus">
              <v-btn
                color="warning"
                variant="elevated"
                size="small"
                prepend-icon="mdi-restart"
                @click="
                  restartTicker.requestAsync().then(async () => {
                    restartTicker.loader.value = true;
                    await sleep(1000).then(() => {
                      restartTicker.loader.value = false
                    })
                  })
                "
                :loading="restartTicker.loader.value"
                class="action-btn restart-btn"
              >
                Restart
              </v-btn>
              
              <v-btn
                color="error"
                variant="elevated"
                size="small"
                prepend-icon="mdi-stop-circle"
                @click="confirmDialog.open({ ...new ConfirmDialogProps(), confirmText: 'Stop' })"
                :loading="stopTicker.loader.value"
                class="action-btn stop-btn"
              >
                Stop System
              </v-btn>
          </template>
          </div>
        </div>
      </div>
    </div>

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
            <p class="metric-value primary-text" v-if="getNextPlannedTicker.response.value !== undefined">
              {{ getNextPlannedTicker.response.value?.nextOccurrence == undefined
                ? 'Not Scheduled'
                : formatDate(getNextPlannedTicker.response.value?.nextOccurrence) }}
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
            <v-icon 
              size="28" 
              :color="warningMessage ? 'warning' : 'success'"
            >
              {{ warningMessage ? 'mdi-alert-circle-outline' : 'mdi-pulse' }}
            </v-icon>
          </div>
          <div class="metric-content">
            <h3 class="metric-label">Active Threads</h3>
            <p 
              class="metric-value"
              :class="warningMessage ? 'warning-text' : 'success-text'"
            >
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
            <p class="metric-value success-text" v-if="getJobStatusesPastWeek.response.value !== undefined">
              {{ getJobStatusesPastWeek.response.value != undefined && getJobStatusesPastWeek.response.value[2].item2 > 0
                ? Math.round(((getJobStatusesPastWeek.response.value[2].item2 - getJobStatusesPastWeek.response.value[1].item2) / getJobStatusesPastWeek.response.value[2].item2) * 100)
                : 0 }}%
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
            <div class="stat-number success-number" v-if="getJobStatusesPastWeek.response.value !== undefined">
              {{ getJobStatusesPastWeek.response.value[2].item2 - getJobStatusesPastWeek.response.value[1].item2 }}
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
            <div class="stat-number error-number" v-if="getJobStatusesPastWeek.response.value !== undefined">
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
              <span class="stat-title">Total</span>
            </div>
            <div class="stat-number info-number" v-if="getJobStatusesPastWeek.response.value !== undefined">
              {{ getJobStatusesPastWeek.response.value[2].item2 }}
            </div>
            <div v-else class="skeleton-text stat-number-skeleton"></div>
            <div class="stat-trend">
              <v-icon size="14" color="info">mdi-chart-line</v-icon>
              <span class="trend-text">Jobs processed</span>
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
            <div 
              v-for="status in statuses" 
              :key="status.name"
              class="status-compact-row"
            >
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
          <div class="table-scroll">
                         <table class="premium-table">
               <thead>
                 <tr>
                   <th>Function Name</th>
                   <th>Request Namespace</th>
                   <th>Priority</th>
                 </tr>
               </thead>
               <tbody>
                 <template v-if="functionItems.length > 0">
                   <tr v-for="item in functionItems" :key="item.function" class="table-row">
                     <td class="function-name">{{ item.function }}</td>
                     <td class="request-namespace">
                       <span 
                         class="namespace-text"
                         :title="item.request"
                       >
                         {{ item.request }}
                       </span>
                     </td>
                     <td class="priority-cell">
                       <div 
                         class="priority-badge"
                         :class="`priority-${item.priority.toLowerCase().replace('longrunning', 'long')}`"
                       >
                         {{ item.priority }}
                       </div>
                     </td>
                   </tr>
                 </template>
                 <template v-else>
                   <tr v-for="i in 5" :key="i" class="table-row">
                     <td><div class="skeleton-text function-name-skeleton"></div></td>
                     <td><div class="skeleton-text namespace-skeleton"></div></td>
                     <td><div class="skeleton-text priority-skeleton"></div></td>
                   </tr>
                 </template>
               </tbody>
             </table>
          </div>
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
            Active Machines
          </h2>
          <p class="card-subtitle">{{ machineItems.length }} machine instances</p>
        </div>
        
                 <div class="machines-list">
           <template v-if="machineItems.length > 0">
             <div 
               v-for="machine in machineItems" 
               :key="machine.machine"
               class="machine-item"
             >
               <div class="machine-indicator">
                 <div class="machine-dot"></div>
               </div>
               <div class="machine-info">
                 <span class="machine-name">{{ machine.machine }}</span>
                 <span class="machine-jobs">{{ machine.locked }}</span>
               </div>
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
              <p class="alert-message">{{ getOptions.response.value?.lastHostExceptionMessage }}</p>
            </div>
          </div>
          
          <div v-else-if="warningMessage" class="alert alert-warning">
            <div class="alert-icon">
              <v-icon color="warning">mdi-alert</v-icon>
            </div>
            <div class="alert-content">
              <h4 class="alert-title">High Thread Usage</h4>
              <p class="alert-message">All worker threads are currently active. Consider optimizing task execution or increasing concurrency limits.</p>
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

  <confirmDialog.Component
    :is-open="confirmDialog.isOpen"
    @close="confirmDialog.close()"
    :dialog-props="confirmDialog.propData"
    @confirm="
      stopTicker.requestAsync().then(() => {
        confirmDialog.close()
        getNextPlannedTicker.updateProperty('nextOccurrence', undefined)
      })
    "
  />
</template>
<style scoped>
/* Dashboard Container */
.dashboard-container {
  min-height: 100vh;
  background: linear-gradient(135deg, #212121 0%, #2d2d2d 100%);
  padding: 0;
  font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
  position: relative;
  color: #e0e0e0;
}

/* Header Section */
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

/* Dashboard Content */
.dashboard-content {
  max-width: 1400px;
  margin: 0 auto;
  padding: 20px 24px 16px 24px;
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

.status-section {
  display: flex;
  align-items: center;
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

.primary-metric::before { background: linear-gradient(90deg, #3b82f6, #1d4ed8); }
.info-metric::before { background: linear-gradient(90deg, #06b6d4, #0891b2); }
.success-metric::before { background: linear-gradient(90deg, #10b981, #059669); }
.warning-metric::before { background: linear-gradient(90deg, #f59e0b, #d97706); }

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

.primary-text { color: #64b5f6; }
.info-text { color: #4dd0e1; }
.success-text { color: #4caf50; }
.warning-text { color: #ffb74d; }

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

.success-number { color: #4caf50; }
.error-number { color: #f44336; }
.info-number { color: #64b5f6; }

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
  border-radius: 8px;
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
  background: linear-gradient(90deg, rgba(255, 255, 255, 0.1) 25%, rgba(255, 255, 255, 0.2) 37%, rgba(255, 255, 255, 0.1) 63%);
  background-size: 400px 100%;
  animation: skeleton-loading 1.4s ease-in-out infinite;
  border-radius: 4px;
}

.skeleton-circle {
  background: linear-gradient(90deg, rgba(255, 255, 255, 0.1) 25%, rgba(255, 255, 255, 0.2) 37%, rgba(255, 255, 255, 0.1) 63%);
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
</style>
