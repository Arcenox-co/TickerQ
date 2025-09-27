<script lang="ts" setup>
import { onMounted, ref, provide, computed, onUnmounted, nextTick, watch } from 'vue'
import { timeTickerService } from '@/http/services/timeTickerService'
import type { GetTimeTickerResponse } from '@/http/services/types/timeTickerService.types'
import { Status } from '@/http/services/types/base/baseHttpResponse.types'
import { tickerService } from '@/http/services/tickerService'
import { useDialog } from '@/composables/useDialog'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
import ChainJobsModal from '@/components/ChainJobsModal.vue'
import TickerNotificationHub, { methodName } from '@/hub/tickerNotificationHub'
import { formatDate, formatFromUtcToLocal } from '@/utilities/dateTimeParser'
import { useConnectionStore } from '@/stores/connectionStore'

const getTimeTickers = timeTickerService.getTimeTickers()
const deleteTimeTicker = timeTickerService.deleteTimeTicker()
const requestCancelTicker = tickerService.requestCancel()

const crudTimeTickerDialog = useDialog<
  GetTimeTickerResponse & { isFromDuplicate: boolean }
>().withComponent(
  () => import('@/components/timetickerComponents/CRUDTimeTickerDialogComponent.vue'),
)

const confirmDialog = useDialog<ConfirmDialogProps & { id: string }>().withComponent(
  () => import('@/components/common/ConfirmDialog.vue'),
)

const tickerRequestDialog = useDialog<{ id: string }>().withComponent(
  () => import('@/components/common/TickerRequestDialog.vue'),
)

const exceptionDialog = useDialog<ConfirmDialogProps>().withComponent(
  () => import('@/components/common/ConfirmDialog.vue'),
)


const requestMatchType = ref(new Map<string, number>())
const crudTimeTickerDialogRef = ref(null)

// Chain Jobs Modal
const chainJobsModal = ref({
  isOpen: false
})

const expandedParents = ref(new Set<string>())
const tableSearch = ref('')

const selectedItems = ref(new Set<string>())

onMounted(async () => {
    // Initialize WebSocket connection
    try {
      const connectionStore = useConnectionStore()
      if (!connectionStore.isInitialized) {
        await connectionStore.initializeConnectionWithRetry()
      }
    } 
    catch (error: any) {}
    
    // Load initial data
    try {
      await getTimeTickers.requestAsync()
    } catch (error) {
      // Failed to load time tickers
    }
    
    // Add hub listeners
    try {
      await addHubListeners()
    } catch (error) {
      // Failed to add hub listeners
    }
    
})

onUnmounted(() => {  
  TickerNotificationHub.stopReceiver(methodName.onReceiveAddTimeTicker)
  TickerNotificationHub.stopReceiver(methodName.onReceiveUpdateTimeTicker)
  TickerNotificationHub.stopReceiver(methodName.onReceiveDeleteTimeTicker)
})

const addHubListeners = async () => {
  TickerNotificationHub.onReceiveAddTimeTicker<GetTimeTickerResponse>((response) => {
    getTimeTickers.addToResponse(response)
  })

  TickerNotificationHub.onReceiveUpdateTimeTicker<GetTimeTickerResponse>((response) => {
    getTimeTickers.updateByKey('id', response, ['requestType'])
    if (crudTimeTickerDialog.isOpen && crudTimeTickerDialog.propData?.id == response.id) {
      crudTimeTickerDialog.setPropData({
        ...response,
        executionTime: formatFromUtcToLocal(response.executionTime),
        isFromDuplicate: false,
      })
      nextTick(() => {
        ;(crudTimeTickerDialogRef.value as any)?.resetForm()
      })
    }
  })

  TickerNotificationHub.onReceiveDeleteTimeTicker<string>((id) => {
    getTimeTickers.removeFromResponse('id', id)
  })
}

// Process data to create hierarchical structure with nested children
const processedTableData = computed(() => {
  const rawData = getTimeTickers.response.value || []
  const result: any[] = []

  // Simple function to flatten the hierarchy based on expanded state
  const flattenData = (items: any[], depth: number = 0) => {
    items.forEach((item) => {
      // Add the current item
      const processedItem = {
        ...item,
        isChild: depth > 0,
        depth: depth,
        isParent: item.children && item.children.length > 0,
        children: item.children || []
      }
      result.push(processedItem)

      // If this item is expanded and has children, add them recursively
      if (expandedParents.value.has(item.id) && item.children && item.children.length > 0) {
        flattenData(item.children, depth + 1)
      }
    })
  }

  // Start flattening from root level
  flattenData(rawData, 0)

  return result
})

const headersWithSelection = computed(() => {
  const headers = [...(getTimeTickers.headers.value || [])]
  headers.unshift({
    title: '',
    key: 'selection',
    sortable: false,
    visibility: true,
  })
  return headers
})

const toggleItemSelection = (itemId: string) => {
  if (selectedItems.value.has(itemId)) {
    selectedItems.value.delete(itemId)
  } else {
    selectedItems.value.add(itemId)
  }
  selectedItems.value = new Set(selectedItems.value)
}

const clearSelection = () => {
  selectedItems.value.clear()
}

// Chain Jobs Modal Methods
const openChainJobsModal = () => {
  chainJobsModal.value.isOpen = true
}

const onChainJobsCreated = async (result: any) => {
  console.log('Chain jobs created successfully!', result)
  await getTimeTickers.requestAsync()
}


const toggleParentExpansion = (parentId: string) => {  
  if (expandedParents.value.has(parentId)) {
    expandedParents.value.delete(parentId)
  } else {
    expandedParents.value.add(parentId)
  }
  // Trigger reactivity
  expandedParents.value = new Set(expandedParents.value)
}

const isParentExpanded = (parentId: string) => {
  return expandedParents.value.has(parentId)
}

