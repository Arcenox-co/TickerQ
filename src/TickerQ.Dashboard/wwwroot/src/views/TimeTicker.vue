<script lang="ts" setup>
import { onMounted, ref, provide, computed, onUnmounted, nextTick, watch } from 'vue'
import { timeTickerService } from '@/http/services/timeTickerService'
import type { GetTimeTickerResponse } from '@/http/services/types/timeTickerService.types'
import { Status } from '@/http/services/types/base/baseHttpResponse.types'
import { tickerService } from '@/http/services/tickerService'
import { useDialog } from '@/composables/useDialog'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart, PieChart } from 'echarts/charts'
import TickerNotificationHub, { methodName } from '@/hub/tickerNotificationHub'
import { formatDate, formatFromUtcToLocal } from '@/utilities/dateTimeParser'
import {
  TitleComponent,
  TooltipComponent,
  LegendComponent,
  ToolboxComponent,
  GridComponent,
} from 'echarts/components'
import VChart, { THEME_KEY } from 'vue-echarts'

const getTimeTickers = timeTickerService.getTimeTickers()
const deleteTimeTicker = timeTickerService.deleteTimeTicker()
const setBatchParent = timeTickerService.setBatchParent()
const unbatchTicker = timeTickerService.unbatchTicker()
const requestCancelTicker = tickerService.requestCancel()
const getTimeTickersGraphDataRange = timeTickerService.getTimeTickersGraphDataRange()
const getTimeTickersGraphData = timeTickerService.getTimeTickersGraphData()

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

const batchOperationDialog = ref({
  isOpen: false,
  selectedItems: [] as any[],
  batchRunCondition: 0,
})

const createBatchDialog = ref({
  isOpen: false,
  selectedParentId: '',
  selectedChildrenIds: [] as string[],
  batchRunCondition: 0,
  step: 1, // 1: Select Parent, 2: Select Children
})

const requestMatchType = ref(new Map<string, number>())
const crudTimeTickerDialogRef = ref(null)

const expandedParents = ref(new Set<string>())
const tableSearch = ref('')

const selectedItems = ref(new Set<string>())
const isMounted = ref(false)

function findItemById(id: string) {
  return processedTableData.value.find((x) => x.id === id)
}

function isStatusBatchable(status: string) {
  return status === 'Idle' || status === 'Queued'
}

const availableParents = computed(() => {
  return processedTableData.value
    .filter((item) => isStatusBatchable(item.status))
    .map((item) => ({
      title: `${item.function} (${item.status})`,
      value: item.id,
      subtitle: `Execution: ${item.executionTime}`,
    }))
})

const availableChildren = computed(() => {
  if (!createBatchDialog.value.selectedParentId) return []

  const parent = findItemById(createBatchDialog.value.selectedParentId)
  if (!parent) return []

  const parentTime = parent.executionTime ? Date.parse(parent.executionTime) : 0

  return processedTableData.value
    .filter((item) => {
      if (item.id === createBatchDialog.value.selectedParentId) return false
      if (!isStatusBatchable(item.status)) return false
      if (item.batchParent) return false // Already has a parent

      const childTime = item.executionTime ? Date.parse(item.executionTime) : 0
      return isNaN(parentTime) || isNaN(childTime) ? true : childTime >= parentTime
    })
    .map((item) => ({
      title: `${item.function} (${item.status})`,
      value: item.id,
      subtitle: `Execution: ${item.executionTime}`,
    }))
})

onMounted(async () => {
  try {
    isMounted.value = true
    
    // Initialize WebSocket connection
    try {
      if (!connectionStore.isInitialized) {
        await connectionStore.initializeConnection()
      }
    } catch (error) {
      // WebSocket connection failed, continuing without it
    }
    
    // Load initial data
    try {
      await getTimeTickers.requestAsync()
    } catch (error) {
      // Failed to load time tickers
    }
    
    try {
      const res = await getTimeTickersGraphDataRange.requestAsync(7, 7)
      if (res && res.length > 0) {
        const min = res[0].date
        const max = res[res.length - 1].date
        if (min && max) {
          await getTimeTickersGraphData.requestAsync()
        }
      }
    } catch (error) {
      // Failed to load graph data range
    }
    
    try {
      await getTimeTickersGraphData.requestAsync()
    } catch (error) {
      // Failed to load status distribution data
    }
    
    // Check if still mounted before continuing
    if (!isMounted.value) return
    
    // Add hub listeners
    try {
      await addHubListeners()
    } catch (error) {
      // Failed to add hub listeners
    }
    
  } catch (error) {
    // Critical error during TimeTicker mount
  }
})

