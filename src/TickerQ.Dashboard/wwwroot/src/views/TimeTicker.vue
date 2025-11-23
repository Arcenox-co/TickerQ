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
import { formatDate } from '@/utilities/dateTimeParser'
import { useConnectionStore } from '@/stores/connectionStore'
import { useTimeZoneStore } from '@/stores/timeZoneStore'
import PaginationFooter from '@/components/PaginationFooter.vue'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart, PieChart } from 'echarts/charts'
import {
  TitleComponent,
  TooltipComponent,
  LegendComponent,
  ToolboxComponent,
  GridComponent,
  DataZoomComponent,
} from 'echarts/components'
import VChart, { THEME_KEY } from 'vue-echarts'
import type { GetTimeTickerGraphDataRangeResponse } from '@/http/services/types/timeTickerService.types'

use([
  CanvasRenderer,
  LineChart,
  PieChart,
  TitleComponent,
  TooltipComponent,
  LegendComponent,
  GridComponent,
  ToolboxComponent,
  DataZoomComponent
])

// Use paginated service instead of regular one
const getTimeTickersPaginated = timeTickerService.getTimeTickersPaginated()
const deleteTimeTicker = timeTickerService.deleteTimeTicker()
const deleteTimeTickersBatch = timeTickerService.deleteTimeTickersBatch()
const requestCancelTicker = tickerService.requestCancel()
const getTimeTickersGraphDataRange = timeTickerService.getTimeTickersGraphDataRange()
const getTimeTickersGraphData = timeTickerService.getTimeTickersGraphData()

// Pagination state
const currentPage = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)

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
  isOpen: false,
})

const expandedParents = ref(new Set<string>())
const tableSearch = ref('')

const selectedItems = ref(new Set<string>())

// Chart related state
const activeChart = ref<'line' | 'pie'>('line')
const chartLoading = ref(false)
const chartKey = ref(0)
const chartData = ref({
  xAxisData: [] as string[],
  series: [] as any[],
  legend: {} as any,
  title: 'Job statuses for all Time Tickers',
})
const pieChartData = ref<any[]>([])
const pieChartKey = ref(0)

// Provide theme for charts
provide(THEME_KEY, 'dark')

const timeZoneStore = useTimeZoneStore()

onMounted(async () => {
  // Initialize WebSocket connection
  try {
    const connectionStore = useConnectionStore()
    if (!connectionStore.isInitialized) {
      await connectionStore.initializeConnectionWithRetry()
    }
  } catch (error: any) {}

  // Load initial data with pagination
  try {
    await loadPageData()
  } catch (error) {
    // Failed to load time tickers
  }
  
  // Load chart data
  try {
    await loadTimeSeriesChartData(-3, 3)
    await loadPieChartData()
  } catch (error) {
    console.error('Failed to load chart data:', error)
  }
  
  // Add hub listeners
  try {
    await addHubListeners()
  } catch (error) {
    // Failed to add hub listeners
  }
})

// Reload data when display timezone changes
watch(
  () => timeZoneStore.effectiveTimeZone,
  async () => {
    try {
      await loadPageData()
      await loadTimeSeriesChartData(-3, 3)
      await loadPieChartData()
    } catch {
      // ignore errors on timezone-driven refresh
    }
  }
)

onUnmounted(() => {
  TickerNotificationHub.stopReceiver(methodName.onReceiveAddTimeTicker)
  TickerNotificationHub.stopReceiver(methodName.onReceiveUpdateTimeTicker)
  TickerNotificationHub.stopReceiver(methodName.onReceiveDeleteTimeTicker)
  TickerNotificationHub.stopReceiver(methodName.onReceiveAddTimeTickersBatch)
})

// Load page data with pagination
const loadPageData = async () => {
  try {
    const response = await getTimeTickersPaginated.requestAsync(currentPage.value, pageSize.value)
    if (response) {
      totalCount.value = response.totalCount || 0
    }
  } catch (error) {
    console.error('Failed to load paginated data:', error)
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

// Chart data loading functions
const loadTimeSeriesChartData = async (min: number, max: number) => {
  try {
    chartLoading.value = true
    const res = await getTimeTickersGraphDataRange.requestAsync(min, max)
    processTimeSeriesData(res)
  } catch (error: any) {
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
      return
    }
    console.error('Failed to load time series chart data:', error)
  } finally {
    chartLoading.value = false
  }
}

const loadPieChartData = async () => {
  try {
    const res = await getTimeTickersGraphData.requestAsync()
    processPieChartData(res)
  } catch (error: any) {
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
      return
    }
    console.error('Failed to load pie chart data:', error)
  }
}

