<script setup lang="ts">
import { watch, type PropType, toRef, onMounted, onUnmounted, ref } from 'vue'
import { cronTickerOccurrenceService } from '@/http/services/cronTickerOccurrenceService'
import { Status } from '@/http/services/types/base/baseHttpResponse.types'
import { tickerService } from '@/http/services/tickerService'
import { sleep } from '@/utilities/sleep'
import { useDialog } from '@/composables/useDialog'
import { methodName, type TickerNotificationHubType } from '@/hub/tickerNotificationHub'
import type { GetCronTickerOccurrenceResponse } from '@/http/services/types/cronTickerOccurrenceService.types'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
import PaginationFooter from '@/components/PaginationFooter.vue'
import { formatTime } from '@/utilities/dateTimeParser'
import { format } from 'timeago.js'

const confirmDialog = useDialog<{ data: string }>().withComponent(
  () => import('@/components/common/ConfirmDialog.vue'),
)

const exceptionDialog = useDialog<ConfirmDialogProps>().withComponent(
  () => import('@/components/common/ConfirmDialog.vue'),
)

// Use paginated service
const getByCronTickerIdPaginated = cronTickerOccurrenceService.getByCronTickerIdPaginated()
const requestCancelTicker = tickerService.requestCancel()
const deleteCronOccurrence = cronTickerOccurrenceService.deleteCronTickerOccurrence()

// Pagination state
const currentPage = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)

// Define headers manually for paginated response
const headers = ref([
  { title: 'Status', key: 'status', sortable: true, visibility: true },
  { title: 'Executed At (Elapsed Time)', key: 'executedAt', sortable: false, visibility: true },
  { title: 'Execution Time', key: 'executionTimeFormatted', sortable: true, visibility: true },
  { title: 'Locked At', key: 'lockedAt', sortable: false, visibility: true },
  { title: 'Lock Holder', key: 'lockHolder', sortable: false, visibility: true },
  { title: 'Retry Count', key: 'retryCount', sortable: false, visibility: true },
  { title: 'Actions', key: 'actions', sortable: false, visibility: true },
])

const props = defineProps({
  dialogProps: {
    type: Object as PropType<{ id: string, retries: number, retryIntervals: string[] }>,
    required: true,
  },
  tickerNotificationHub: {
    type: Object as PropType<TickerNotificationHubType>,
    required: true,
  },
  isOpen: {
    type: Boolean,
    required: true,
  },
})

const emit = defineEmits<{
  (e: 'close'): void
}>()

// Load page data with pagination
const loadPageData = async () => {
  if (props.dialogProps.id != undefined) {
    try {
      const response = await getByCronTickerIdPaginated.requestAsync(props.dialogProps.id, currentPage.value, pageSize.value)
      if (response) {
        totalCount.value = response.totalCount || 0
      }
    } catch (error) {
      console.error('Failed to load paginated data:', error)
    }
  }
}

// Handle page change
const handlePageChange = async (page: number) => {
  currentPage.value = page
  await loadPageData()
}

// Handle page size change  
const handlePageSizeChange = async (size: number) => {
  pageSize.value = size
  currentPage.value = 1  // Reset to first page when changing page size
  await loadPageData()
}

const addHubListeners = async () => {
  props.tickerNotificationHub.onReceiveUpdateCronTickerOccurrence((val:GetCronTickerOccurrenceResponse) => {
    // Update in-memory items array while preserving lockedAt and lockHolder
    const response = getByCronTickerIdPaginated.response.value;
    if (response && response.items) {
      const itemIndex = response.items.findIndex((item: GetCronTickerOccurrenceResponse) => item.id === val.id);
      if (itemIndex !== -1) {
        const currentItem = response.items[itemIndex];
        // Merge update while preserving lockedAt and lockHolder
        response.items[itemIndex] = {
          ...currentItem,
          ...val,
          status: Status[val.status as any],
          executedAt: `${format(val.executedAt)} (took ${formatTime(val.elapsedTime as number, true)})`,
          retryIntervals: currentItem.retryIntervals,
          lockedAt: currentItem.lockedAt, // Preserve existing lockedAt
          lockHolder: currentItem.lockHolder,
          executionTime: currentItem.executionTime // Preserve existing lockHolder
        };
      } else {
        // Item not found in current page, reload
        loadPageData();
      }
    } else {
      // No data loaded yet, reload
      loadPageData();
    }
  });

  props.tickerNotificationHub.onReceiveAddCronTickerOccurrence((val:GetCronTickerOccurrenceResponse) => {
    // Reload current page when new item is added
    loadPageData();
  });
}