onUnmounted(() => {
  isMounted.value = false
  
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

// Process data to create hierarchical structure
const processedTableData = computed(() => {
  const rawData = getTimeTickers.response.value || []
  const result: any[] = []

  // Create maps for quick lookup
  const parentMap = new Map<string, any>()
  const childrenMap = new Map<string, any[]>()

  // First pass: separate parents and children
  rawData.forEach((item) => {
    if (!item.batchParent) {
      // This is a parent item
      parentMap.set(item.id, { ...item, isParent: true, children: [] })
    } else {
      // This is a child item
      if (!childrenMap.has(item.batchParent)) {
        childrenMap.set(item.batchParent, [])
      }
      childrenMap.get(item.batchParent)?.push({ ...item, isChild: true })
    }
  })

  parentMap.forEach((parent, parentId) => {
    const children = childrenMap.get(parentId) || []
    parent.children = children
    result.push(parent)

    // Add children to result if parent is expanded
    if (expandedParents.value.has(parentId)) {
      children.forEach((child) => {
        result.push(child)
      })
    }
  })

  childrenMap.forEach((children, parentId) => {
    if (!parentMap.has(parentId)) {
      children.forEach((child) => {
        result.push({ ...child, isOrphan: true })
      })
    }
  })

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

const selectAllItems = () => {
  const allIds = processedTableData.value.map((item) => item.id)
  selectedItems.value = new Set(allIds)
}

const clearSelection = () => {
  selectedItems.value.clear()
}

const openBatchOperationDialog = () => {
  if (selectedItems.value.size === 0) return

  batchOperationDialog.value.selectedItems = processedTableData.value.filter((item) =>
    selectedItems.value.has(item.id),
  )
  batchOperationDialog.value.isOpen = true
}

const openCreateBatchDialog = () => {
  createBatchDialog.value.selectedParentId = ''
  createBatchDialog.value.selectedChildrenIds = []
  createBatchDialog.value.step = 1
  createBatchDialog.value.isOpen = true
}

const handleBatchOperationConfirm = () => {
  // This is now just a legacy function, actual batching happens in create batch dialog
  batchOperationDialog.value.isOpen = false
  batchOperationDialog.value.selectedItems = []
  clearSelection()
}

const handleBatchOperationCancel = () => {
  batchOperationDialog.value.isOpen = false
  batchOperationDialog.value.selectedItems = []
}

const handleCreateBatchConfirm = () => {
  const { selectedParentId, selectedChildrenIds, batchRunCondition } = createBatchDialog.value

  if (!selectedParentId || selectedChildrenIds.length === 0) return

  selectedChildrenIds.forEach((childId) => {
    setBatchParent.requestAsync({
      batchRunCondition: batchRunCondition,
      parentId: selectedParentId,
      targetId: childId,
    })
  })

  createBatchDialog.value.isOpen = false
  createBatchDialog.value.selectedParentId = ''
  createBatchDialog.value.selectedChildrenIds = []
  createBatchDialog.value.step = 1
}

const handleCreateBatchCancel = () => {
  createBatchDialog.value.isOpen = false
  createBatchDialog.value.selectedParentId = ''
  createBatchDialog.value.selectedChildrenIds = []
  createBatchDialog.value.step = 1
}

const nextStep = () => {
  if (createBatchDialog.value.selectedParentId) {
    createBatchDialog.value.step = 2
  }
}

const previousStep = () => {
  createBatchDialog.value.step = 1
}

// Unbatch functionality for individual items only
const unbatchItem = async (itemId: string) => {
  const item = findItemById(itemId)
  if (!item) return

  // Only unbatch this specific item, not its relationships
  if (item.batchParent) {
    try {
      await unbatchTicker.requestAsync({ tickerId: itemId })
    } catch (error) {
      // Failed to unbatch item
    }
  }
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
  const parent = processedTableData.value.find((item) => item.id === parentId && item.isParent)
  return parent?.children?.length || 0
}

const closeCrudTimeTickerDialog = () => {
  crudTimeTickerDialog.close()
}

const chartLoading = ref(false)
const chartError = ref(false)

const getTimeTickersGraphDataAndParseToGraph = async () => {
  chartLoading.value = true
  chartError.value = false

  try {
    // Set loading state
    totalOption.value.series[0].data = [
      { value: 1, name: 'Loading...', itemStyle: { color: '#64b5f6' } },
    ]

    const res = await getTimeTickersGraphData.requestAsync()

    if (res && Array.isArray(res) && res.length > 0) {
      const chartData = res
        .filter((item) => item.item2 > 0) // Only show statuses with count > 0
        .sort((a, b) => b.item2 - a.item2) // Sort by count descending
        .map((item, index) => ({
          name: Status[item.item1] || `Status ${item.item1}`,
          value: item.item2,
          itemStyle: {
            color: seriesColors[Status[item.item1]] || `hsl(${index * 45}, 70%, 60%)`,
            borderColor: '#ffffff',
            borderWidth: 2,
          },
          label: {
            show: true,
            formatter: function (params: any) {
              const percentage = params.percent
              return percentage > 8 ? `${params.name}\n${params.value}` : ''
            },
            color: '#ffffff',
            fontSize: 11,
            fontWeight: 'bold',
          },
          labelLine: {
            show: true,
            length: 10,
            length2: 5,
            lineStyle: {
              color: 'rgba(255, 255, 255, 0.6)',
              width: 2,
            },
          },
          emphasis: {
            itemStyle: {
              shadowBlur: 20,
              shadowColor: seriesColors[Status[item.item1]] || '#666',
              scale: true,
              scaleSize: 8,
            },
          },
        }))

      if (chartData.length > 0) {
        totalOption.value.series[0].data = chartData
        totalOption.value.title.text = `Status Distribution (${res.reduce((sum, item) => sum + item.item2, 0)} Total)`
      } else {
        totalOption.value.series[0].data = [
          { value: 1, name: 'No Data', itemStyle: { color: '#9e9e9e' } },
        ]
        totalOption.value.title.text = 'Status Distribution (No Data)'
      }
    } else {
      // No data received for Status Distribution
      totalOption.value.series[0].data = [
        { value: 1, name: 'No Data', itemStyle: { color: '#9e9e9e' } },
      ]
      totalOption.value.title.text = 'Status Distribution (No Data)'
    }
  } catch (error) {
    // Error loading Status Distribution
    totalOption.value.series[0].data = [
      { value: 1, name: 'Error', itemStyle: { color: '#f44336' } },
    ]
    totalOption.value.title.text = 'Status Distribution (Error)'
  } finally {
    chartLoading.value = false
  }
}

const getTimeTickersGraphDataRangeAndParseToGraph = async (startDate: number, endDate: number) => {
  try {
    const res = await getTimeTickersGraphDataRange.requestAsync(startDate, endDate)
    
    if (!res || !Array.isArray(res)) {
      return
    }
    
    // Extract unique Dates for xAxis
    const uniqueDates = res.map((x) => x.date)
    option.value.xAxis.data = uniqueDates // Assign to ECharts

    // Extract all unique item1 values (Status IDs)
    const uniqueItem1Set = new Set<number>()

    res.forEach(({ results }) => {
      if (results && Array.isArray(results)) {
        results.forEach(({ item1 }) => uniqueItem1Set.add(item1))
      }
    })
    const uniqueItem1Array = Array.from(uniqueItem1Set) // Convert Set to Array

    // Create a Map to store series data
    const seriesMap = new Map<number, number[]>() // item1 -> data array

    // Initialize seriesMap with empty arrays
    uniqueItem1Array.forEach((item1) => {
      seriesMap.set(item1, Array(uniqueDates.length).fill(0)) // Fill with 0s initially
    })

    // Populate seriesMap with actual data
    res.forEach(({ date, results }) => {
      if (results && Array.isArray(results)) {
        const dateIndex = uniqueDates.indexOf(date) // Find index in xAxis

        results.forEach(({ item1, item2 }) => {
          const dataArray = seriesMap.get(item1)
          if (dataArray) {
            dataArray[dateIndex] = item2 // Assign value at the correct index
          }
        })
      }
    })

    // Generate series data
    const composedData = Array.from(seriesMap.entries()).map(([item1, dataArray]) => ({
      data: dataArray,
      name: Status[item1] || `Unknown ${item1}`,
      type: 'line',
      smooth: true,
      symbol: 'circle',
      symbolSize: 6,
      lineStyle: {
        color: seriesColors[Status[item1]],
        width: 3,
        shadowBlur: 10,
        shadowColor: seriesColors[Status[item1]],
        shadowOffsetY: 2,
      },
      itemStyle: {
        color: seriesColors[Status[item1]],
        borderColor: '#ffffff',
        borderWidth: 2,
        shadowBlur: 10,
        shadowColor: 'rgba(0, 0, 0, 0.3)',
      },
      areaStyle: {
        color: {
          type: 'linear',
          x: 0,
          y: 0,
          x2: 0,
          y2: 1,
          colorStops: [
            { offset: 0, color: seriesColors[Status[item1]] + '40' },
            { offset: 1, color: seriesColors[Status[item1]] + '10' },
          ],
        },
      },
      emphasis: {
        itemStyle: {
          shadowBlur: 20,
          shadowColor: seriesColors[Status[item1]],
          scale: true,
          scaleSize: 2,
        },
        lineStyle: {
          width: 4,
        },
      },
    }))

    const statuses = [
      'Idle',
      'Queued',
      'InProgress',
      'Done',
      'DueDone',
      'Failed',
      'Cancelled',
      'Batched',
    ]

    const seriesNames = composedData.filter((x) => x.data.some((y) => y > 0)).map((x) => x.name)

    option.value.legend = {
      show: true,
      top: '15%',
      textStyle: { color: '#fff', fontSize: 11 },
      itemGap: 16,
      itemWidth: 12,
      itemHeight: 12,
      selected: Object.fromEntries(
        statuses.map((status) => [status, seriesNames.includes(status)]),
      ),
    }

    option.value.series = composedData as any
    
  } catch (error: any) {
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
      return
    }
    
    // Set default/error state for the chart
    option.value.series = []
    option.value.xAxis.data = []
  }
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
  Batched: '#A9A9A9', // Dark Gray
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
    case 'Batched':
      return 'secondary'
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

// Helper functions for enhanced functionality
const resetRange = () => {
  range.value = [-3, 3]
}

const refreshData = async () => {
  await getTimeTickers.requestAsync()
  await getTimeTickersGraphDataRangeAndParseToGraph(range.value[0], range.value[1])
  await getTimeTickersGraphDataAndParseToGraph()
}

// Helper functions for retry intervals display
const formatDuration = (duration: string) => {
  if (!duration) return ''
  // Keep the format simple and readable
  return duration.replace('00:00:', '').replace('00:', '')
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
    classes.push('child-row')
    return {
      style: `${baseStyle}; padding-left: 40px; background-color: rgba(255, 255, 255, 0.02);`,
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

use([
  CanvasRenderer,
  LineChart,
  TitleComponent,
  TooltipComponent,
  LegendComponent,
  ToolboxComponent,
  GridComponent,
  PieChart,
])

provide(THEME_KEY, 'dark')

const option = ref({
  backgroundColor: 'transparent',
  title: {
    text: 'Time Series Analysis',
    left: 'center',
    top: '2%',
    textStyle: {
      color: '#ffffff',
      fontSize: 14,
      fontWeight: 'bold',
      textShadow: '0 2px 4px rgba(0,0,0,0.3)',
    },
  },
  tooltip: {
    trigger: 'axis',
    backgroundColor: 'rgba(0, 0, 0, 0.95)',
    borderColor: 'rgba(100, 181, 246, 0.5)',
    borderWidth: 1,
    textStyle: {
      color: '#ffffff',
    },
    extraCssText: 'border-radius: 8px; box-shadow: 0 4px 20px rgba(0,0,0,0.3);',
    axisPointer: {
      type: 'cross',
      label: {
        backgroundColor: 'rgba(100, 181, 246, 0.8)',
        color: '#ffffff',
      },
    },
  },
  legend: {
    show: true,
    top: '15%',
    textStyle: {
      color: '#e0e0e0',
      fontSize: 11,
    },
    itemGap: 16,
    itemWidth: 12,
    itemHeight: 12,
    selected: {},
  },
  grid: {
    left: '8%',
    right: '8%',
    bottom: '15%',
    top: '25%',
    containLabel: true,
  },
  xAxis: {
    type: 'category',
    boundaryGap: false,
    data: [] as string[],
    axisLabel: {
      color: '#bdbdbd',
      fontSize: 10,
      rotate: 45,
    },
    axisLine: {
      lineStyle: {
        color: 'rgba(255, 255, 255, 0.3)',
        width: 1,
      },
    },
    axisTick: {
      lineStyle: {
        color: 'rgba(255, 255, 255, 0.3)',
        width: 1,
      },
      length: 6,
    },
    splitLine: {
      show: true,
      lineStyle: {
        color: 'rgba(255, 255, 255, 0.05)',
        width: 1,
        type: 'dashed',
      },
    },
  },
  yAxis: {
    type: 'value',
    min: 0,
    axisLine: {
      show: false,
    },
    axisTick: {
      show: false,
    },
    axisLabel: {
      color: '#bdbdbd',
      fontSize: 10,
    },
    splitLine: {
      show: true,
      lineStyle: {
        color: 'rgba(255, 255, 255, 0.08)',
        width: 1,
        type: 'dashed',
      },
    },
  },
  series: [],
  animation: true,
  animationDuration: 1000,
  animationEasing: 'cubicOut' as const,
})

const totalOption = ref({
  backgroundColor: 'transparent',
  title: {
    text: 'Status Distribution',
    left: 'center',
    top: '3%',
    textStyle: {
      color: '#ffffff',
      fontSize: 18,
      fontWeight: 'bold',
      textShadow: '0 2px 8px rgba(0,0,0,0.4)',
    },
  },
  tooltip: {
    trigger: 'item',
    formatter: function (params: any) {
      const percentage = params.percent || 0
      const value = params.value || 0
      return `<div style="
        padding: 12px;
        background: linear-gradient(135deg, rgba(0,0,0,0.95) 0%, rgba(30,30,30,0.95) 100%);
        border-radius: 12px;
        border: 1px solid rgba(100, 181, 246, 0.3);
        backdrop-filter: blur(20px);
      ">
        <div style="
          font-weight: bold; 
          margin-bottom: 8px; 
          color: #ffffff;
          font-size: 14px;
        ">${params.name}</div>
        <div style="
          display: flex; 
          justify-content: space-between; 
          gap: 16px;
          font-size: 13px;
        ">
          <span style="color: #64b5f6;">Count: <strong>${value}</strong></span>
          <span style="color: #4caf50;">Percent: <strong>${percentage.toFixed(1)}%</strong></span>
        </div>
      </div>`
    },
    backgroundColor: 'transparent',
    borderWidth: 0,
    textStyle: {
      color: '#ffffff',
    },
    extraCssText: 'box-shadow: 0 8px 32px rgba(0,0,0,0.5);',
  },
  legend: {
    orient: 'vertical',
    right: '2%',
    top: 'middle',
    textStyle: {
      color: '#e0e0e0',
      fontSize: 13,
      fontWeight: '500',
    },
    itemGap: 16,
    itemWidth: 16,
    itemHeight: 16,
    formatter: function (name: string) {
      return name.length > 12 ? name.substring(0, 12) + '...' : name
    },
    selector: false,
  },
  series: [
    {
      type: 'pie',
      radius: ['30%', '70%'],
      center: ['35%', '55%'],
      roseType: false,
      avoidLabelOverlap: true,
      data: [{ value: 1, name: 'Loading...', itemStyle: { color: '#64b5f6' } }],
      label: {
        show: true,
        position: 'outside',
        color: '#ffffff',
        fontSize: 12,
        fontWeight: 'bold',
        formatter: function (params: any) {
          const percentage = params.percent || 0
          return percentage > 5 ? `${params.name}\n${params.value}` : ''
        },
        textBorderColor: 'rgba(0, 0, 0, 0.8)',
        textBorderWidth: 2,
      },
      labelLine: {
        show: true,
        length: 12,
        length2: 8,
        lineStyle: {
          color: 'rgba(255, 255, 255, 0.7)',
          width: 2,
        },
      },
      itemStyle: {
        borderRadius: 8,
        borderWidth: 3,
        borderColor: 'rgba(255, 255, 255, 0.15)',
        shadowBlur: 10,
        shadowColor: 'rgba(0, 0, 0, 0.3)',
      },
      emphasis: {
        itemStyle: {
          shadowBlur: 25,
          shadowOffsetX: 0,
          shadowOffsetY: 0,
          shadowColor: 'rgba(0, 0, 0, 0.6)',
          scale: true,
          scaleSize: 10,
          borderWidth: 4,
          borderColor: 'rgba(255, 255, 255, 0.8)',
        },
        label: {
          fontSize: 14,
          fontWeight: 'bold',
        },
        labelLine: {
          lineStyle: {
            width: 3,
          },
        },
      },
      animationType: 'scale',
      animationEasing: 'cubicOut',
      animationDelay: function (idx: number) {
        return idx * 100
      },
      animationDuration: 1000,
    },
  ],
})

const range = ref([-3, 3])
const activeChart = ref('line') // Default to line chart (Time Series)

const safeRange = computed({
  get: () => range.value,
  set: ([min, max]) => {
    // Clamp min to [-10, -1]
    min = Math.max(-10, Math.min(min, -1))

    // Clamp max to [1, 10]
    max = Math.max(1, Math.min(max, 10))

    // Prevent invalid crossover
    if (min >= max) {
      // Reset to closest valid positions if they conflict
      min = -1
      max = 1
    }

    range.value = [min, max]
  },
})

const safeMin = computed({
  get: () => safeRange.value[0],
  set: (val) => {
    safeRange.value = [val || -1, safeRange.value[1]]
  },
})

const safeMax = computed({
  get: () => safeRange.value[1],
  set: (val) => {
    safeRange.value = [safeRange.value[0], val || 1]
  },
})

// ✅ Debounce utility
function debounce<T extends (...args: any[]) => void>(fn: T, delay: number): T {
  let timeout: ReturnType<typeof setTimeout>
  return ((...args: any[]) => {
    clearTimeout(timeout)
    timeout = setTimeout(() => fn(...args), delay)
  }) as T
}

// ✅ Debounced API call
const fetchGraphData = debounce(async ([min, max]: number[]) => {
  await getTimeTickersGraphDataRangeAndParseToGraph(min, max)
}, 200)

// ✅ Watch `safeRange` efficiently
watch(
  () => range.value.toString(), // Triggers only on actual [min, max] change
  () => {
    fetchGraphData([...range.value])
  },
  {
    immediate: true,
    flush: 'post',
  },
)
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
          <div class="range-controls">
            <div class="range-header">
              <div class="range-info">
                <span class="range-label">Range:</span>
                <span class="range-values">{{ safeRange[0] }} to {{ safeRange[1] }} days</span>
              </div>
              <div class="range-actions">
                <v-btn
                  icon
                  size="x-small"
                  variant="text"
                  color="primary"
                  @click="resetRange"
                  class="reset-btn"
                  density="compact"
                >
                  <v-icon size="x-small">mdi-refresh</v-icon>
                </v-btn>
              </div>
            </div>

            <div class="range-slider-container">
              <v-range-slider
                v-model="safeRange"
                :max="10"
                :min="-10"
                :step="1"
                hide-details
                density="compact"
                thumb-size="8"
                color="primary"
                class="range-slider"
              ></v-range-slider>
              <div class="range-labels">
                <span class="range-min">Past</span>
                <span class="range-max">Future</span>
              </div>
            </div>
          </div>

          <div class="chart-container">
            <v-chart
              class="chart-compact"
              :option="option"
              :key="`line-${JSON.stringify(option.series)}`"
              autoresize
            />
          </div>
        </div>

        <!-- Status Distribution Chart -->
        <div v-if="activeChart === 'pie'" class="chart-section">
          <div class="card-header">
            <h2 class="card-title">
              <v-icon class="title-icon" color="primary">mdi-chart-pie</v-icon>
              Status Distribution
            </h2>
            <p class="card-subtitle">Real-time ticker status breakdown</p>

            <!-- Action Buttons -->
            <div class="chart-actions">
              <v-btn
                icon
                size="small"
                variant="text"
                color="primary"
                @click="getTimeTickersGraphDataAndParseToGraph"
                :loading="chartLoading"
                class="refresh-chart-btn"
              >
                <v-icon size="small">mdi-refresh</v-icon>
              </v-btn>
            </div>
          </div>

          <div class="chart-container">
            <!-- Loading State -->
            <div v-if="chartLoading" class="chart-loading-state">
              <div class="loading-spinner">
                <v-progress-circular indeterminate color="primary" size="48"></v-progress-circular>
              </div>
              <p class="loading-text">Loading status data...</p>
            </div>

            <!-- Error State -->
            <div v-else-if="chartError" class="chart-error-state">
              <v-icon size="48" color="error">mdi-alert-circle</v-icon>
              <p class="error-text">Failed to load chart data</p>
              <v-btn
                size="small"
                color="primary"
                variant="outlined"
                @click="getTimeTickersGraphDataAndParseToGraph"
                prepend-icon="mdi-refresh"
              >
                Retry
              </v-btn>
            </div>

            <!-- Chart -->
            <v-chart
              v-else
              class="status-chart"
              :option="totalOption"
              :key="`pie-${JSON.stringify(totalOption.series[0].data)}-${Date.now()}`"
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

                <button
                  class="premium-action-btn secondary-action"
                  @click="openCreateBatchDialog()"
                >
                  <div class="btn-icon">
                    <v-icon size="18">mdi-link-plus</v-icon>
                  </div>
                  <span class="btn-text">Create Batch</span>
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
            :items-per-page="15"
            class="enhanced-table"
            :search="tableSearch"
          >
          
            <!-- Selection Column -->
            <template v-slot:item.selection="{ item }">
              <v-checkbox
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

                <!-- Indentation for Child Items -->
                <div v-else-if="item.isChild" class="ml-4"></div>

                <!-- Function Name with Hierarchy Indicators -->
                <div class="d-flex align-center">
                  <v-icon
                    v-if="item.isParent && getChildrenCount(item.id) > 0"
                    size="small"
                    class="mr-1"
                    color="primary"
                  >
                    mdi-folder-outline
                  </v-icon>
                  <v-icon v-else-if="item.isChild" size="small" class="mr-1" color="secondary">
                    mdi-subdirectory-arrow-right
                  </v-icon>
                  <v-icon v-else-if="item.isOrphan" size="small" class="mr-1" color="warning">
                    mdi-help-circle-outline
                  </v-icon>

                  <span class="function-name">{{ item.function }}</span>

                  <!-- Child Count Badge for Parents -->
                  <v-chip
                    v-if="item.isParent && getChildrenCount(item.id) > 0"
                    size="x-small"
                    color="primary"
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
                        @click="requestCancel(item.id)"
                        :disabled="!hasStatus(item.status, Status.InProgress)"
                        class="modern-action-btn cancel-btn"
                        :class="{ active: hasStatus(item.status, Status.InProgress) }"
                      >
                        <v-icon size="16">mdi-cancel</v-icon>
                      </button>
                    </template>
                    <span>Cancel Operation</span>
                  </v-tooltip>
                </div>

                <!-- Unbatch Button - Only for child items -->
                <div v-if="item.batchParent" class="action-btn-wrapper">
                  <v-tooltip location="top">
                    <template v-slot:activator="{ props }">
                      <button
                        v-bind="props"
                        @click="unbatchItem(item.id)"
                        :disabled="unbatchTicker.loader.value"
                        class="modern-action-btn unbatch-btn"
                        :class="{ 'loading': unbatchTicker.loader.value }"
                      >
                        <v-progress-circular
                          v-if="unbatchTicker.loader.value"
                          indeterminate
                          size="12"
                          width="2"
                          color="white"
                        ></v-progress-circular>
                        <v-icon v-else size="16">mdi-link-variant-off</v-icon>
                      </button>
                    </template>
                    <span>Unbatch from Parent</span>
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
                  :class="{ 'action-btn-disabled': hasStatus(item.status, Status.InProgress) }"
                >
                  <v-tooltip location="top">
                    <template v-slot:activator="{ props }">
                      <button
                        v-bind="props"
                        @click="confirmDialog.open({ id: item.id })"
                        :disabled="hasStatus(item.status, Status.InProgress)"
                        class="modern-action-btn delete-btn"
                        :class="{ active: !hasStatus(item.status, Status.InProgress) }"
                      >
                        <v-icon size="16">mdi-trash-can</v-icon>
                      </button>
                    </template>
                    <span>Delete Ticker</span>
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

    <!-- Simple Batch Operation Dialog -->
    <v-dialog v-model="batchOperationDialog.isOpen" max-width="450" persistent>
      <v-card density="compact">
        <v-card-title class="d-flex align-center pa-3">
          <v-icon class="mr-2" color="secondary">mdi-format-list-bulleted</v-icon>
          Create Batch
        </v-card-title>

        <v-card-text class="pa-3">
          <p class="text-body-2 mb-3">
            Create batch with
            <strong>{{ batchOperationDialog.selectedItems.length }} selected items</strong>
          </p>

          <v-select
            v-model="batchOperationDialog.batchRunCondition"
            label="When to run children"
            :items="[
              { title: 'When parent completes (any status)', value: 0 },
              { title: 'Only when parent succeeds', value: 1 },
            ]"
            variant="outlined"
            density="compact"
            class="mb-3"
          ></v-select>

          <div class="text-caption text-grey-darken-1 pa-2 bg-grey-lighten-4 rounded">
            <v-icon size="small" class="mr-1">mdi-information</v-icon>
            First item becomes parent, others run when it finishes
          </div>
        </v-card-text>

        <v-card-actions class="pa-3">
          <v-spacer></v-spacer>
          <v-btn variant="text" @click="handleBatchOperationCancel">Cancel</v-btn>
          <v-btn color="primary" variant="elevated" @click="handleBatchOperationConfirm"
            >Create Batch</v-btn
          >
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Step-by-Step Create Batch Dialog -->
    <v-dialog v-model="createBatchDialog.isOpen" max-width="600" persistent>
      <v-card density="compact">
        <v-card-title class="d-flex align-center pa-3">
          <v-icon class="mr-2" color="primary">mdi-link-plus</v-icon>
          Create Batch - Step {{ createBatchDialog.step }} of 2
        </v-card-title>

        <!-- Step 1: Select Parent -->
        <v-card-text v-if="createBatchDialog.step === 1" class="pa-3">
          <h3 class="text-h6 mb-3">Step 1: Select Parent Item</h3>
          <p class="text-body-2 mb-3">Choose which item will be the parent of the batch</p>

          <v-select
            v-model="createBatchDialog.selectedParentId"
            label="Select Parent Item"
            :items="availableParents"
            variant="outlined"
            density="compact"
            class="mb-3"
            hint="Only Idle and Queued items can be parents"
            persistent-hint
          >
            <template v-slot:item="{ props, item }">
              <v-list-item v-bind="props" :subtitle="item.raw.subtitle">
                <template v-slot:prepend>
                  <v-icon color="primary" size="small">mdi-account-supervisor</v-icon>
                </template>
              </v-list-item>
            </template>
          </v-select>

          <div class="text-caption text-grey-darken-1 pa-2 bg-grey-lighten-4 rounded">
            <v-icon size="small" class="mr-1">mdi-information</v-icon>
            The parent item controls when children items will execute
          </div>
        </v-card-text>

        <!-- Step 2: Select Children -->
        <v-card-text v-if="createBatchDialog.step === 2" class="pa-3">
          <h3 class="text-h6 mb-3">Step 2: Select Children Items</h3>
          <p class="text-body-2 mb-3">
            Choose which items will be children of
            <strong>{{ findItemById(createBatchDialog.selectedParentId)?.function }}</strong>
          </p>

          <v-select
            v-model="createBatchDialog.selectedChildrenIds"
            label="Select Children Items"
            :items="availableChildren"
            variant="outlined"
            density="compact"
            class="mb-3"
            multiple
            chips
            closable-chips
            hint="Children must have execution time >= parent time"
            persistent-hint
          >
            <template v-slot:item="{ props, item }">
              <v-list-item v-bind="props" :subtitle="item.raw.subtitle">
                <template v-slot:prepend>
                  <v-icon color="secondary" size="small">mdi-subdirectory-arrow-right</v-icon>
                </template>
              </v-list-item>
            </template>
          </v-select>

          <v-select
            v-model="createBatchDialog.batchRunCondition"
            label="When to run children"
            :items="[
              { title: 'When parent completes (any status)', value: 0 },
              { title: 'Only when parent succeeds', value: 1 },
            ]"
            variant="outlined"
            density="compact"
            class="mb-3"
          ></v-select>

          <div class="text-caption text-grey-darken-1 pa-2 bg-grey-lighten-4 rounded">
            <v-icon size="small" class="mr-1">mdi-information</v-icon>
            Selected items will run after the parent completes based on the condition above
          </div>
        </v-card-text>

        <v-card-actions class="pa-3">
          <v-btn v-if="createBatchDialog.step === 2" variant="text" @click="previousStep">
            Back
          </v-btn>
          <v-spacer></v-spacer>
          <v-btn variant="text" @click="handleCreateBatchCancel">Cancel</v-btn>
          <v-btn
            v-if="createBatchDialog.step === 1"
            color="primary"
            variant="elevated"
            :disabled="!createBatchDialog.selectedParentId"
            @click="nextStep"
          >
            Next
          </v-btn>
          <v-btn
            v-if="createBatchDialog.step === 2"
            color="primary"
            variant="elevated"
            :disabled="createBatchDialog.selectedChildrenIds.length === 0"
            @click="handleCreateBatchConfirm"
          >
            Create Batch
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
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
.success-metric::before {
  background: linear-gradient(90deg, #10b981, #059669);
}
.warning-metric::before {
  background: linear-gradient(90deg, #f59e0b, #d97706);
}
.error-metric::before {
  background: linear-gradient(90deg, #ef4444, #dc2626);
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
.success-text {
  color: #4caf50;
}
.warning-text {
  color: #ffb74d;
}
.error-text {
  color: #f44336;
}

/* Analytics Overview Card */
.analytics-overview {
  background: rgba(66, 66, 66, 0.9);
  backdrop-filter: blur(20px);
  border-radius: 12px;
  padding: 20px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.4);
  border: 1px solid rgba(255, 255, 255, 0.1);
  transition: all 0.3s ease;
  margin-bottom: 20px;
  position: relative;
}

.analytics-overview:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 25px rgba(0, 0, 0, 0.6);
}

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

.toggle-label {
  color: #ffffff;
  font-size: 0.7rem;
  font-weight: 600;
  text-align: center;
  padding: 4px 0;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.5);
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  margin-bottom: 4px;
}

.chart-btn-vertical {
  min-width: 56px;
  min-height: 64px;
  font-weight: 600;
  text-transform: none;
  letter-spacing: 0.5px;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  border-radius: 12px;
  padding: 10px 6px;
  font-size: 0.75rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 6px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.1);
  margin: 0;
}

.chart-btn-vertical:hover {
  transform: translateX(-3px) scale(1.05);
  background: rgba(100, 181, 246, 0.2);
  border-color: rgba(100, 181, 246, 0.6);
  box-shadow: 0 8px 25px rgba(100, 181, 246, 0.3);
}

.chart-btn-vertical.v-btn--active,
.chart-btn-vertical.v-btn--color-primary {
  background: linear-gradient(135deg, rgba(100, 181, 246, 0.3) 0%, rgba(100, 181, 246, 0.15) 100%);
  border-color: rgba(100, 181, 246, 0.8);
  box-shadow: 0 4px 20px rgba(100, 181, 246, 0.4);
  transform: translateX(-2px);
}

.btn-text {
  font-size: 0.7rem;
  line-height: 1;
  font-weight: 600;
  color: #ffffff;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.3);
}

.chart-icon {
  color: #64b5f6;
  filter: drop-shadow(0 2px 4px rgba(100, 181, 246, 0.3));
  transition: all 0.3s ease;
}

.chart-btn-vertical:hover .chart-icon {
  color: #ffffff;
  filter: drop-shadow(0 4px 8px rgba(100, 181, 246, 0.5));
  transform: scale(1.1);
}

.chart-btn-vertical.v-btn--active .chart-icon {
  color: #ffffff;
  filter: drop-shadow(0 2px 6px rgba(100, 181, 246, 0.6));
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

.chart-section .card-header {
  margin-bottom: 12px;
  width: 100%;
  padding: 0;
}

.chart-section .chart-container {
  height: 250px;
  width: 100%;
  max-width: 100%;
  margin: 0;
  position: relative;
  overflow: hidden;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.02);
  border: 1px solid rgba(255, 255, 255, 0.05);
}

/* Chart Actions */
.chart-actions {
  position: absolute;
  top: 8px;
  right: 8px;
  display: flex;
  gap: 4px;
}

.refresh-chart-btn {
  background: rgba(255, 255, 255, 0.05);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.refresh-chart-btn:hover {
  background: rgba(100, 181, 246, 0.15);
  border-color: rgba(100, 181, 246, 0.3);
}

/* Enhanced Chart Styling */
.status-chart {
  height: 100%;
  width: 100%;
  opacity: 0;
  animation: chartFadeIn 0.6s ease-out forwards;
}

@keyframes chartFadeIn {
  from {
    opacity: 0;
    transform: scale(0.95);
  }
  to {
    opacity: 1;
    transform: scale(1);
  }
}

/* Loading State */
.chart-loading-state {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 16px;
  background: rgba(255, 255, 255, 0.02);
  backdrop-filter: blur(10px);
}

.loading-spinner {
  position: relative;
}

.loading-spinner::after {
  content: '';
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  width: 60px;
  height: 60px;
  border: 2px solid rgba(100, 181, 246, 0.2);
  border-radius: 50%;
  border-top-color: rgba(100, 181, 246, 0.6);
  animation: spin 1s linear infinite;
}

@keyframes spin {
  to {
    transform: translate(-50%, -50%) rotate(360deg);
  }
}

.loading-text {
  color: #64b5f6;
  font-size: 14px;
  font-weight: 500;
  margin: 0;
  animation: pulse 1.5s ease-in-out infinite;
}

@keyframes pulse {
  0%,
  100% {
    opacity: 0.7;
  }
  50% {
    opacity: 1;
  }
}

/* Error State */
.chart-error-state {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 16px;
  background: rgba(244, 67, 54, 0.05);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(244, 67, 54, 0.2);
  border-radius: 12px;
}

.error-text {
  color: #f44336;
  font-size: 14px;
  font-weight: 500;
  margin: 0;
  text-align: center;
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

.content-card {
  background: rgba(66, 66, 66, 0.9);
  backdrop-filter: blur(20px);
  border-radius: 12px;
  padding: 20px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.4);
  border: 1px solid rgba(255, 255, 255, 0.1);
  transition: all 0.3s ease;
}

.card-header {
  margin-bottom: 16px;
}

.card-title {
  font-size: 1.25rem;
  font-weight: 700;
  color: #ffffff;
  margin: 0 0 12px 0;
  display: flex;
  align-items: center;
  gap: 12px;
  justify-content: center;
  text-shadow: 0 2px 4px rgba(0, 0, 0, 0.3);
}

.title-icon {
  background: linear-gradient(135deg, rgba(100, 181, 246, 0.3) 0%, rgba(100, 181, 246, 0.1) 100%);
  border-radius: 10px;
  padding: 10px;
  box-shadow: 0 2px 8px rgba(100, 181, 246, 0.2);
}

.card-subtitle {
  font-size: 0.9rem;
  color: #bdbdbd;
  margin: 0;
  text-align: center;
  font-weight: 500;
  opacity: 0.9;
}

.chart-container {
  height: 250px;
  min-height: 200px;
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;
  overflow: hidden;
}

.chart-compact {
  height: 100%;
  width: 100%;
  max-width: 100%;
}

.chart-placeholder {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  text-align: center;
  color: #bdbdbd;
}

.chart-placeholder p {
  margin: 8px 0 0 0;
  font-size: 14px;
}

/* Range Controls */
.range-controls {
  background: rgba(0, 0, 0, 0.2);
  border-radius: 6px;
  padding: 4px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  margin: 0 0 6px 0;
  width: 100%;
}

.range-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 4px;
  width: 100%;
}

.range-info {
  display: flex;
  align-items: center;
  gap: 4px;
}

.range-label {
  font-size: 0.75rem;
  color: #bdbdbd;
  font-weight: 500;
  white-space: nowrap;
}

.range-values {
  font-size: 0.75rem;
  color: #ffffff;
  font-weight: 600;
  background: rgba(100, 181, 246, 0.2);
  padding: 1px 4px;
  border-radius: 3px;
  border: 1px solid rgba(100, 181, 246, 0.3);
}

.range-actions {
  display: flex;
  align-items: center;
}

.range-slider-container {
  position: relative;
  padding: 0 4px;
}

.range-labels {
  display: flex;
  justify-content: space-between;
  margin-top: 2px;
  font-size: 0.7rem;
  color: #bdbdbd;
}

.range-min {
  color: #ff9800;
}

.range-max {
  color: #4caf50;
}

.reset-btn {
  color: #64b5f6;
}

.range-slider {
  width: 100%;
  margin: 0;
}

:deep(.range-slider .v-slider) {
  margin: 0;
  padding: 0;
}

:deep(.range-slider .v-slider__track) {
  height: 4px;
}

:deep(.range-slider .v-slider__thumb) {
  width: 12px;
  height: 12px;
}

.range-input {
  background: rgba(255, 255, 255, 0.05);
  border-radius: 8px;
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
  color: #ffb74d;
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
  border: 1px solid rgba(255, 255, 255, 0.15);
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.3);
}

/* Enhanced table styles */
.enhanced-table {
  border-radius: 8px;
  overflow: hidden;
  background: transparent;
}

:deep(.enhanced-table .v-data-table__td) {
  padding: 8px 12px;
}

:deep(.enhanced-table .v-data-table-header__td) {
  padding: 10px 12px;
}

:deep(.enhanced-table .v-data-table__wrapper) {
  border-radius: 12px;
  background: transparent;
}

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
  min-height: 40px;
}

:deep(.enhanced-table .v-data-table__tr::after) {
  content: '';
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  height: 1px;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.08), transparent);
}