const getChildrenCount = (parentId: string) => {
  // First check in raw data
  const rawData = getTimeTickers.response.value || []
  
  // Recursive function to find item by ID in nested structure
  const findItemById = (items: any[], id: string): any => {
    for (const item of items) {
      if (item.id === id) {
        return item
      }
      if (item.children && item.children.length > 0) {
        const found = findItemById(item.children, id)
        if (found) return found
      }
    }
    return null
  }
  
  const parent = findItemById(rawData, parentId)
  return parent?.children?.length || 0
}

// Helper functions for depth-based styling
const getDepthColor = (depth: number) => {
  const colors = {
    1: 'rgba(66, 66, 66, 0.9)', // Orange for children
    2: '#66bb6a', // Light Green for grandchildren  
    3: '#42a5f5', // Light Blue for great-grandchildren
    4: '#ab47bc', // Purple for depth 4
    5: '#ec407a'  // Pink for depth 5
  }
  return colors[depth as keyof typeof colors] || colors[1]
}


const closeCrudTimeTickerDialog = () => {
  crudTimeTickerDialog.close()
}

const hasStatus = (statusItem: string | number, statusEnum: Status) => {
  return statusItem == Status[statusEnum]
}

const pushRequestMatchType = (matchType: number) => {
  requestMatchType.value.set(tickerRequestDialog.propData.id, matchType)
}

const getRequestMatchType = computed(() => {
  return Array.from(requestMatchType.value.entries()).map((item, index) => {
    if (item[1] == 0)
      return { id: item[0], icon: 'mdi-delete-alert', color: '#212121', class: 'grey-badge' }
    else if (item[1] == 1)
      return { id: item[0], icon: 'mdi-check-decagram', color: '#212121', class: 'blue-badge' }
    else if (item[1] == 2)
      return { id: item[0], icon: 'mdi-alert-decagram', color: '#212121', class: 'red-badge' }
  })
})

const seriesColors: { [key: string]: string } = {
  Idle: '#A9A9A9', // Dark Gray
  Queued: '#00CED1', // Dark Turquoise
  InProgress: '#6495ED', // Royal Blue
  Done: '#32CD32', // Lime Green
  DueDone: '#008000', // Green
  Failed: '#FF0000', // Red
  Cancelled: '#FFD700', // Gold/Yellow
}

// Helper functions for status styling
const getStatusColor = (status: string) => {
  switch (status) {
    case 'Done':
    case 'DueDone':
      return 'success'
    case 'InProgress':
      return 'primary'
    case 'Queued':
      return 'info'
    case 'Failed':
      return 'error'
    case 'Cancelled':
      return 'warning'
    default:
      return 'grey'
  }
}

const getStatusVariant = (status: string) => {
  switch (status) {
    case 'Done':
    case 'DueDone':
    case 'InProgress':
      return 'tonal'
    case 'Failed':
      return 'elevated'
    default:
      return 'outlined'
  }
}

const refreshData = async () => {
  await getTimeTickers.requestAsync()
}

const getRetryIntervalsArray = (retryIntervals: string[] | string | null): string[] => {
  if (!retryIntervals) return []
  if (Array.isArray(retryIntervals)) return retryIntervals
  if (typeof retryIntervals === 'string') return [retryIntervals]
  return []
}

const getDisplayIntervals = (item: any) => {
  const intervals = getRetryIntervalsArray(item.retryIntervals)
  if (intervals.length <= 3) {
    // Show all if 3 or fewer
    return intervals.map((value, index) => ({ value, originalIndex: index }))
  }

  const currentRetryIndex = (item.retryCount || 1) - 1
  const isInProgress = item.status === Status[Status.InProgress]

  if (currentRetryIndex < 2) {
    // Current is in first 2, show first 3
    return intervals.slice(0, 3).map((value, index) => ({ value, originalIndex: index }))
  } else if (isInProgress && currentRetryIndex < intervals.length) {
    // Current is active and beyond first 2, show last 2 previous + current
    const prevIndex = currentRetryIndex - 1
    const prevPrevIndex = currentRetryIndex - 2
    return [
      { value: intervals[prevPrevIndex], originalIndex: prevPrevIndex },
      { value: intervals[prevIndex], originalIndex: prevIndex },
      { value: intervals[currentRetryIndex], originalIndex: currentRetryIndex },
    ]
  } else {
    // Default: show first 3
    return intervals.slice(0, 3).map((value, index) => ({ value, originalIndex: index }))
  }
}

const getHiddenCount = (item: any) => {
  const intervals = getRetryIntervalsArray(item.retryIntervals)
  if (intervals.length <= 3) return 0

  const currentRetryIndex = (item.retryCount || 1) - 1
  const isInProgress = item.status === Status[Status.InProgress]

  if (currentRetryIndex >= 2 && isInProgress && currentRetryIndex < intervals.length) {
    // We're showing: [currentRetryIndex-2], [currentRetryIndex-1], [currentRetryIndex]
    // Show count of remaining intervals after the current one
    return intervals.length - currentRetryIndex - 1
  } else {
    // Showing first 3 normally
    return intervals.length - 3
  }
}

const getRetryStatus = (index: number, item: any) => {
  if (!item.retryCount) return 'pending'

  const currentRetryIndex = item.retryCount - 1
  const isInProgress = item.status === Status[Status.InProgress]

  if (index < currentRetryIndex) {
    return 'completed'
  } else if (index === currentRetryIndex && isInProgress) {
    return 'active'
  } else if (index === currentRetryIndex) {
    return 'completed'
  } else {
    return 'pending'
  }
}

const requestCancel = async (id: string) => {
  await requestCancelTicker.requestAsync(id)
}

const onSubmitConfirmDialog = async () => {
  confirmDialog.close()
  await deleteTimeTicker.requestAsync(confirmDialog.propData?.id!)
}