onMounted(() => {
  addHubListeners();
})

onUnmounted(() => {
  props.tickerNotificationHub.stopReceiver(methodName.onReceiveUpdateCronTickerOccurrence)
  props.tickerNotificationHub.stopReceiver(methodName.onReceiveAddCronTickerOccurrence)
})

watch(
  () => props.isOpen,
  () => {
    if (props.isOpen && props.dialogProps.id != undefined)
      props.tickerNotificationHub.joinGroup(props.dialogProps.id)
    else if (!props.isOpen && props.dialogProps.id != undefined)
      props.tickerNotificationHub.leaveGroup(props.dialogProps.id)
  }
)

watch(
  () => props.dialogProps.id,
  async () => {
    if (props.dialogProps.id != undefined){
      currentPage.value = 1 // Reset to first page when id changes
      await loadPageData()
    }
  },
)

const hasStatus = (statusItem: string | number, statusEnum: Status) =>
  statusItem == Status[statusEnum]

const requestCancel = async (id: string) => {
  await requestCancelTicker
    .requestAsync(id)
    .then(async () => await sleep(100))
    .then(async () => {
      await loadPageData()
    })
}

const onSubmitConfirmDialog = async () => {
  confirmDialog.close()
  await deleteCronOccurrence
    .requestAsync(confirmDialog.propData?.data!)
    .then(async () => await sleep(100))
    .then(async () => {
      await loadPageData()
    })
}

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

const getStatusColor = (status: string | number) => {
  const statusStr = typeof status === 'number' ? Status[status] : status
  return seriesColors[statusStr] || '#999'
}

const getStatusIcon = (status: string | number) => {
  const statusStr = typeof status === 'number' ? Status[status] : status
  switch (statusStr) {
    case 'Idle': return 'mdi-clock-outline'
    case 'Queued': return 'mdi-queue'
    case 'InProgress': return 'mdi-progress-clock'
    case 'Done': return 'mdi-check-circle'
    case 'DueDone': return 'mdi-check-circle-outline'
    case 'Failed': return 'mdi-alert-circle'
    case 'Cancelled': return 'mdi-cancel'
    case 'Skipped': return 'mdi-skip-forward'
    default: return 'mdi-help-circle'
  }
}

const formatRetryIntervals = (item: any) => {
  if (!item.retryIntervals || item.retryIntervals.length === 0) {
    return []
  }

  const intervals = []
  const maxRetries = props.dialogProps.retries as number
  
  for (let i = 0; i < maxRetries; i++) {
    if (i < item.retryIntervals.length) {
      intervals.push({
        attempt: i + 1,
        interval: item.retryIntervals[i],
        status: i === item.retryCount - 1 ? item.status : 'Completed',
        isCurrent: i === item.retryCount - 1,
        isCompleted: i < item.retryCount - 1
      })
    } else {
      intervals.push({
        attempt: i + 1,
        interval: item.retryIntervals[item.retryIntervals.length - 1],
        status: 'Pending',
        isCurrent: false,
        isCompleted: false
      })
    }
  }
  
  return intervals
}

const setRowProp = (propContext: any) => {
  const status = propContext.item.status;
  const statusStr = typeof status === 'string' ? status : (status !== null && status !== undefined ? String(status) : 'Unknown');
  return { 
    class: `status-row status-${statusStr.toLowerCase()}`,
    style: `border-left: 4px solid ${getStatusColor(status)}`
  }
}
</script>