const processTimeSeriesData = (data: GetTimeTickerGraphDataRangeResponse[]) => {
  if (!data || !Array.isArray(data)) {
    chartData.value.xAxisData = []
    chartData.value.series = []
    chartData.value.legend = { data: [] }
    return
  }

  const allStatuses = new Set<number>()
  const dateMap = new Map<string, Map<number, number>>()

  data.forEach((item) => {
    if (!item.results) return
    item.results.forEach((result: any) => {
      allStatuses.add(result.item1)
      if (!dateMap.has(item.date)) {
        dateMap.set(item.date, new Map())
      }
      dateMap.get(item.date)!.set(result.item1, result.item2)
    })
  })

  const uniqueDates = Array.from(dateMap.keys()).sort()
  const statusArray = Array.from(allStatuses).sort((a, b) => a - b)

  // Use the same statusColors mapping
  const statusColors: { [key: number]: string } = {
    0: '#A9A9A9', // Idle - Dark Gray
    1: '#00CED1', // Queued - Dark Turquoise
    2: '#6495ED', // InProgress - Royal Blue
    3: '#32CD32', // Done - Lime Green
    4: '#008000', // DueDone - Green
    5: '#FF0000', // Failed - Red
    6: '#FFD700', // Cancelled - Gold/Yellow
    7: '#BA68C8', // Skipped - Medium Orchid (Purple)
  }

  const composedData = statusArray.map((status) => ({
    name: Status[status] || `Status ${status}`,
    type: 'line',
    smooth: true,
    symbol: 'circle',
    symbolSize: 6,
    itemStyle: { color: statusColors[status] || '#808080' },
    lineStyle: { 
      width: 2,
      color: statusColors[status] || '#808080'
    },
    areaStyle: {
      color: {
        type: 'linear',
        x: 0,
        y: 0,
        x2: 0,
        y2: 1,
        colorStops: [
          { offset: 0, color: (statusColors[status] || '#808080') + '30' },
          { offset: 1, color: (statusColors[status] || '#808080') + '05' }
        ]
      }
    },
    data: uniqueDates.map((date) => {
      const statusMap = dateMap.get(date)
      return statusMap ? (statusMap.get(status) || 0) : 0
    }),
  }))

  chartData.value = {
    xAxisData: uniqueDates,
    series: composedData,
    legend: { data: composedData.map(s => s.name) },
    title: chartData.value.title
  }
  
  chartKey.value++
}

const processPieChartData = (data: any[]) => {
  if (!data || !Array.isArray(data)) {
    pieChartData.value = []
    return
  }

  const statusColors: { [key: number]: string } = {
    0: '#A9A9A9', // Idle - Dark Gray
    1: '#00CED1', // Queued - Dark Turquoise
    2: '#6495ED', // InProgress - Royal Blue
    3: '#32CD32', // Done - Lime Green
    4: '#008000', // DueDone - Green
    5: '#FF0000', // Failed - Red
    6: '#FFD700', // Cancelled - Gold/Yellow
    7: '#BA68C8', // Skipped - Medium Orchid (Purple)
  }

  const chartDataProcessed = data
    .filter(item => item && item.item1 !== undefined && item.item2 !== undefined)
    .sort((a, b) => a.item2 - b.item2)
    .map((item) => {
      const statusName = Status[item.item1] || `Status ${item.item1}`
      const color = statusColors[item.item1] || '#999999' // Default gray if status not found
      return {
        name: `${statusName} (${item.item2})`,
        value: item.item2,
        itemStyle: {
          color: color
        }
      }
    })

  // If no data, add a placeholder
  if (chartDataProcessed.length === 0) {
    chartDataProcessed.push({
      name: 'No Data Available',
      value: 1,
      itemStyle: { color: '#9e9e9e' }
    })
  }

  pieChartData.value = chartDataProcessed
  pieChartKey.value++
}

const addHubListeners = async () => {
  // Debounce utility for view refreshes
  function debounce<T extends (...args: any[]) => void>(fn: T, delay: number): T {
    let timeout: ReturnType<typeof setTimeout>
    return ((...args: any[]) => {
      clearTimeout(timeout)
      timeout = setTimeout(() => fn(...args), delay)
    }) as T
  }

  const debouncedRefresh = debounce(() => {
    loadPageData()
    loadPieChartData()
  }, 200)

  TickerNotificationHub.onReceiveAddTimeTicker<GetTimeTickerResponse>(() => {
    debouncedRefresh()
  })

  TickerNotificationHub.onReceiveUpdateTimeTicker<void>(() => {
    debouncedRefresh()
  })

  TickerNotificationHub.onReceiveDeleteTimeTicker<string>((id) => {
    // Reload current page when item is deleted
    loadPageData()
    // Update charts
    loadPieChartData()
  })

  // Batch insert notification: just refresh current view and charts once
  TickerNotificationHub.onReceiveAddTimeTickersBatch(() => {
    loadPageData()
    loadTimeSeriesChartData(-3, 3)
    loadPieChartData()
  })
}

// Chart configurations
const lineChartOption = computed(() => ({
  backgroundColor: 'transparent',
  title: {
    text: 'Time Series Analysis',
    subtext: chartData.value.title,
    left: 'center',
    top: '2%',
    textStyle: {
      color: '#e0e0e0',
      fontSize: 14,
      fontWeight: 600,
    },
    subtextStyle: {
      color: '#9e9e9e',
      fontSize: 11,
    },
  },
  tooltip: {
    trigger: 'axis',
    backgroundColor: 'rgba(42, 42, 42, 0.95)',
    borderColor: '#666',
    borderWidth: 1,
    axisPointer: {
      type: 'cross',
      label: {
        backgroundColor: 'rgba(66, 66, 66, 0.9)',
        color: '#fff',
      },
    },
  },
  legend: chartData.value.legend,
  grid: {
    left: '8%',
    right: '8%',
    bottom: '10%',
    top: '18%',
    containLabel: true,
  },
  xAxis: {
    type: 'category',
    boundaryGap: false,
    data: chartData.value.xAxisData,
    axisLabel: {
      color: '#bdbdbd',
      fontSize: 10,
      rotate: 45,
    },
    axisLine: {
      lineStyle: { color: '#424242' },
    },
  },
  yAxis: {
    type: 'value',
    axisLabel: {
      color: '#bdbdbd',
      fontSize: 10,
    },
    axisLine: {
      lineStyle: { color: '#424242' },
    },
    splitLine: {
      lineStyle: {
        color: 'rgba(255, 255, 255, 0.05)',
        type: 'dashed',
      },
    },
  },
  dataZoom: [
    {
      type: 'inside',
      start: 0,
      end: 100,
    },
    {
      show: true,
      type: 'slider',
      bottom: '2%',
      start: 0,
      end: 100,
      backgroundColor: 'rgba(47, 47, 47, 0.5)',
      borderColor: '#666',
      fillerColor: 'rgba(100, 181, 246, 0.2)',
      handleStyle: {
        color: '#64b5f6',
        borderColor: '#64b5f6',
      },
    },
  ],
  series: chartData.value.series,
  animation: true,
  animationDuration: 1000,
  animationEasing: 'cubicOut' as const,
}))