const setRowProp = (propContext: any) => {
  const baseStyle = `color:${seriesColors[propContext.item.status]}`
  let classes = []

  if (selectedItems.value.has(propContext.item.id)) {
    classes.push('selected-row')
  }

  if (propContext.item.isChild) {
    const depth = propContext.item.depth || 1
    classes.push(`child-row depth-${depth}`)
    
    
    // Calculate indentation based on depth
    const leftPadding = 20 + (depth * 20) // 40px for depth 1, 60px for depth 2, etc.
    
    // Get depth-specific border color
    const depthColors = {
      1: 'rgba(66, 66, 66, 0.9)', // Dark gray for children
      2: 'rgba(66, 66, 66, 0.9)', // Same dark gray for grandchildren  
      3: 'rgba(66, 66, 66, 0.9)', // Same dark gray for great-grandchildren
      4: 'rgba(66, 66, 66, 0.9)', // Same dark gray for depth 4
      5: 'rgba(66, 66, 66, 0.9)'  // Same dark gray for depth 5
    }
    const borderColor = depthColors[depth as keyof typeof depthColors] || depthColors[1]
    
    // Get depth-specific border width
    const depthBorderWidths = {
      1: '8px', // 4px for children
      2: '16px', // 8px for grandchildren
      3: '4px', // 4px for great-grandchildren
      4: '4px', // 4px for depth 4
      5: '4px'  // 4px for depth 5
    }
    const borderWidth = depthBorderWidths[depth as keyof typeof depthBorderWidths] || depthBorderWidths[1]
    
    return {
      style: `${baseStyle}; padding-left: ${leftPadding}px; --child-border-color: ${borderColor}; --child-border-width: ${borderWidth}; margin-right: 8px;`,
      class: classes.join(' '),
    }
  } else if (propContext.item.isParent) {
    classes.push('parent-row')
    return {
      style: `${baseStyle}; font-weight: 500;`,
      class: classes.join(' '),
    }
  } else if (propContext.item.isOrphan) {
    classes.push('orphan-row')
    return {
      style: `${baseStyle}; font-style: italic; opacity: 0.8;`,
      class: classes.join(' '),
    }
  }

  return {
    style: baseStyle,
    class: classes.join(' '),
  }
}

const canBeForceDeleted = ref<string[]>([]);

</script>