<template>
  <confirmDialog.Component
    :is-open="confirmDialog.isOpen"
    @close="confirmDialog.close()"
    @confirm="onSubmitConfirmDialog"
  />
  <exceptionDialog.Component
    :is-open="exceptionDialog.isOpen"
    @close="exceptionDialog.close()"
    :dialog-props="exceptionDialog.propData"
  />
  
  <v-dialog v-model="toRef(isOpen).value" max-width="1400" persistent>
    <v-card class="occurrences-dialog">
      <!-- Header -->
      <v-card-title class="dialog-header">
        <div class="header-content">
          <div class="header-left">
            <v-icon size="24" color="primary" class="header-icon">mdi-calendar-clock</v-icon>
            <div class="header-text">
              <h2 class="dialog-title">Cron Ticker Occurrences</h2>
              <p class="dialog-subtitle">Execution history and retry attempts</p>
            </div>
          </div>
          <v-btn 
            @click="emit('close')" 
            icon 
            variant="text" 
            size="small"
            class="close-btn"
            aria-label="Close"
          >
            <v-icon size="20">mdi-close</v-icon>
          </v-btn>
        </div>
      </v-card-title>

      <!-- Content -->
      <v-card-text class="dialog-content">
        <div v-if="getByCronTickerIdPaginated.loader.value" class="loading-container">
          <v-progress-circular indeterminate color="primary" size="64"></v-progress-circular>
          <p class="loading-text">Loading occurrences...</p>
        </div>

        <div v-else-if="!getByCronTickerIdPaginated.response.value || getByCronTickerIdPaginated.response.value?.items?.length === 0" class="empty-state">
          <v-icon size="64" color="grey-lighten-1">mdi-calendar-remove</v-icon>
          <h3 class="empty-title">No Occurrences Found</h3>
          <p class="empty-subtitle">This cron ticker hasn't been executed yet.</p>
        </div>

        <div v-else class="table-container">
          <v-data-table
            :headers="headers"
            :loading="getByCronTickerIdPaginated.loader.value"
            :items="getByCronTickerIdPaginated.response.value?.items || []"
            item-value="Id"
            :row-props="setRowProp"
            key="Id"
            :items-per-page="-1"
            hide-default-footer
            class="enhanced-table"
            density="compact"
            hover
            height="400"
            fixed-header
          >
            <!-- Status Column -->
            <template v-slot:item.status="{ item }">
              <div class="status-cell">
                <div class="status-badge" :style="{ backgroundColor: getStatusColor(item.status) }">
                  <v-icon size="14" color="white">{{ getStatusIcon(item.status) }}</v-icon>
                  <span class="status-text">{{ item.status }}</span>
                </div>
                
                <!-- Exception indicator for failed or skipped statuses -->
                <div 
                  v-if="(hasStatus(item.status, Status.Failed) && item.exceptionMessage) || (hasStatus(item.status, Status.Skipped) && item.skippedReason)"
                  class="exception-indicator"
                  @click="exceptionDialog.open({
                    ...new ConfirmDialogProps(),
                    title: hasStatus(item.status, Status.Skipped) ? 'Skipped Reason' : 'Exception Details',
                    text: hasStatus(item.status, Status.Skipped) ? item.skippedReason! : item.exceptionMessage!,
                    showConfirm: false,
                    maxWidth: '900',
                    icon: hasStatus(item.status, Status.Failed) ? 'mdi-bug-outline' : 'mdi-information-outline',
                    isException: hasStatus(item.status, Status.Failed),
                  })"
                >
                  <v-icon size="16" :color="hasStatus(item.status, Status.Failed) ? 'error' : 'warning'">
                    {{ hasStatus(item.status, Status.Failed) ? 'mdi-bug-outline' : 'mdi-information-outline' }}
                  </v-icon>
                  <v-tooltip activator="parent" location="top">
                    {{ hasStatus(item.status, Status.Failed) ? 'View Exception Details' : 'View Skipped Reason' }}
                  </v-tooltip>
                </div>
              </div>
            </template>

            <!-- Executed At Column -->
            <template v-slot:item.ExecutedAt="{ item }">
              <div class="executed-at-cell">
                <div v-if="hasStatus(item.status, Status.InProgress)" class="executing-indicator">
                  <v-icon size="16" class="spinning">mdi-loading</v-icon>
                  <span class="executing-text">Executing...</span>
                </div>
                <div v-else-if="hasStatus(item.status, Status.Cancelled) || hasStatus(item.status, Status.Queued)" class="na-text">
                  N/A
                </div>
                <div v-else class="execution-time">
                  {{ item.executedAt }}
                </div>
              </div>
            </template>

            <!-- Retry Intervals Column -->
            <template v-slot:item.retryIntervals="{ item }">
              <div class="retry-intervals-cell">
                <div v-if="!item.retryIntervals || item.retryIntervals.length === 0" class="no-retries">
                  <span class="na-text">N/A</span>
                </div>
                <div v-else class="retry-timeline-compact">
                  <div 
                    v-for="(retry, retryIndex) in formatRetryIntervals(item)" 
                    :key="retryIndex"
                    class="timeline-item-compact"
                    :class="{
                      'current': retry.isCurrent,
                      'completed': retry.isCompleted,
                      'pending': !retry.isCompleted && !retry.isCurrent
                    }"
                  >
                    <div class="timeline-marker-compact">
                      <v-icon 
                        v-if="retry.isCompleted" 
                        size="12" 
                        color="success"
                      >mdi-check-circle</v-icon>
                      <v-icon 
                        v-else-if="retry.isCurrent" 
                        size="12" 
                        color="primary"
                        class="spinning"
                      >mdi-progress-clock</v-icon>
                      <v-icon 
                        v-else 
                        size="12" 
                        color="grey-lighten-1"
                      >mdi-clock-outline</v-icon>
                    </div>
                    
                    <div class="timeline-content-compact">
                      <span class="attempt-number-compact">#{{ retry.attempt }}</span>
                      <span class="interval-time-compact">{{ retry.interval }}</span>
                    </div>
                  </div>
                </div>
              </div>
            </template>

            <!-- Actions Column -->
            <template v-slot:item.actions="{ item }">
              <div class="actions-cell">
                <v-btn
                  @click="requestCancel(item.id)"
                  :disabled="!hasStatus(item.status, Status.InProgress)"
                  icon
                  variant="text"
                  size="small"
                  class="action-btn cancel-btn"
                  :class="{ 'active': hasStatus(item.status, Status.InProgress) }"
                >
                  <v-icon size="18">mdi-cancel</v-icon>
                  <v-tooltip activator="parent" location="top">Cancel Execution</v-tooltip>
                </v-btn>
                
                <v-btn
                  @click="confirmDialog.open({ data: item.id })"
                  :disabled="hasStatus(item.status, Status.InProgress)"
                  icon
                  variant="text"
                  size="small"
                  class="action-btn delete-btn"
                >
                  <v-icon size="18">mdi-delete</v-icon>
                  <v-tooltip activator="parent" location="top">Delete Occurrence</v-tooltip>
                </v-btn>
              </div>
            </template>
          </v-data-table>
          
          <!-- Custom pagination footer -->
          <PaginationFooter
            :page="currentPage"
            :page-size="pageSize"
            :total-count="totalCount"
            :page-size-options="[10, 20, 50, 100]"
            @update:page="handlePageChange"
            @update:page-size="handlePageSizeChange"
          />
        </div>
      </v-card-text>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.occurrences-dialog {
  border-radius: 16px;
  overflow: hidden;
  background: linear-gradient(135deg, #1a1a1a 0%, #2d2d2d 100%);
  border: 1px solid rgba(255, 255, 255, 0.1);
  max-height: 90vh;
  display: flex;
  flex-direction: column;
}

.dialog-header {
  background: linear-gradient(135deg, rgba(100, 181, 246, 0.12) 0%, rgba(100, 181, 246, 0.04) 100%);
  border-bottom: 1px solid rgba(100, 181, 246, 0.2);
  padding: 16px;
}

.header-content {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 12px;
}

.header-icon {
  background: rgba(100, 181, 246, 0.15);
  border-radius: 8px;
  padding: 6px;
}

.header-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.dialog-title {
  font-size: 1.25rem;
  font-weight: 700;
  color: #ffffff;
  margin: 0;
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
}

.dialog-subtitle {
  font-size: 0.75rem;
  color: #bdbdbd;
  margin: 0;
}

.close-btn {
  color: #bdbdbd;
  transition: all 0.2s ease;
}

.close-btn:hover {
  color: #ffffff;
  background: rgba(255, 255, 255, 0.1);
}

.dialog-content {
  padding: 0;
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.loading-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 64px 32px;
  gap: 16px;
}

.loading-text {
  color: #bdbdbd;
  font-size: 1rem;
  margin: 0;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 64px 32px;
  gap: 16px;
  text-align: center;
}

.empty-title {
  color: #ffffff;
  font-size: 1.25rem;
  font-weight: 600;
  margin: 0;
}

.empty-subtitle {
  color: #bdbdbd;
  font-size: 0.875rem;
  margin: 0;
}

.table-container {
  padding: 16px;
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.enhanced-table {
  background: transparent;
  border-radius: 8px;
  overflow: hidden;
  flex: 1;
}

/* Table Row Styling */
:deep(.enhanced-table .v-data-table__tr) {
  transition: all 0.2s ease;
  border-bottom: 1px solid rgba(255, 255, 255, 0.03);
}

:deep(.enhanced-table .v-data-table__tr:hover) {
  background: rgba(255, 255, 255, 0.03) !important;
  transform: translateY(-1px);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
}

:deep(.enhanced-table .v-data-table-header) {
  background: linear-gradient(135deg, rgba(100, 181, 246, 0.12) 0%, rgba(100, 181, 246, 0.04) 100%);
  border-bottom: 1px solid rgba(100, 181, 246, 0.2);
}

:deep(.enhanced-table .v-data-table-header__td) {
  color: #ffffff;
  font-weight: 600;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.3px;
  padding: 8px 8px;
}

:deep(.enhanced-table .v-data-table__td) {
  padding: 6px 8px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.03);
}

:deep(.enhanced-table .v-data-table__wrapper) {
  overflow: hidden;
}

:deep(.enhanced-table .v-data-table__tbody) {
  overflow: hidden;
}

/* Status Row Styling */
.status-row {
  background: rgba(255, 255, 255, 0.01);
  transition: all 0.2s ease;
}

.status-row:hover {
  background: rgba(255, 255, 255, 0.05) !important;
}

/* Status Cell Styling */
.status-cell {
  display: flex;
  align-items: center;
  gap: 8px;
}

.status-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 8px;
  border-radius: 12px;
  font-weight: 600;
  font-size: 0.65rem;
  color: white;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.3);
  text-transform: uppercase;
  letter-spacing: 0.3px;
}