:deep(.enhanced-table .v-data-table__tr:nth-child(even)) {
  background: rgba(255, 255, 255, 0.02);
}

/* Status chip styles */
.status-chip {
  transition: all 0.2s ease;
  cursor: pointer;
  font-weight: 600;
  letter-spacing: 0.3px;
}

/* Selection styles */
.selection-checkbox {
  margin: 0;
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
.parent-row {
  border-left: 4px solid #64b5f6;
  background: linear-gradient(135deg, rgba(100, 181, 246, 0.05) 0%, rgba(100, 181, 246, 0.02) 100%);
}

.child-row {
  border-left: 4px solid #ffb74d;
  background: linear-gradient(135deg, rgba(255, 183, 77, 0.05) 0%, rgba(255, 183, 77, 0.02) 100%);
  position: relative;
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

.orphan-row {
  border-left: 4px solid #ff5722;
  background: linear-gradient(135deg, rgba(255, 87, 34, 0.05) 0%, rgba(255, 87, 34, 0.02) 100%);
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
  .chart-toggle-vertical {
    top: 16px;
    left: -52px;
  }

  .chart-btn-vertical {
    min-width: 50px;
    min-height: 60px;
    font-size: 0.7rem;
  }

  .btn-text {
    font-size: 0.65rem;
  }

  .chart-section .chart-container {
    height: 220px;
    max-width: 100%;
  }

  .card-title {
    font-size: 1.125rem;
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

  .chart-section .chart-container {
    height: 200px;
  }

  .card-title {
    font-size: 1rem;
    gap: 8px;
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