<template>
  <div class="time-ticker-dashboard">
    <!-- Content Section -->
    <div class="dashboard-content">
      <!-- Analytics Section -->
      <div class="content-card analytics-overview">
      </div>

      <!-- Operations Table Section -->
      <div class="table-section">
        <div class="section-header">
          <h2 class="section-title">
            <v-icon class="section-icon" color="primary">mdi-table</v-icon>
            Time Ticker Operations
          </h2>
          <div class="table-controls">
            <!-- Primary Actions Group -->
            <div class="primary-actions">
              <div class="action-group">
                <button
                  class="premium-action-btn primary-action"
                  @click="
                    crudTimeTickerDialog.open({
                      ...({} as GetTimeTickerResponse),
                      isFromDuplicate: true,
                    })
                  "
                >
                  <div class="btn-icon">
                    <v-icon size="18">mdi-plus</v-icon>
                  </div>
                  <span class="btn-text">Add Ticker</span>
                  <div class="btn-shine"></div>
                </button>


                <button
                  class="premium-action-btn tertiary-action"
                  @click="openChainJobsModal()"
                >
                  <div class="btn-icon">
                    <v-icon size="18">mdi-family-tree</v-icon>
                  </div>
                  <span class="btn-text">Chain Jobs</span>
                  <div class="btn-shine"></div>
                </button>
              </div>
            </div>

            <!-- Search and Info Group -->
            <div class="search-info-group">
              <v-text-field
                v-model="tableSearch"
                prepend-inner-icon="mdi-magnify"
                label="Search tickers..."
                variant="outlined"
                density="compact"
                hide-details
                style="width: 200px"
                class="search-field"
              ></v-text-field>

              <v-chip
                :color="getTimeTickers.loader.value ? 'warning' : 'success'"
                variant="tonal"
                size="small"
                class="status-chip"
              >
                <v-icon size="small" class="mr-1">
                  {{ getTimeTickers.loader.value ? 'mdi-loading' : 'mdi-check' }}
                </v-icon>
                {{
                  getTimeTickers.loader.value ? 'Loading...' : `${processedTableData.length} items`
                }}
              </v-chip>
            </div>

            <!-- Utility Actions Group -->
            <div class="utility-actions">
              <v-btn
                icon
                size="small"
                variant="text"
                color="primary"
                @click="refreshData"
                class="refresh-btn utility-btn"
              >
                <v-icon>mdi-refresh</v-icon>
              </v-btn>

              <v-btn
                v-if="selectedItems.size > 0"
                size="small"
                color="grey"
                variant="text"
                prepend-icon="mdi-close"
                class="clear-btn utility-btn"
                @click="clearSelection"
              >
                Clear
              </v-btn>
            </div>
          </div>
        </div>

        <div class="table-container">
          <v-data-table
            density="compact"
            :row-props="setRowProp"
            :headers="headersWithSelection"
            :loading="getTimeTickers.loader.value"
            :items="processedTableData"
            item-value="Id"
            :items-per-page="20"
            class="enhanced-table dense-table"
            :search="tableSearch"
            :item-height="32"
          >
          
            <!-- Selection Column -->
            <template v-slot:item.selection="{ item }">
              <v-checkbox
                v-if="!item.isChild"
                :model-value="selectedItems.has(item.id)"
                @update:model-value="toggleItemSelection(item.id)"
                color="primary"
                density="compact"
                hide-details
                class="selection-checkbox"
              />
            </template>

            <template v-slot:item.function="{ item }">
              <div class="d-flex align-center">
                <!-- Expand button for any item that has children (parents or children with grandchildren) -->
                <v-btn
                  v-if="item.isParent && getChildrenCount(item.id) > 0"
                  :icon="isParentExpanded(item.id) ? 'mdi-chevron-down' : 'mdi-chevron-right'"
                  size="small"
                  variant="text"
                  @click="toggleParentExpansion(item.id)"
                  class="mr-2 expansion-button"
                  color="primary"
                >
                </v-btn>

                <!-- Function Name with Hierarchy Indicators -->
                <div class="d-flex align-center">

                  <span class="function-name">{{ item.function }}</span>

                  <!-- Child Count Badge for any item that has children -->
                  <v-chip
                    v-if="item.isParent && getChildrenCount(item.id) > 0"
                    size="x-small"
                    :color="item.isChild ? getDepthColor(item.depth || 1) : 'primary'"
                    variant="tonal"
                    class="ml-1"
                  >
                    {{ getChildrenCount(item.id) }}
                  </v-chip>
                </div>
              </div>
            </template>

            <template v-slot:item.status="{ item }">
              <div class="d-flex align-center">
                <v-chip
                  :color="getStatusColor(item.status)"
                  :variant="getStatusVariant(item.status)"
                  size="x-small"
                  class="status-chip"
                  @click="
                    hasStatus(item.status, Status.Failed)
                      ? exceptionDialog.open({
                          ...new ConfirmDialogProps(),
                          title: 'Exception Details',
                          text: item.exception!,
                          showConfirm: false,
                          maxWidth: '900',
                          icon: 'mdi-bug-outline',
                          isException: true,
                        })
                      : null
                  "
                >
                  <v-icon size="x-small" class="mr-1" v-if="hasStatus(item.status, Status.Failed)">
                    mdi-bug-outline
                  </v-icon>
                  <span class="font-weight-medium text-caption">{{ item.status }}</span>
                </v-chip>
              </div>
            </template>
            <template v-slot:item.RequestType="{ item }">
              <v-badge
                v-bind="
                  getRequestMatchType.find((y) => y!.id == item.id) ?? {
                    icon: 'mdi-cursor-default-click-outline',
                    color: '#212121',
                    style: '{color: #212121}',
                  }
                "
                class="custom-icon"
              >
                <p
                  class="blue-underline mr-2 text-caption"
                  @click="tickerRequestDialog.open({ id: item.id })"
                >
                  {{ item.requestType }}
                </p>
              </v-badge>
            </template>

            <template v-slot:item.ExecutedAt="{ item }">
              <div
                v-if="hasStatus(item.status, Status.InProgress)"
                class="snippet"
                data-title="dot-carousel"
              >
                <div class="stage">
                  <div class="dot-carousel"></div>
                </div>
              </div>
              <div v-else class="text-caption">
                {{
                  hasStatus(item.status, Status.Cancelled) || hasStatus(item.status, Status.Queued)
                    ? ''
                    : item.executedAt
                }}
              </div>
            </template>

            <template v-slot:item.retryIntervals="{ item }">
              <div class="retry-display" v-if="getRetryIntervalsArray(item.retryIntervals)?.length">
                <span  class="retry-sequence">
                  <template v-for="(interval, index) in getDisplayIntervals(item)" :key="index">
                    <span class="retry-item" :class="getRetryStatus(interval.originalIndex, item)"
                      >{{ interval.originalIndex + 1 }}:{{ interval.value }}</span
                    >
                  </template>
                  <span v-if="getHiddenCount(item) > 0" class="retry-more">
                    +{{ getHiddenCount(item) }}
                  </span>
                </span>
              </div>
            </template>

            <template v-slot:item.lockHolder="{ item }">
              <span class="text-caption">{{ item.lockHolder }}</span>
              <v-tooltip v-if="item.lockHolder != ''" activator="parent" location="left">
                <span>Locked At: {{ formatDate(item.lockedAt) }}</span>
              </v-tooltip>
            </template>

            <template v-slot:item.actions="{ item }">
              <div class="action-buttons-container">
                <!-- Cancel Button -->
                <div
                  class="action-btn-wrapper"
                  :class="{ 'action-btn-disabled': !hasStatus(item.status, Status.InProgress) }"
                >
                  <v-tooltip location="top">
                    <template v-slot:activator="{ props }">
                      <button
                        v-bind="props"
                        @click="requestCancel(item.id).catch(() => {
                          canBeForceDeleted.push(item.id)
                        })"
                        :disabled="!hasStatus(item.status, Status.InProgress) || canBeForceDeleted.includes(item.id)"
                        class="modern-action-btn cancel-btn"
                        :class="{ active: hasStatus(item.status, Status.InProgress) }"
                      >
                        <v-icon size="16">mdi-cancel</v-icon>
                      </button>
                    </template>
                    <span>Cancel Operation</span>
                  </v-tooltip>
                </div>

                <!-- Edit/Duplicate Button -->
                <div class="action-btn-wrapper">
                  <v-tooltip location="top">
                    <template v-slot:activator="{ props }">
                      <button
                        v-bind="props"
                        v-if="
                          hasStatus(item.status, Status.Queued) ||
                          hasStatus(item.status, Status.Idle)
                        "
                        @click="
                          crudTimeTickerDialog.open({
                            ...item,
                            executionTime: formatFromUtcToLocal(item.executionTime),
                            isFromDuplicate: false,
                          })
                        "
                        class="modern-action-btn edit-btn"
                      >
                        <v-icon size="16">mdi-pencil</v-icon>
                      </button>
                      <button
                        v-else
                        v-bind="props"
                        @click="
                          crudTimeTickerDialog.open({
                            ...item,
                            executionTime: formatFromUtcToLocal(item.executionTime),
                            isFromDuplicate: true,
                          })
                        "
                        class="modern-action-btn duplicate-btn"
                      >
                        <v-icon size="16">mdi-content-copy</v-icon>
                      </button>
                    </template>
                    <span>{{
                      hasStatus(item.status, Status.Queued) || hasStatus(item.status, Status.Idle)
                        ? 'Edit'
                        : 'Duplicate'
                    }}</span>
                  </v-tooltip>
                </div>

                <!-- Delete Button -->
                <div
                  class="action-btn-wrapper"
                  :class="{ 'action-btn-disabled': hasStatus(item.status, Status.InProgress) && !canBeForceDeleted.includes(item.id) }"
                >
                  <v-tooltip location="top">
                    <template v-slot:activator="{ props }">
                      <button
                        v-bind="props"
                        @click="confirmDialog.open({ id: item.id })"
                        :disabled="hasStatus(item.status, Status.InProgress) && !canBeForceDeleted.includes(item.id)"
                        class="modern-action-btn delete-btn"
                        :class="{ active: !hasStatus(item.status, Status.InProgress) || canBeForceDeleted.includes(item.id) }"
                      >
                        <v-icon size="16">mdi-trash-can</v-icon>
                      </button>
                    </template>
                    <span>{{ canBeForceDeleted.includes(item.id) ? 'Force Delete Ticker' : 'Delete Ticker' }}</span>
                  </v-tooltip>
                </div>
              </div>
            </template>
          </v-data-table>
        </div>
      </div>
    </div>

    <confirmDialog.Component
      :is-open="confirmDialog.isOpen"
      @close="confirmDialog.close()"
      @confirm="onSubmitConfirmDialog"
    />

    <tickerRequestDialog.Component
      @push-match-type="pushRequestMatchType"
      :dialog-props="tickerRequestDialog.propData"
      :is-open="tickerRequestDialog.isOpen"
      @close="tickerRequestDialog.close()"
    />

    <crudTimeTickerDialog.Component
      ref="crudTimeTickerDialogRef"
      :dialog-props="crudTimeTickerDialog.propData"
      :is-open="crudTimeTickerDialog.isOpen"
      @close="closeCrudTimeTickerDialog"
      @confirm="closeCrudTimeTickerDialog"
    />

    <exceptionDialog.Component
      :is-open="exceptionDialog.isOpen"
      @close="exceptionDialog.close()"
      :dialog-props="exceptionDialog.propData"
    />

    <!-- Chain Jobs Modal -->
    <ChainJobsModal
      v-model="chainJobsModal.isOpen"
      @created="onChainJobsCreated"
    />
  </div>