.status-text {
  font-size: 0.65rem;
}

.exception-indicator {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: rgba(255, 0, 0, 0.2);
  cursor: pointer;
  transition: all 0.2s ease;
}

.exception-indicator:hover {
  background: rgba(255, 0, 0, 0.3);
  transform: scale(1.05);
}

/* Executed At Cell Styling */
.executed-at-cell {
  display: flex;
  align-items: center;
  min-height: 20px;
}

.executing-indicator {
  display: flex;
  align-items: center;
  gap: 6px;
  color: #6495ED;
  font-weight: 600;
  font-size: 0.75rem;
}

.executing-text {
  font-size: 0.75rem;
}

.na-text {
  color: #bdbdbd;
  font-style: italic;
  font-size: 0.75rem;
}

.execution-time {
  color: #ffffff;
  font-size: 0.75rem;
  font-weight: 500;
}

/* Retry Intervals Cell Styling */
.retry-intervals-cell {
  min-height: 20px;
}

.no-retries {
  display: flex;
  align-items: center;
  height: 100%;
}

.retry-timeline-compact {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.timeline-item-compact {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 2px 6px;
  border-radius: 4px;
  transition: all 0.2s ease;
}

.timeline-item-compact.current {
  background: rgba(100, 181, 246, 0.12);
  border: 1px solid rgba(100, 181, 246, 0.25);
}

.timeline-item-compact.completed {
  background: rgba(76, 175, 80, 0.08);
  border: 1px solid rgba(76, 175, 80, 0.15);
}

.timeline-item-compact.pending {
  background: rgba(255, 255, 255, 0.01);
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.timeline-marker-compact {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.08);
  flex-shrink: 0;
}

.timeline-content-compact {
  display: flex;
  align-items: center;
  gap: 6px;
  flex: 1;
}

.attempt-number-compact {
  color: #ffffff;
  font-weight: 600;
  font-size: 0.65rem;
  min-width: 16px;
}

.interval-time-compact {
  color: #bdbdbd;
  font-size: 0.65rem;
  font-family: 'JetBrains Mono', monospace;
}

/* Actions Cell Styling */
.actions-cell {
  display: flex;
  align-items: center;
  gap: 6px;
  justify-content: center;
}

.action-btn {
  border-radius: 6px;
  transition: all 0.2s ease;
}

.action-btn:hover {
  transform: scale(1.05);
}

.cancel-btn.active {
  background: rgba(100, 181, 246, 0.15);
  color: #6495ED;
}

.delete-btn:hover {
  background: rgba(255, 0, 0, 0.15);
  color: #ff0000;
}

.spinning {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* Responsive Design */
@media (max-width: 768px) {
  .occurrences-dialog {
    margin: 12px;
    max-width: calc(100vw - 24px);
  }
  
  .dialog-header {
    padding: 12px;
  }
  
  .header-left {
    gap: 8px;
  }
  
  .dialog-title {
    font-size: 1.125rem;
  }
  
  .header-icon {
    padding: 4px;
  }
  
  .table-container {
    padding: 12px;
  }
  
  :deep(.enhanced-table .v-data-table-header__td),
  :deep(.enhanced-table .v-data-table__td) {
    padding: 4px 6px;
  }
}
</style>