const pieChartOption = computed(() => ({
  backgroundColor: 'transparent',
  title: {
    text: 'Status Distribution',
    subtext: 'Current Overview',
    left: 'center',
    top: '2%',
    textStyle: {
      color: '#e0e0e0',
      fontSize: 14,
      fontWeight: 600,
    },
    subtextStyle: {
      color: '#9e9e9e',
      fontSize: 11,
    },
  },
  tooltip: {
    trigger: 'item',
    formatter: '{b}: {c} ({d}%)',
    backgroundColor: 'rgba(42, 42, 42, 0.95)',
    borderColor: '#666',
    borderWidth: 1,
  },
  legend: {
    orient: 'vertical',
    right: '8%',
    top: 'center',
    textStyle: {
      color: '#bdbdbd',
      fontSize: 11,
    },
  },
  series: [
    {
      name: 'Status',
      type: 'pie',
      radius: ['40%', '70%'],
      center: ['35%', '50%'],
      avoidLabelOverlap: false,
      itemStyle: {
        borderRadius: 10,
        borderColor: 'rgba(30, 30, 30, 0.8)',
        borderWidth: 2,
      },
      label: {
        show: false,
        position: 'center',
      },
      emphasis: {
        label: {
          show: true,
          fontSize: 16,
          fontWeight: 'bold',
          color: '#fff',
        },
        itemStyle: {
          shadowBlur: 10,
          shadowOffsetX: 0,
          shadowColor: 'rgba(0, 0, 0, 0.5)',
        },
      },
      labelLine: {
        show: false,
      },
      data: pieChartData.value,
    },
  ],
  animation: true,
  animationDuration: 1000,
  animationEasing: 'cubicOut' as const,
}))

// Process data to create tree table structure with proper hierarchy
const processedTableData = computed(() => {
  const paginatedData = getTimeTickersPaginated.response.value
  const rawData = paginatedData?.items || []
  const result: any[] = []

  // Recursive function to build tree structure with proper indentation
  const buildTreeData = (items: any[], depth: number = 0, parentPath: string = '') => {
    items.forEach((item, index) => {
      const currentPath = parentPath ? `${parentPath}.${index}` : `${index}`
      const hasChildren = item.children && item.children.length > 0
      const isExpanded = expandedParents.value.has(item.id)

      // Add the current item with tree metadata
      const treeItem = {
        ...item,
        // Tree structure properties
        depth: depth,
        isParent: hasChildren,
        isChild: depth > 0,
        isExpanded: isExpanded,
        hasChildren: hasChildren,
        childrenCount: item.children?.length || 0,
        treePath: currentPath,
        // Visual properties for tree rendering
        isFirstChild: index === 0,
        isLastChild: index === items.length - 1,
        children: item.children || [],
      }

      result.push(treeItem)

      // If this item is expanded and has children, add them recursively
      if (isExpanded && hasChildren) {
        buildTreeData(item.children, depth + 1, currentPath)
      }
    })
  }

  // Build the tree structure starting from root level
  buildTreeData(rawData, 0)

  return result
})