</template>

<style scoped>
/* Dashboard Layout */
.time-ticker-dashboard {
  background: linear-gradient(135deg, #212121 0%, #2d2d2d 100%);
  min-height: 100vh;
  font-family:
    'Inter',
    -apple-system,
    BlinkMacSystemFont,
    sans-serif;
  position: relative;
  color: #e0e0e0;
}

/* Content Section */
.dashboard-content {
  max-width: 1400px;
  margin: 0 auto;
  padding: 32px 24px 16px 24px;
}


/* Table Section */
.table-section {
  background: rgba(66, 66, 66, 0.9);
  backdrop-filter: blur(20px);
  border-radius: 12px;
  padding: 20px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.4);
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 20px;
  flex-wrap: wrap;
  gap: 16px;
}

.section-title {
  font-size: 1.125rem;
  font-weight: 700;
  color: #e0e0e0;
  margin: 0;
  display: flex;
  align-items: center;
  gap: 8px;
}

.section-icon {
  background: rgba(100, 181, 246, 0.2);
  border-radius: 6px;
  padding: 6px;
}

.table-controls {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  flex-wrap: wrap;
  width: 100%;
}

/* Premium Action Buttons */
.primary-actions {
  display: flex;
  align-items: center;
}

.action-group {
  display: flex;
  align-items: center;
  gap: 8px;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 16px;
  padding: 4px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  backdrop-filter: blur(20px);
}

.premium-action-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  border: none;
  border-radius: 12px;
  cursor: pointer;
  font-weight: 600;
  font-size: 0.875rem;
  letter-spacing: 0.4px;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  position: relative;
  overflow: hidden;
  min-height: 40px;
}

.premium-action-btn .btn-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: 6px;
  transition: all 0.3s ease;
}

.premium-action-btn .btn-text {
  font-weight: 600;
  letter-spacing: 0.3px;
  white-space: nowrap;
}

.premium-action-btn .btn-shine {
  position: absolute;
  top: 0;
  left: -100%;
  width: 100%;
  height: 100%;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.2), transparent);
  transition: left 0.5s ease;
}

.premium-action-btn:hover .btn-shine {
  left: 100%;
}

.primary-action {
  background: linear-gradient(135deg, #64b5f6 0%, #1976d2 100%);
  color: #ffffff;
  box-shadow: 0 4px 20px rgba(100, 181, 246, 0.4);
}

.primary-action:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 30px rgba(100, 181, 246, 0.6);
  background: linear-gradient(135deg, #90caf9 0%, #1565c0 100%);
}

.primary-action .btn-icon {
  background: rgba(255, 255, 255, 0.2);
}

.secondary-action {
  background: linear-gradient(135deg, rgba(255, 255, 255, 0.1) 0%, rgba(255, 255, 255, 0.05) 100%);
  color: #e0e0e0;
  border: 1px solid rgba(255, 255, 255, 0.15);
}

.secondary-action:hover {
  transform: translateY(-2px);
  background: linear-gradient(135deg, rgba(255, 255, 255, 0.15) 0%, rgba(255, 255, 255, 0.08) 100%);
  border-color: rgba(255, 255, 255, 0.25);
  box-shadow: 0 8px 25px rgba(0, 0, 0, 0.3);
}

.secondary-action .btn-icon {
  background: rgba(100, 181, 246, 0.2);
  color: #64b5f6;
}

.tertiary-action {
  background: linear-gradient(135deg, rgba(156, 39, 176, 0.1) 0%, rgba(156, 39, 176, 0.05) 100%);
  color: #e0e0e0;
  border: 1px solid rgba(156, 39, 176, 0.15);
}

.tertiary-action:hover {
  transform: translateY(-2px);
  background: linear-gradient(135deg, rgba(156, 39, 176, 0.15) 0%, rgba(156, 39, 176, 0.08) 100%);
  border-color: rgba(156, 39, 176, 0.25);
  box-shadow: 0 8px 25px rgba(156, 39, 176, 0.2);
}

.tertiary-action .btn-icon {
  background: rgba(156, 39, 176, 0.2);
  color: #ab47bc;
}

/* Search and Info Group */
.search-info-group {
  display: flex;
  align-items: center;
  gap: 12px;
  flex: 1;
  justify-content: center;
}

.status-chip {
  backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.1);
}

/* Utility Actions */
.utility-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.utility-btn {
  backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.05);
  transition: all 0.2s ease;
}

.utility-btn:hover {
  background: rgba(255, 255, 255, 0.08);
  border-color: rgba(255, 255, 255, 0.15);
}

.action-btn {
  font-weight: 600;
  text-transform: none;
  letter-spacing: 0.5px;
  border-radius: 12px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  transition: all 0.3s ease;
}

/* Modern Action Buttons */
.action-buttons-container {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 2px;
}

.action-btn-wrapper {
  position: relative;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}

.action-btn-wrapper.action-btn-disabled {
  opacity: 0.4;
  pointer-events: none;
}

.modern-action-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border: none;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.05);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  cursor: pointer;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  position: relative;
  overflow: hidden;
}

.modern-action-btn::before {
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: linear-gradient(135deg, rgba(255, 255, 255, 0.1), rgba(255, 255, 255, 0.05));
  opacity: 0;
  transition: opacity 0.3s ease;
}

.modern-action-btn:hover::before {
  opacity: 1;
}

.modern-action-btn:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 25px rgba(0, 0, 0, 0.3);
  border-color: rgba(255, 255, 255, 0.2);
}

.modern-action-btn:active {
  transform: translateY(0);
  transition: transform 0.1s ease;
}

.modern-action-btn:disabled {
  cursor: not-allowed;
  opacity: 0.5;
  transform: none !important;
  box-shadow: none !important;
}

/* Button-specific colors */
.cancel-btn {
  color: rgba(66, 66, 66, 0.9);
  border-color: rgba(255, 183, 77, 0.2);
}

.cancel-btn.active {
  background: rgba(255, 183, 77, 0.15);
  color: #ff9800;
  border-color: rgba(255, 152, 0, 0.4);
  box-shadow: 0 0 20px rgba(255, 152, 0, 0.3);
}

.cancel-btn:hover {
  border-color: rgba(255, 183, 77, 0.5);
  box-shadow: 0 8px 25px rgba(255, 183, 77, 0.4);
}

.unbatch-btn {
  color: #f44336;
  border-color: rgba(244, 67, 54, 0.2);
  background: rgba(244, 67, 54, 0.1);
}

.unbatch-btn:hover {
  border-color: rgba(244, 67, 54, 0.5);
  box-shadow: 0 8px 25px rgba(244, 67, 54, 0.4);
  background: rgba(244, 67, 54, 0.15);
}

.unbatch-btn.loading {
  opacity: 0.7;
  pointer-events: none;
}

.unbatch-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.edit-btn {
  color: #64b5f6;
  border-color: rgba(100, 181, 246, 0.2);
}

.edit-btn:hover {
  border-color: rgba(100, 181, 246, 0.5);
  box-shadow: 0 8px 25px rgba(100, 181, 246, 0.4);
  background: rgba(100, 181, 246, 0.15);
}

.duplicate-btn {
  color: #81c784;
  border-color: rgba(129, 199, 132, 0.2);
}

.duplicate-btn:hover {
  border-color: rgba(129, 199, 132, 0.5);
  box-shadow: 0 8px 25px rgba(129, 199, 132, 0.4);
  background: rgba(129, 199, 132, 0.15);
}

.delete-btn {
  color: #e57373;
  border-color: rgba(229, 115, 115, 0.2);
}

.delete-btn.active {
  background: rgba(229, 115, 115, 0.15);
  color: #f44336;
  border-color: rgba(244, 67, 54, 0.4);
}

.delete-btn:hover {
  border-color: rgba(229, 115, 115, 0.5);
  background: rgba(229, 115, 115, 0.15);
}

.expansion-button {
  transition: all 0.2s ease;
  border-radius: 6px;
}

.search-field {
  background: rgba(255, 255, 255, 0.05);
  border-radius: 8px;
}

.search-field :deep(.v-field__outline) {
  border-color: rgba(255, 255, 255, 0.2);
}

.search-field :deep(.v-field--focused .v-field__outline) {
  border-color: #64b5f6;
}

.refresh-btn {
  color: #64b5f6;
}

.batch-btn,
.unbatch-btn,
.create-batch-btn {
  font-weight: 600;
  text-transform: none;
  letter-spacing: 0.5px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
  transition: all 0.3s ease;
}

.create-batch-btn {
  border: 2px solid rgba(100, 181, 246, 0.5);
}

.create-batch-btn:hover {
  border-color: rgba(100, 181, 246, 0.8);
  background: rgba(100, 181, 246, 0.1);
}

.clear-btn {
  color: #9e9e9e;
  transition: all 0.2s ease;
}

.table-container {
  background: linear-gradient(135deg, rgba(0, 0, 0, 0.25) 0%, rgba(0, 0, 0, 0.15) 100%);
  border-radius: 12px;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.15) !important;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.3);
}

/* Enhanced table styles */
.enhanced-table {
  border-radius: 8px;
  overflow: hidden;
  background: transparent;
}

/* Dense table styling */
.dense-table {
  font-size: 0.875rem;
}

:deep(.enhanced-table .v-data-table__td) {
  padding: 4px 6px !important;
  font-size: 0.875rem;
  line-height: 1.2;
  min-height: 32px;
  height: 32px;
}

:deep(.enhanced-table .v-data-table-header__td) {
  padding: 6px 6px !important;
  font-size: 0.75rem;
  font-weight: 600;
  min-height: 36px;
  height: 36px;
}