const headersWithSelection = computed(() => {
  // For now, define headers manually since paginated response might not have headers
  const headers = [
    { title: 'Function', key: 'function', sortable: true, visibility: true },
    { title: 'Status', key: 'status', sortable: true, visibility: true },
    { title: 'Request Type', key: 'RequestType', sortable: false, visibility: true },
    { title: 'Description', key: 'description', sortable: true, visibility: true },
    { title: 'Execution Time', key: 'executionTimeFormatted', sortable: true, visibility: true },
    { title: 'Executed At (Elapsed Time)', key: 'executedAt', sortable: false, visibility: true },
    { title: 'Lock Holder', key: 'lockHolder', sortable: false, visibility: true },
    { title: 'Retry Status', key: 'retryIntervals', sortable: false, visibility: true },
    { title: 'Actions', key: 'actions', sortable: false, visibility: true },
  ]
  headers.unshift({
    title: '',
    key: 'selection',
    sortable: false,
    visibility: true,
  })
  headers.push({
    title: '',
    key: 'treeMarker',
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

const selectAllVisible = () => {
  const ids = processedTableData.value
    .filter((item) => !item.isChild)
    .map((item) => item.id as string)

  selectedItems.value = new Set(ids)
}

const deleteSelected = async () => {
  if (selectedItems.value.size === 0) return

  const ids = Array.from(selectedItems.value)
  try {
    await deleteTimeTickersBatch.requestAsync(ids)
    clearSelection()
    await loadPageData()
    await loadPieChartData()
  } catch (error) {
    console.error('Failed to delete selected time tickers:', error)
  }
}

// Chain Jobs Modal Methods
const openChainJobsModal = () => {
  chainJobsModal.value.isOpen = true
}

const onChainJobsCreated = async (result: any) => {
  console.log('Chain jobs created successfully!', result)
  await loadPageData()
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

// Enhanced tree navigation functions
const expandAll = () => {
  const allParentIds = processedTableData.value
    .filter((item) => item.hasChildren)
    .map((item) => item.id)

  expandedParents.value = new Set(allParentIds)
}

const collapseAll = () => {
  expandedParents.value.clear()
  expandedParents.value = new Set()
}

const expandToLevel = (maxDepth: number) => {
  const parentsToExpand = processedTableData.value
    .filter((item) => item.hasChildren && item.depth < maxDepth)
    .map((item) => item.id)

  expandedParents.value = new Set(parentsToExpand)
}

// Row props function to style rows based on tree depth and type
const getRowProps = (item: any) => {
  const classes = []
  const styles: any = {}

  classes.push('tree-leaf-row')
  classes.push('tree-root-row')

  return {
    class: classes.join(' '),
    style: styles,
  }
}

// Enhanced tree table helper functions
const getTreeIcon = (item: any) => {
  // Parent nodes - folder icons
  if (item.depth === 0) {
    return item.isExpanded ? 'mdi-folder-open-outline' : 'mdi-folder-outline'
  } else {
    return item.isExpanded ? 'mdi-folder-multiple-outline' : 'mdi-folder-multiple'
  }
}

const getTreeIconColor = (item: any) => {
  // Parent nodes - different colors by depth
  if (item.depth === 0) {
    return 'amber'
  } else if (item.depth === 1) {
    return 'blue'
  } else {
    return 'purple'
  }
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

// Get the actual hex color for a status
const getStatusHexColor = (status: string | number) => {
  // Convert numeric status to string name
  const statusStr = typeof status === 'number' ? Status[status] : status
  
  switch (statusStr) {
    case 'Done':
      return '#32CD32' // Lime Green
    case 'DueDone':
      return '#008000' // Green
    case 'InProgress':
      return '#6495ED' // Royal Blue
    case 'Queued':
      return '#00CED1' // Dark Turquoise
    case 'Idle':
      return '#A9A9A9' // Dark Gray
    case 'Failed':
      return '#FF0000' // Red
    case 'Cancelled':
      return '#FFD700' // Gold/Yellow
    case 'Skipped':
      return '#BA68C8' // Medium Orchid (Purple)
    default:
      return '#808080' // Default gray
  }
}

const refreshData = async () => {
  await loadPageData()
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

const canBeForceDeleted = ref<string[]>([])
</script>

<template>
  <div class="time-ticker-dashboard">
    <!-- Content Section -->
    <div class="dashboard-content">
      <!-- Analytics Section -->
      <div class="content-card analytics-overview">
        <!-- Chart Toggle Switch - Vertical Edge Design -->
        <div class="chart-toggle-vertical">
          <div class="chart-switch-vertical">
            <div class="toggle-label">Charts</div>
            <v-btn
              :color="activeChart === 'line' ? 'primary' : 'default'"
              variant="elevated"
              density="compact"
              class="chart-btn-vertical"
              @click="activeChart = 'line'"
            >
              <v-icon class="mb-1 chart-icon">mdi-chart-line</v-icon>
              <span class="btn-text">Time</span>
            </v-btn>
            <v-btn
              :color="activeChart === 'pie' ? 'primary' : 'default'"
              variant="elevated"
              density="compact"
              class="chart-btn-vertical"
              @click="activeChart = 'pie'"
            >
              <v-icon class="mb-1 chart-icon">mdi-chart-pie</v-icon>
              <span class="btn-text">Status</span>
            </v-btn>
          </div>
        </div>

        <!-- Time Series Analysis Chart -->
        <div v-if="activeChart === 'line'" class="chart-section">
          <div class="chart-wrapper">
            <v-chart
              class="chart-compact"
              :option="lineChartOption"
              :key="`chart-${chartKey}`"
              :loading="chartLoading"
              autoresize
            />
          </div>
        </div>

        <!-- Status Distribution Pie Chart -->
        <div v-if="activeChart === 'pie'" class="chart-section">
          <div class="chart-wrapper">
            <v-chart
              class="chart-compact"
              :option="pieChartOption"
              :key="`pie-${pieChartKey}`"
              :loading="chartLoading"
              autoresize
            />
          </div>
        </div>
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

                <button class="premium-action-btn tertiary-action" @click="openChainJobsModal()">
                  <div class="btn-icon">
                    <v-icon size="18">mdi-family-tree</v-icon>
                  </div>
                  <span class="btn-text">Chain Jobs</span>
                  <div class="btn-shine"></div>
                </button>
              </div>
            </div>

            <!-- Tree Navigation Controls -->
            <div class="tree-controls">
              <v-btn-group variant="outlined" density="compact" class="tree-nav-group">
                <v-btn
                  @click="expandAll"
                  size="small"
                  prepend-icon="mdi-unfold-more-horizontal"
                  class="tree-nav-btn"
                >
                  Expand All
                </v-btn>
                <v-btn
                  @click="collapseAll"
                  size="small"
                  prepend-icon="mdi-unfold-less-horizontal"
                  class="tree-nav-btn"
                >
                  Collapse All
                </v-btn>
                <v-menu>
                  <template v-slot:activator="{ props }">
                    <v-btn
                      v-bind="props"
                      size="small"
                      append-icon="mdi-chevron-down"
                      class="tree-nav-btn"
                    >
                      Expand to Level
                    </v-btn>
                  </template>
                  <v-list density="compact">
                    <v-list-item @click="expandToLevel(1)" class="tree-level-item">
                      <template v-slot:prepend>
                        <v-icon>mdi-numeric-1-box</v-icon>
                      </template>
                      <v-list-item-title>Level 1</v-list-item-title>
                    </v-list-item>
                    <v-list-item @click="expandToLevel(2)" class="tree-level-item">
                      <template v-slot:prepend>
                        <v-icon>mdi-numeric-2-box</v-icon>
                      </template>
                      <v-list-item-title>Level 2</v-list-item-title>
                    </v-list-item>
                    <v-list-item @click="expandToLevel(3)" class="tree-level-item">
                      <template v-slot:prepend>
                        <v-icon>mdi-numeric-3-box</v-icon>
                      </template>
                      <v-list-item-title>Level 3</v-list-item-title>
                    </v-list-item>
                  </v-list>
                </v-menu>
              </v-btn-group>
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
                :color="getTimeTickersPaginated.loader.value ? 'warning' : 'success'"
                variant="tonal"
                size="small"
                class="status-chip"
              >
                <v-icon size="small" class="mr-1">
                  {{ getTimeTickersPaginated.loader.value ? 'mdi-loading' : 'mdi-check' }}
                </v-icon>
                {{
                  getTimeTickersPaginated.loader.value ? 'Loading...' : `${totalCount} total items`
                }}
              </v-chip>
            </div>

            <!-- Utility Actions Group -->
            <div class="utility-actions">
              <v-btn
                v-if="processedTableData.length > 0"
                size="small"
                color="grey"
                variant="text"
                prepend-icon="mdi-select-all"
                class="utility-btn"
                @click="selectAllVisible"
              >
                Select All
              </v-btn>

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
                color="error"
                variant="text"
                prepend-icon="mdi-trash-can"
                class="utility-btn"
                @click="deleteSelected"
              >
                Delete Selected ({{ selectedItems.size }})
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
            :row-props="getRowProps"
            :headers="headersWithSelection"
            :loading="getTimeTickersPaginated.loader.value"
            :items="processedTableData"
            item-value="Id"
            :items-per-page="-1"
            class="enhanced-table dense-table"
            :search="tableSearch"
            hide-default-footer
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
              <div class="tree-cell" :style="{ paddingLeft: item.depth * 28 + 'px' }">
                <!-- Tree Structure with improved lines -->
                <div class="tree-structure" v-if="item.depth > 0">
                  <svg class="tree-svg" :width="item.depth * 28" height="32">
                    <!-- Draw vertical lines for each parent level -->
                    <line
                      v-for="level in item.depth"
                      :key="`v-${level}`"
                      :x1="(level - 1) * 28 + 14"
                      :x2="(level - 1) * 28 + 14"
                      :y1="0"
                      :y2="item.isLastChild && level === item.depth ? 16 : 32"
                      stroke="rgba(100, 181, 246, 0.4)"
                      stroke-width="1"
                      stroke-dasharray="2,2"
                    />
                    <!-- Draw horizontal connector line -->
                    <line
                      :x1="(item.depth - 1) * 28 + 14"
                      :x2="(item.depth - 1) * 28 + 26"
                      :y1="16"
                      :y2="16"
                      stroke="rgba(100, 181, 246, 0.4)"
                      stroke-width="1"
                      stroke-dasharray="2,2"
                    />
                  </svg>
                </div>

                <div class="tree-content d-flex align-center">
                  <!-- Expand/Collapse Button with animation -->
                  <v-btn
                    v-if="item.hasChildren"
                    :icon="item.isExpanded ? 'mdi-chevron-down' : 'mdi-chevron-right'"
                    size="x-small"
                    variant="text"
                    @click="toggleParentExpansion(item.id)"
                    class="tree-expand-btn"
                    color="primary"
                    density="compact"
                  >
                  </v-btn>

                  <!-- Placeholder for alignment when no expand button -->
                  <div v-else class="tree-expand-placeholder"></div>

                  <!-- Tree Node Icon with children count -->
                  <div class="tree-icon-wrapper" v-if="item.hasChildren">
                    <v-icon
                      :icon="getTreeIcon(item)"
                      size="small"
                      :color="getTreeIconColor(item)"
                      class="tree-icon"
                    ></v-icon>
                  </div>

                  <!-- Function Name with improved typography -->
                  <div class="function-content">
                    <span class="function-name">
                      {{ item.function }}
                    </span>
                  </div>
                </div>
              </div>
            </template>

            <template v-slot:item.status="{ item }">
              <div class="d-flex align-center">
                <v-chip
                  :style="{ 
                    backgroundColor: getStatusHexColor(item.status) + '20', 
                    color: getStatusHexColor(item.status) + ' !important', 
                    borderColor: getStatusHexColor(item.status),
                    border: '1px solid ' + getStatusHexColor(item.status)
                  }"
                  size="x-small"
                  class="status-chip"
                  @click="
                        hasStatus(item.status, Status.Failed) || hasStatus(item.status, Status.Skipped)
                      ? exceptionDialog.open({
                          ...new ConfirmDialogProps(),
                          title: hasStatus(item.status, Status.Skipped) ? 'Skipped Reason' : 'Exception Details',
                          text: hasStatus(item.status, Status.Skipped) ? item.skippedReason! : item.exceptionMessage!,
                          showConfirm: false,
                          maxWidth: '900',
                          icon: hasStatus(item.status, Status.Failed) ? 'mdi-bug-outline' : 'mdi-information-outline',
                          isException: hasStatus(item.status, Status.Skipped)? false : true,
                        })
                      : null
                  "
                >
                  <v-icon size="x-small" class="mr-1" v-if="hasStatus(item.status, Status.Failed)">
                    mdi-bug-outline
                  </v-icon>
                  <v-icon size="x-small" class="mr-1" v-else-if="hasStatus(item.status, Status.Done) || hasStatus(item.status, Status.DueDone)"> mdi-check-circle </v-icon>
                  <v-icon size="x-small" class="mr-1" v-else-if="hasStatus(item.status, Status.Skipped)"> mdi-debug-step-over </v-icon>
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
              <div class="retry-display" v-if="getRetryIntervalsArray(item.retryIntervals)?.length || (item.retries && item.retries > 0)">
                <div class="retry-header" v-if="item.retries > 0">
                  <span class="retry-count-label">Attempt {{ item.retryCount || 0 }}/{{ item.retries }}</span>
                </div>
                <span class="retry-sequence" v-if="getRetryIntervalsArray(item.retryIntervals)?.length">
                  <template v-for="(interval, index) in getDisplayIntervals(item)" :key="index">
                    <span class="retry-item" :class="getRetryStatus(interval.originalIndex, item)"
                      >{{ interval.originalIndex + 1 }}:{{ interval.value }}</span
                    >
                  </template>
                  <span v-if="getHiddenCount(item) > 0" class="retry-more">
                    +{{ getHiddenCount(item) }}
                  </span>
                </span>
                <span v-else class="no-intervals">(Default: 30s)</span>
              </div>
              <span v-else class="no-retries">—</span>
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
                        @click="
                          requestCancel(item.id).catch(() => {
                            canBeForceDeleted.push(item.id)
                          })
                        "
                        :disabled="
                          !hasStatus(item.status, Status.InProgress) ||
                          canBeForceDeleted.includes(item.id)
                        "
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
                            executionTime: item.executionTime,
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
                            executionTime: item.executionTime,
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
                  :class="{
                    'action-btn-disabled':
                      hasStatus(item.status, Status.InProgress) &&
                      !canBeForceDeleted.includes(item.id),
                  }"
                >
                  <v-tooltip location="top">
                    <template v-slot:activator="{ props }">
                      <button
                        v-bind="props"
                        @click="confirmDialog.open({ id: item.id })"
                        :disabled="
                          hasStatus(item.status, Status.InProgress) &&
                          !canBeForceDeleted.includes(item.id)
                        "
                        class="modern-action-btn delete-btn"
                        :class="{
                          active:
                            !hasStatus(item.status, Status.InProgress) ||
                            canBeForceDeleted.includes(item.id),
                        }"
                      >
                        <v-icon size="16">mdi-trash-can</v-icon>
                      </button>
                    </template>
                    <span>{{
                      canBeForceDeleted.includes(item.id) ? 'Force Delete Ticker' : 'Delete Ticker'
                    }}</span>
                  </v-tooltip>
                </div>
              </div>
            </template>

            <!-- Tree Marker Column -->
            <template v-slot:item.treeMarker="{ item }">
              <div class="tree-marker">
                <svg width="32" height="32" viewBox="0 0 32 32" class="tree-marker-svg">
                  <!-- Mirror the left side tree structure on the right -->
                  <g v-if="item.depth > 0">
                    <!-- Vertical dashed lines for each depth level (mirrored from right) -->
                    <line
                      v-for="level in item.depth"
                      :key="`v-${level}`"
                      :x1="32 - (level - 1) * 6 - 6"
                      :x2="32 - (level - 1) * 6 - 6"
                      :y1="0"
                      :y2="item.isLastChild && level === item.depth ? 16 : 32"
                      stroke="rgba(100, 181, 246, 0.4)"
                      stroke-width="1"
                      stroke-dasharray="2,2"
                    />
                    <!-- Horizontal connector line (mirrored from right) -->
                    <line
                      :x1="32 - (item.depth - 1) * 6 - 6"
                      :x2="32 - (item.depth - 1) * 6 - 18"
                      :y1="16"
                      :y2="16"
                      stroke="rgba(100, 181, 246, 0.4)"
                      stroke-width="1"
                      stroke-dasharray="2,2"
                    />
                    <!-- Dotted path to parent - vertical connection upward with rounded end -->
                    <path
                      :d="`M ${32 - (item.depth - 1) * 6 - 6} 0 L ${32 - (item.depth - 1) * 6 - 6} -6 Q ${32 - (item.depth - 1) * 6 - 6} -8 ${32 - (item.depth - 1) * 6 - 4} -8`"
                      stroke="rgba(100, 181, 246, 0.4)"
                      stroke-width="1"
                      stroke-dasharray="1,1"
                      fill="none"
                      stroke-linecap="round"
                    />

                    <!-- Downward tree structure for child items that have grandchildren (only when expanded) -->
                    <g v-if="item.hasChildren && item.isExpanded">
                      <!-- Vertical dashed line going downward -->
                      <line
                        :x1="32 - item.depth * 6 - 6"
                        :x2="32 - item.depth * 6 - 6"
                        y1="24"
                        y2="32"
                        stroke="rgba(100, 181, 246, 0.4)"
                        stroke-width="1"
                        stroke-dasharray="2,2"
                      />
                      <!-- Horizontal connector line (positioned lower to avoid overlap) -->
                      <line
                        :x1="32 - item.depth * 6 - 6"
                        :x2="32 - item.depth * 6 - 18"
                        y1="24"
                        y2="24"
                        stroke="rgba(100, 181, 246, 0.4)"
                        stroke-width="1"
                        stroke-dasharray="2,2"
                      />
                      <!-- Dotted path downward with rounded end -->
                      <path
                        :d="`M ${32 - item.depth * 6 - 6} 32 L ${32 - item.depth * 6 - 6} 38 Q ${32 - item.depth * 6 - 6} 40 ${32 - item.depth * 6 - 4} 40`"
                        stroke="rgba(100, 181, 246, 0.4)"
                        stroke-width="1"
                        stroke-dasharray="1,1"
                        fill="none"
                        stroke-linecap="round"
                      />
                    </g>
                  </g>
                  <!-- For root items that have children, show downward tree structure -->
                  <g v-else-if="item.hasChildren">
                    <!-- Vertical dashed line going downward (positioned like depth 1) -->
                    <line
                      x1="26"
                      x2="26"
                      y1="16"
                      y2="32"
                      stroke="rgba(100, 181, 246, 0.4)"
                      stroke-width="1"
                      stroke-dasharray="2,2"
                    />
                    <!-- Horizontal connector line -->
                    <line
                      x1="26"
                      x2="14"
                      y1="16"
                      y2="16"
                      stroke="rgba(100, 181, 246, 0.4)"
                      stroke-width="1"
                      stroke-dasharray="2,2"
                    />
                    <!-- Dotted path downward with rounded end -->
                    <path
                      d="M 26 32 L 26 38 Q 26 40 28 40"
                      stroke="rgba(100, 181, 246, 0.4)"
                      stroke-width="1"
                      stroke-dasharray="1,1"
                      fill="none"
                      stroke-linecap="round"
                    />
                  </g>
                  <!-- No content for items without children -->
                </svg>
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
    <ChainJobsModal v-model="chainJobsModal.isOpen" @created="onChainJobsCreated" />
  </div>
</template>

<style scoped>
/* Enhanced Tree Table Styles */
.tree-cell {
  position: relative;
  min-height: 36px;
  padding: 4px 0;
}

.tree-structure {
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  pointer-events: none;
}

.tree-svg {
  position: absolute;
  top: 0;
  left: 0;
}

.tree-content {
  position: relative;
  z-index: 1;
  gap: 8px;
}

.tree-expand-btn {
  min-width: 24px !important;
  width: 24px !important;
  height: 24px !important;
  margin-right: 4px;
  transition: all 0.2s ease;
  border-radius: 4px !important;
}

.tree-expand-btn:hover {
  background-color: rgba(100, 181, 246, 0.1) !important;
  transform: scale(1.1);
}

.tree-expand-placeholder {
  width: 28px;
  height: 24px;
  margin-right: 4px;
}

.tree-icon-wrapper {
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;
  width: 32px;
  height: 24px;
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.05);
  transition: all 0.2s ease;
}

.tree-icon-wrapper:hover {
  background: rgba(255, 255, 255, 0.1);
  transform: translateY(-1px);
}

.tree-icon {
  transition: all 0.2s ease;
}

.function-content {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 8px;
}

/* Function Type Chips */
.function-type-chip {
  font-size: 9px !important;
  height: 16px !important;
  font-weight: 600 !important;
  letter-spacing: 0.5px;
}

/* Children Count Chip positioned next to icon */
.tree-icon-wrapper .children-count-chip {
  position: absolute;
  top: -6px;
  right: -8px;
  font-size: 8px !important;
  height: 14px !important;
  min-width: 14px !important;
  padding: 0 4px !important;
  font-weight: 700 !important;
  z-index: 2;
}

/* Animation for expand/collapse */
@keyframes treeExpand {
  from {
    opacity: 0;
    transform: translateY(-4px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.tree-cell {
  animation: treeExpand 0.2s ease-out;
}

/* Tree Navigation Controls */
.tree-controls {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 0 16px;
}

.tree-nav-group {
  background: rgba(255, 255, 255, 0.05);
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.tree-nav-btn {
  font-size: 0.75rem !important;
  font-weight: 500;
  letter-spacing: 0.3px;
  color: #e0e0e0 !important;
  border-color: rgba(255, 255, 255, 0.1) !important;
  transition: all 0.2s ease;
}

/* 
.tree-level-item {
  font-size: 0.875rem;
  transition: all 0.2s ease;
}

.tree-level-item:hover {
  background: rgba(100, 181, 246, 0.1);
  color: #64b5f6;
} */

/* Tree Row Styling
:deep(.tree-root-row) {
  background-color: rgba(255, 255, 255, 0.02) !important;
  border-left: 3px solid transparent !important;
} */

:deep(.tree-leaf-row .v-data-table__td) {
  font-weight: 400;
  color: #d0d0d0 !important;
}

:deep(.tree-child-row) {
  position: relative;
}

/* Tree Marker Column Styles */
.tree-marker {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  width: 100%;
  height: 32px;
  padding-right: 4px;
}

.tree-marker-svg {
  transition: all 0.2s ease;
}

.tree-marker:hover .tree-marker-svg line {
  stroke: rgba(100, 181, 246, 0.6) !important;
}

.tree-marker:hover .tree-marker-svg circle {
  fill: rgba(100, 181, 246, 0.6) !important;
}

/* Hover effects for tree rows */
:deep(.tree-root-row):hover {
  background-color: rgba(100, 180, 246, 0.1) !important;
}

/* Responsive Tree Controls */
@media (max-width: 768px) {
  .tree-controls {
    margin: 8px 0;
    justify-content: center;
  }

  .tree-nav-btn {
    font-size: 0.7rem !important;
    padding: 4px 8px !important;
  }
}

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
  min-height: 64px;
  height: 64px;
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
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.retry-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 2px;
}

.retry-count-label {
  font-weight: 600;
  color: #64b5f6;
  font-size: 0.8rem;
  padding: 2px 6px;
  background: rgba(100, 181, 246, 0.15);
  border-radius: 4px;
}

.no-retries {
  content: '—';
  color: #6b7280;
  font-style: italic;
  display: inline-block;
}

.no-intervals {
  color: #888;
  font-size: 0.7rem;
  font-style: italic;
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

::v-deep(.v-data-table) {
  empty-cells: show;
}
::v-deep(td.v-data-table__td:empty) {
  position: relative;
}
::v-deep(td.v-data-table__td:empty)::after {
  content: '—';
  color: #6b7280;
  font-style: italic;
  display: inline-block;
}

/* Analytics Overview Card */
.analytics-overview {
  background: rgba(66, 66, 66, 0.9);
  backdrop-filter: blur(20px);
  border-radius: 12px;
  border: 1px solid rgba(255, 255, 255, 0.05);
  box-shadow: 0 4px 15px rgba(0, 0, 0, 0.3);
  overflow: visible;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  position: relative;
  margin-top: 24px;
}

.analytics-overview:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 25px rgba(0, 0, 0, 0.6);
}

/* Chart Styles */
.chart-toggle-vertical {
  position: absolute;
  top: 20px;
  left: -70px;
  z-index: 10;
  background: rgba(0, 0, 0, 0.1);
  border-radius: 20px;
  padding: 8px;
}

.chart-switch-vertical {
  background: rgba(66, 66, 66, 0.95);
  backdrop-filter: blur(20px);
  border-radius: 16px;
  border: 1px solid rgba(100, 181, 246, 0.4);
  box-shadow: 0 8px 32px rgba(100, 181, 246, 0.2);
  overflow: hidden;
  padding: 8px 4px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.chart-switch-vertical .toggle-label {
  font-size: 0.7rem;
  font-weight: 600;
  color: #bdbdbd;
  text-transform: uppercase;
  letter-spacing: 1px;
  padding: 4px 0;
  text-align: center;
}

.chart-btn-vertical {
  min-width: 60px;
  min-height: 70px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 8px 4px;
  font-size: 0.75rem;
  text-transform: none;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid transparent;
  transition: all 0.3s ease;
  position: relative;
}

.chart-btn-vertical.v-btn--active {
  background: linear-gradient(135deg, #64b5f6, #42a5f5);
  box-shadow: 0 4px 12px rgba(100, 181, 246, 0.3);
  border: 1px solid rgba(100, 181, 246, 0.6);
}

.chart-btn-vertical:not(.v-btn--active):hover {
  background: rgba(100, 181, 246, 0.1);
  border: 1px solid rgba(100, 181, 246, 0.3);
}

.chart-btn-vertical .chart-icon {
  font-size: 20px;
  margin-bottom: 4px;
}

.chart-btn-vertical .btn-text {
  font-size: 0.7rem;
  font-weight: 500;
  white-space: nowrap;
}

.chart-section {
  margin: 0;
  display: flex;
  flex-direction: column;
  align-items: stretch;
  text-align: center;
  width: 100%;
  animation: fadeIn 0.3s ease-in-out;
}

.chart-wrapper {
  height: 420px;
  width: 100%;
  max-width: 100%;
  margin: 0;
  position: relative;
  overflow: hidden;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.02);
  border: 1px solid rgba(255, 255, 255, 0.05);
}

.chart-compact {
  width: 100%;
  height: 100%;
}

@keyframes fadeIn {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* Responsive Chart Styles */
@media (max-width: 1400px) {
  .chart-toggle-vertical {
    top: 16px;
    left: -52px;
  }

  .chart-btn-vertical {
    min-width: 50px;
    min-height: 60px;
    font-size: 0.7rem;
  }

  .chart-wrapper {
    height: 380px;
  }
}

@media (max-width: 768px) {
  .chart-toggle-vertical {
    top: 12px;
    left: -45px;
  }

  .chart-btn-vertical {
    min-width: 40px;
    min-height: 50px;
    font-size: 0.65rem;
    padding: 6px 3px;
  }

  .btn-text {
    font-size: 0.6rem;
  }

  .chart-wrapper {
    height: 320px;
  }
}
</style>