:deep(.enhanced-table .v-data-table__wrapper) {
  border-radius: 12px;
  background: transparent;
  border-left: none !important;
  border-right: none !important;
  border: none !important;
}

/* Allow main table borders to show naturally */

:deep(.enhanced-table .v-data-table-header) {
  background: linear-gradient(135deg, rgba(100, 181, 246, 0.15) 0%, rgba(100, 181, 246, 0.05) 100%);
  border-bottom: 2px solid rgba(100, 181, 246, 0.3);
  font-weight: 600;
  font-size: 0.75rem;
  color: #ffffff;
  text-transform: uppercase;
  letter-spacing: 0.8px;
  box-shadow: 0 2px 8px rgba(100, 181, 246, 0.1);
}

:deep(.enhanced-table .v-data-table__tr) {
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
  position: relative;
  min-height: 32px;
  height: 32px;
}

:deep(.dense-table .v-data-table__tr) {
  min-height: 32px !important;
  height: 32px !important;
}


:deep(.enhanced-table .v-data-table__tr::after) {
  content: '';
  position: absolute;
  bottom: 0;
  left: 40px; /* Start after the selection column */
  right: 0;
  height: 1px;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.08), transparent);
}

/* Removed - replaced with uniform #353535 background */

/* Status chip styles - Dense */
.status-chip {
  transition: all 0.2s ease;
  cursor: pointer;
  font-weight: 600;
  letter-spacing: 0.3px;
  font-size: 0.75rem !important;
  height: 20px !important;
  min-height: 20px !important;
  padding: 0 6px !important;
}

:deep(.status-chip .v-chip__content) {
  font-size: 0.75rem !important;
  line-height: 1.2;
  padding: 0 !important;
}

/* Dense expansion button */
.expansion-button {
  min-width: 24px !important;
  width: 24px !important;
  height: 24px !important;
}

/* Dense function name styling */
.function-name {
  font-size: 0.875rem;
  line-height: 1.2;
}

/* Dense child count badge */
:deep(.v-chip.v-chip--size-x-small) {
  height: 16px !important;
  min-height: 16px !important;
  font-size: 0.65rem !important;
  padding: 0 4px !important;
}

/* Selection styles */
.selection-checkbox {
  margin: 0;
}


/* Remove ::after for selection column specifically */
:deep(.enhanced-table .v-data-table__td:first-child::after) {
  display: none !important;
}

:deep(.enhanced-table .v-data-table__tr .v-data-table__td:first-child::after) {
  content: none !important;
  display: none !important;
}

:deep(.selection-checkbox .v-selection-control) {
  margin: 0;
}

:deep(.selection-checkbox .v-selection-control__input) {
  margin: 0;
}

.selected-row {
  background: linear-gradient(
    135deg,
    rgba(100, 181, 246, 0.1) 0%,
    rgba(100, 181, 246, 0.05) 100%
  ) !important;
  border-left: 3px solid #64b5f6;
}

/* Row hierarchy styles */
/* Let parent rows use original DataTable styling */

.child-row {
  /* Border colors and backgrounds are now handled by inline styles */
  position: relative;
  margin-right: 8px !important;
  padding-right: 20px !important;
}

/* Depth-specific styling for nested children - now handled by inline styles */

/* All depth-specific styling is now handled by inline styles in setRowProp */

/* More specific selector for Vuetify table rows - Default child styling */
:deep(.enhanced-table .v-data-table__tr.child-row) {
  /* Border colors are now handled by inline styles */
  padding-left: 40px !important;
  padding-right: 20px !important;
  margin-right: 8px !important;
}

/* Let DataTable use original styling */

/* Child row styling using CSS custom properties set by inline styles */
:deep(.enhanced-table .v-data-table__tr.child-row) {
  border: none !important;
}

/* Remove default table borders for child rows */
:deep(.enhanced-table .child-row .v-data-table__td) {
  border-bottom: none !important;
  border-top: none !important;
  border-radius: 0 !important;
}

:deep(.enhanced-table .child-row .v-data-table__td:first-child) {
  border-left: var(--child-border-width, 4px) solid var(--child-border-color, rgba(66, 66, 66, 0.9)) !important;
  padding-left: 44px !important; /* Increased to account for border */
  border-radius: 0 !important;
}

:deep(.enhanced-table .child-row .v-data-table__td:last-child) {
  border-right: var(--child-border-width, 4px) solid var(--child-border-color, rgba(66, 66, 66, 0.9)) !important;
  padding-right: 24px !important; /* Increased to account for border */
  border-radius: 0 !important;
}

/* Target the first and last table cells in child rows for padding */
:deep(.enhanced-table .child-row .v-data-table__td:first-child) {
  padding-left: 40px !important;
}

:deep(.enhanced-table .child-row .v-data-table__td:last-child) {
  padding-right: 20px !important;
}

/* Add margin to the entire child row */
:deep(.enhanced-table .child-row) {
  margin-right: 8px !important;
}

/* Alternative - target all cells in child rows */
:deep(.child-row td) {
  background: rgba(255, 183, 77, 0.02) !important;
}

:deep(.child-row td:first-child) {
  padding-left: 40px !important;
}

:deep(.child-row td:last-child) {
  padding-right: 20px !important;
}

/* Most specific approach - target the table wrapper */
:deep(.v-table .child-row) {
  margin-right: 8px !important;
}

:deep(.v-table .child-row td) {
  background: rgba(255, 183, 77, 0.02) !important;
}

:deep(.v-table .child-row td:first-child) {
  padding-left: 40px !important;
  /* Border color is now handled by inline styles */
}

:deep(.v-table .child-row td:last-child) {
  padding-right: 20px !important;
  /* Border color is now handled by inline styles */
}

/* Try with tbody as well */
:deep(tbody .child-row td:first-child) {
  padding-left: 40px !important;
}

:deep(tbody .child-row td:last-child) {
  padding-right: 20px !important;
}

/* Dense child row styling */
:deep(.enhanced-table .child-row .v-data-table__td) {
  padding: 4px 6px !important;
  font-size: 0.85rem;
  min-height: 32px;
  height: 32px;
}

:deep(.enhanced-table .child-row .v-data-table__td:first-child) {
  padding: 4px 6px 4px 30px !important;
}

:deep(.enhanced-table .child-row .v-data-table__td:last-child) {
  padding: 4px 16px 4px 6px !important;
}

.child-row::before {
  content: '';
  position: absolute;
  left: -4px;
  top: 0;
  bottom: 0;
  width: 2px;
  background: linear-gradient(to bottom, rgba(255, 183, 77, 0.5), rgba(255, 183, 77, 0.2));
}

.child-row::after {
  content: '';
  position: absolute;
  right: 0;
  top: 0;
  bottom: 0;
  width: 2px;
  background: linear-gradient(to bottom, rgba(255, 183, 77, 0.3), rgba(255, 183, 77, 0.1));
}

.orphan-row {
  border-left: 4px solid #ff5722;
  background: linear-gradient(135deg, rgba(255, 87, 34, 0.05) 0%, rgba(255, 87, 34, 0.02) 100%);
}

/* Child Jobs Dropdown */
.child-dropdown-btn {
  font-size: 0.7rem;
  min-width: auto;
  padding: 2px 6px;
  height: 24px;
  border-radius: 12px;
  text-transform: none;
  letter-spacing: 0.3px;
}

.child-jobs-dropdown {
  background: rgba(42, 42, 42, 0.98);
  backdrop-filter: blur(20px);
  border-radius: 8px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
  border: 1px solid rgba(255, 255, 255, 0.1);
  min-width: 700px;
  max-width: 800px;
}

.child-dropdown-header {
  padding: 12px 16px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  background: linear-gradient(135deg, rgba(100, 181, 246, 0.15) 0%, rgba(100, 181, 246, 0.05) 100%);
}

.child-dropdown-title {
  font-weight: 600;
  font-size: 0.9rem;
  color: #ffffff;
  letter-spacing: 0.3px;
}

.child-jobs-table {
  padding: 0;
}

.child-table-row {
  display: flex;
  padding: 8px 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
  transition: background-color 0.2s ease;
  align-items: center;
}

.child-table-row:hover {
  background-color: rgba(100, 181, 246, 0.08);
}

.child-table-row:last-child {
  border-bottom: none;
}

.child-col {
  display: flex;
  align-items: center;
  font-size: 0.8rem;
}

.child-col-function {
  flex: 2;
  min-width: 200px;
}

.child-col-status {
  flex: 1;
  min-width: 100px;
  justify-content: center;
}

.child-col-execution {
  flex: 1.5;
  min-width: 140px;
}

.child-col-executed {
  flex: 1.5;
  min-width: 140px;
}

.child-col-actions {
  flex: 0.5;
  min-width: 60px;
  justify-content: center;
}

.child-function-name {
  font-weight: 600;
  color: #e0e0e0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.child-execution-time,
.child-executed-at {
  font-size: 0.75rem;
  color: #bdbdbd;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.child-action-btn {
  opacity: 0.7;
  transition: opacity 0.2s ease;
}

.child-table-row:hover .child-action-btn {
  opacity: 1;
}

/* Utility classes */
.blue-underline {
  cursor: pointer;
  text-decoration: underline;
  transition: all 0.2s ease;
}

:deep(.blue-badge .v-badge__badge) {
  color: rgb(0, 145, 255) !important;
}

:deep(.red-badge .v-badge__badge) {
  color: red !important;
}

:deep(.grey-badge .v-badge__badge) {
  color: grey !important;
}

.function-name {
  font-weight: 600;
  color: #e0e0e0;
}

/* Clean Retry Intervals Display */
.retry-display {
  font-family: 'JetBrains Mono', 'Monaco', 'Consolas', monospace;
  font-size: 0.75rem;
  line-height: 1.2;
}

.no-retries {
  content:"—"; color:#6b7280; font-style:italic; display:inline-block;
}

.retry-sequence {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: nowrap;
}

.retry-item {
  font-weight: 500;
  transition: color 0.2s ease;
  white-space: nowrap;
}

.retry-item.completed {
  color: #4caf50;
}

.retry-item.active {
  color: #ff9800;
  font-weight: 600;
  animation: retryPulse 1.5s ease-in-out infinite;
  position: relative;
  background: rgba(255, 152, 0, 0.1);
  padding: 2px 4px;
  border-radius: 4px;
  border: 1px solid rgba(255, 152, 0, 0.3);
}

@keyframes retryPulse {
  0%,
  100% {
    opacity: 1;
    transform: scale(1);
    box-shadow: 0 0 0 0 rgba(255, 152, 0, 0.4);
  }
  50% {
    opacity: 0.8;
    transform: scale(1.05);
    box-shadow: 0 0 0 4px rgba(255, 152, 0, 0.1);
  }
}

.retry-item.pending {
  color: #888;
}

.retry-more {
  color: #666;
  font-size: 0.7rem;
  font-style: italic;
  opacity: 0.8;
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



  .title-icon {
    padding: 8px;
    border-radius: 8px;
  }

  .metrics-grid {
    grid-template-columns: 1fr;
  }

  .section-header {
    flex-direction: column;
    align-items: stretch;
  }

  .table-controls {
    justify-content: center;
  }
}

@media (max-width: 480px) {
  .dashboard-content {
    padding: 16px 12px 12px 12px;
  }

  .metrics-grid {
    gap: 12px;
  }

  .metric-card {
    padding: 12px;
  }

  .content-card {
    padding: 16px;
  }

  .table-section {
    padding: 16px;
  }

}

::v-deep(.v-data-table) { empty-cells: show; }
::v-deep(td.v-data-table__td:empty) { position: relative; }
::v-deep(td.v-data-table__td:empty)::after { content:"—"; color:#6b7280; font-style:italic; display:inline-block; }
</style>
