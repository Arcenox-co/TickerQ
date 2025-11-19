<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, provide, ref, watch, type Ref } from 'vue'
import { cronTickerService } from '@/http/services/cronTickerService'
import { Status } from '@/http/services/types/base/baseHttpResponse.types'
import { useDialog } from '@/composables/useDialog'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
import PaginationFooter from '@/components/PaginationFooter.vue'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart, PieChart } from 'echarts/charts'
import { sleep } from '@/utilities/sleep'
import {
  GetCronTickerGraphDataRangeResponse,
  type GetCronTickerResponse,
} from '@/http/services/types/cronTickerService.types'
import TickerNotificationHub, { methodName } from '@/hub/tickerNotificationHub'
import { useConnectionStore } from '@/stores/connectionStore'
import { useTimeZoneStore } from '@/stores/timeZoneStore'
import {
  TitleComponent,
  TooltipComponent,
  LegendComponent,
  ToolboxComponent,
  GridComponent,
} from 'echarts/components'
import VChart, { THEME_KEY } from 'vue-echarts'
import cronstrue from 'cronstrue'

// Helper function to get readable cron expression
const getReadableCronExpression = (expression: string): string => {
  try {
    // cronstrue expects 6-part format with seconds
    return cronstrue.toString(expression)
  } catch (error) {
    return 'Invalid cron expression'
  }
}

const getCronTickerRangeGraphData = cronTickerService.getTimeTickersGraphDataRange()
const getCronTickersPaginated = cronTickerService.getCronTickersPaginated()
const getCronTickerRangeGraphDataById = cronTickerService.getTimeTickersGraphDataRangeById()
const getCronTickersGraphDataAndParseToGraph = cronTickerService.getTimeTickersGraphData()
const deleteCronTicker = cronTickerService.deleteCronTicker()
const runCronTickerOnDemand = cronTickerService.runCronTickerOnDemand()

// Pagination state
const currentPage = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)

// Load page data with pagination
const loadPageData = async () => {
  try {
    const response = await getCronTickersPaginated.requestAsync(currentPage.value, pageSize.value)
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

const confirmDialog = useDialog<ConfirmDialogProps & { id: string }>().withComponent(
  () => import('@/components/common/ConfirmDialog.vue'),
)
const cronOccurrenceDialog = useDialog<{
  id: string
  retries: number
  retryIntervals: string[]
}>().withComponent(() => import('@/components/crontickerComponents/CronOccurrenceDialog.vue'))

const crudCronTickerDialog = useDialog<
  GetCronTickerResponse & { isFromDuplicate: boolean }
>().withComponent(() => import('@/components/crontickerComponents/CRUDCronTickerDialog.vue'))

const crudCronTickerDialogRef = ref(null)

const selectedCronTickerGraphData: Ref<string | undefined> = ref(undefined)
const chartLoading = ref(false)
const isMounted = ref(false)

const timeZoneStore = useTimeZoneStore()

const onSubmitConfirmDialog = async () => {
  try {
    const deletedId = confirmDialog.propData?.id!
    confirmDialog.close()
    
    // Immediately remove from UI for better UX
    // Reload page after deletion
    
    // Perform the actual deletion
    await deleteCronTicker.requestAsync(deletedId)
    
    // Update charts to reflect the deletion
    await updateChartsAfterDeletion(deletedId)
    
  } catch (error: any) {
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
      return
    }
    // If deletion failed, we might want to refresh the data to restore the item
    // But for now, we'll let the notification system handle it
  }
}

// Helper function to update charts after any data change (create, update, delete)
const updateChartsAfterDataChange = async (changedId?: string) => {
  try {
    // Update pie chart with overall statistics
    await getTimeTickersGraphDataAndParseToGraph()
    
    // If we have a selected ticker and it's the one that was changed, update its specific chart
    if (selectedCronTickerGraphData.value && changedId && selectedCronTickerGraphData.value === changedId) {
      // Update the specific ticker's chart
      const res = await getCronTickerRangeGraphDataById.requestAsync(selectedCronTickerGraphData.value, -3, 3)
      GetCronTickerRangeGraphData(res)
      await updatePieChartForSelectedTicker(selectedCronTickerGraphData.value, -3, 3)
    } else if (selectedCronTickerGraphData.value) {
      // If another ticker is selected, update its specific chart
      const res = await getCronTickerRangeGraphDataById.requestAsync(selectedCronTickerGraphData.value, -3, 3)
      GetCronTickerRangeGraphData(res)
      await updatePieChartForSelectedTicker(selectedCronTickerGraphData.value, -3, 3)
    } else {
      // No specific ticker selected, update overall chart
      chartData.value.title = 'Job statuses for all Cron Tickers'
      const res = await getCronTickerRangeGraphData.requestAsync(-3, 3)
      GetCronTickerRangeGraphData(res)
    }
  } catch (error: any) {
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
      return
    }
    console.error('Error updating charts after data change:', error)
  }
}

// Helper function to update charts after deletion (specific case)
const updateChartsAfterDeletion = async (deletedId: string) => {
  try {
    // Update pie chart with overall statistics
    await getTimeTickersGraphDataAndParseToGraph()
    
    // If we have a selected ticker and it's the one that was deleted, reset to overall view
    if (selectedCronTickerGraphData.value === deletedId) {
      selectedCronTickerGraphData.value = undefined
      chartData.value.title = 'Job statuses for all Cron Tickers'
      
      // Update the main chart with overall data
      const res = await getCronTickerRangeGraphData.requestAsync(-3, 3)
      GetCronTickerRangeGraphData(res)
    } else if (selectedCronTickerGraphData.value) {
      // If another ticker is selected, update its specific chart
      const res = await getCronTickerRangeGraphDataById.requestAsync(selectedCronTickerGraphData.value, -3, 3)
      GetCronTickerRangeGraphData(res)
      await updatePieChartForSelectedTicker(selectedCronTickerGraphData.value, -3, 3)
    } else {
      // No specific ticker selected, update overall chart
      chartData.value.title = 'Job statuses for all Cron Tickers'
      const res = await getCronTickerRangeGraphData.requestAsync(-3, 3)
      GetCronTickerRangeGraphData(res)
    }
  } catch (error: any) {
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
      return
    }
    console.error('Error updating charts after deletion:', error)
  }
}

const getTimeTickersGraphDataAndParseToGraph = async () => {
  try {
    const res = await getCronTickersGraphDataAndParseToGraph.requestAsync()
    
    if (!res || !Array.isArray(res)) {
      pieChartData.value = [{ value: 1, name: 'No Data Available', itemStyle: { color: '#9e9e9e' } }]
      return
    }
    
    const chartData = res
      .filter(item => item && item.item1 !== undefined && item.item2 !== undefined)
      .sort((a, b) => a.item2 - b.item2)
      .map((item) => {
        const statusName = Status[item.item1] || `Status ${item.item1}`
        const color = statusColors[item.item1] || '#999999' // fallback color
        return {
          name: `${statusName} (${item.item2})`,
          value: item.item2,
          itemStyle: {
            color: color
          }
        }
      })

    // Update pieChartData to trigger reactivity
    pieChartData.value = chartData
    
    // Force pie chart re-render by updating the key
    pieChartKey.value++
    
  } catch (error: any) {
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
      return
    }
  }
}

const updatePieChartForSelectedTicker = async (tickerId: string, min: number, max: number) => {
  try {
    // Get the specific ticker's data for the selected range
    const res = await getCronTickerRangeGraphDataById.requestAsync(tickerId, min, max)
    
    if (!res || !Array.isArray(res)) {
      pieChartData.value = [{ value: 1, name: 'No Data Available', itemStyle: { color: '#9e9e9e' } }]
      return
    }
    
    // Process the data to create status distribution
    const statusCounts = new Map<number, number>()
    
    // Count occurrences of each status in the selected range
    res.forEach(({ results }) => {
      if (results && Array.isArray(results)) {
        results.forEach(({ item1, item2 }) => {
          const currentCount = statusCounts.get(item1) || 0
          statusCounts.set(item1, currentCount + item2)
        })
      }
    })
    
    // Convert to pie chart format
    const chartData = Array.from(statusCounts.entries())
      .filter(([_, count]) => count > 0) // Only show statuses with data
      .sort(([_, a], [__, b]) => b - a) // Sort by count descending
      .map(([statusId, count]) => {
        const statusName = Status[statusId] || `Status ${statusId}`
        const color = statusColors[statusId] || '#999999'
        return {
          name: `${statusName} (${count})`,
          value: count,
          itemStyle: {
            color: color
          }
        }
      })
    
    // If no data, show a message
    if (chartData.length === 0) {
      chartData.push({
        name: 'No data in selected range',
        value: 1,
        itemStyle: { color: '#999' }
      })
    }
    
    // Update pieChartData to trigger reactivity
    pieChartData.value = chartData
    
    // Force pie chart re-render by updating the key
    pieChartKey.value++
    
  } catch (error: any) {
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
      return
    }
  }
}

const GetCronTickerRangeGraphData = (res: GetCronTickerGraphDataRangeResponse[]) => {
  try {
    if (!res || !Array.isArray(res)) {
      return
    }
    
    // Extract unique Dates for xAxis
    const uniqueDates = res.map((x) => x.date)

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
        color: statusColors[item1] || '#999999',
        width: 3,
        shadowBlur: 10,
        shadowColor: statusColors[item1] || '#999999',
        shadowOffsetY: 2,
      },
      itemStyle: {
        color: statusColors[item1] || '#999999',
        borderColor: '#ffffff',
        borderWidth: 2,
        shadowBlur: 10,
        shadowColor: 'rgba(0, 0, 0, 0.3)',
        shadowOffsetY: 2,
      },
      areaStyle: {
        color: {
          type: 'linear',
          x: 0,
          y: 0,
          x2: 0,
          y2: 1,
          colorStops: [
            { offset: 0, color: (statusColors[item1] || '#999999') + '40' },
            { offset: 1, color: (statusColors[item1] || '#999999') + '10' },
          ],
        },
      },
      emphasis: {
        itemStyle: {
          shadowBlur: 20,
          shadowColor: statusColors[item1] || '#999999',
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
    ]

    const seriesNames = composedData.filter((x) => x.data.some((y) => y > 0)).map((x) => x.name)

    const legendData = {
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

    // Update chartData to trigger reactivity
    chartData.value = {
      xAxisData: uniqueDates,
      series: composedData,
      legend: legendData,
      title: chartData.value.title // Keep current title
    }
    
    // Force chart re-render by updating the key
    chartKey.value++
    
  } catch (error: any) {
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
      return
    }
  }
}

const ShowCronTickerOccurrenceGraphData = async (functionName: string, id: string, min: number, max: number) => {
  try {
    chartLoading.value = true
    
    if (selectedCronTickerGraphData.value === id) {
      // Deselect current ticker and show all data
      selectedCronTickerGraphData.value = undefined
      chartData.value.title = 'Job statuses for all Cron Tickers'
      
      try {
        const res = await getCronTickerRangeGraphData.requestAsync(min, max)
        GetCronTickerRangeGraphData(res)
        
        // Refresh pie chart with all tickers data
        await getTimeTickersGraphDataAndParseToGraph()
      } catch (error: any) {
        if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
          return
        }
      }
    } else {
      // Select specific ticker and show its data
      selectedCronTickerGraphData.value = id
      chartData.value.title = `Job statuses for ${functionName}`
      
      try {
        const res = await getCronTickerRangeGraphDataById.requestAsync(id, min, max)
        GetCronTickerRangeGraphData(res)
        
        // Update pie chart with selected ticker's data
        await updatePieChartForSelectedTicker(id, min, max)
      } catch (error: any) {
        if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
          return
        }
      }
    }
    
    // Force chart re-render
    await nextTick()
    
    // Additional delay to ensure chart updates
    setTimeout(() => {
    }, 100)
    
  } catch (error: any) {
  } finally {
    chartLoading.value = false
  }
}

const RunCronTickerOnDemand = async (id: string) => {
  await runCronTickerOnDemand.requestAsync(id)
  await sleep(1000)
  await loadPageData()
}

onMounted(async () => {
  try {
    isMounted.value = true
    // Ensure WebSocket connection is established (only if not already initialized)
    try {
      const connectionStore = useConnectionStore()
      if (!connectionStore.isInitialized) {
        await connectionStore.initializeConnectionWithRetry()
      }
    } 
    catch (error: any) {}
    
    // Check if still mounted before continuing
    if (!isMounted.value) return
    
    // Load cron tickers data
    try {
      await loadPageData()
    } catch (error: any) {
    }
    
    // Check if still mounted before continuing
    if (!isMounted.value) return
    
    // Load graph data range
    try {
      const res = await getCronTickerRangeGraphData.requestAsync(-3, 3)
      GetCronTickerRangeGraphData(res)
    } catch (error: any) {
      if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
        return
      }
    }
    
    // Check if still mounted before continuing
    if (!isMounted.value) return
    
    // Load status distribution data
    try {
      await getTimeTickersGraphDataAndParseToGraph()
    } catch (error: any) {
    }
    
    // Check if still mounted before continuing
    if (!isMounted.value) return
    
    // Add hub listeners
    try {
      await addHubListeners()
    } catch (error: any) {
    }
    
  } catch (error: any) {
  }
})

// Reload data when display timezone changes
watch(
  () => timeZoneStore.effectiveTimeZone,
  async () => {
    try {
      if (!isMounted.value) return

      await loadPageData()

      // Reload main range chart and pie distribution
      const range = await getCronTickerRangeGraphData.requestAsync(-3, 3)
      GetCronTickerRangeGraphData(range)
      await getTimeTickersGraphDataAndParseToGraph()

      // If a specific ticker is selected, update its view
      if (selectedCronTickerGraphData.value) {
        const res = await getCronTickerRangeGraphDataById.requestAsync(
          selectedCronTickerGraphData.value,
          -3,
          3
        )
        GetCronTickerRangeGraphData(res)
        await updatePieChartForSelectedTicker(selectedCronTickerGraphData.value, -3, 3)
      }
    } catch {
      // ignore errors on timezone-driven refresh
    }
  }
)

onUnmounted(() => {
  isMounted.value = false
  
  TickerNotificationHub.stopReceiver(methodName.onReceiveAddCronTicker)
  TickerNotificationHub.stopReceiver(methodName.onReceiveUpdateCronTicker)
  TickerNotificationHub.stopReceiver(methodName.onReceiveDeleteCronTicker)
})

const addHubListeners = async () => {
  TickerNotificationHub.onReceiveAddCronTicker<GetCronTickerResponse>((response) => {
    // Reload current page when new item is added
    loadPageData()
    
    // Update charts to reflect the new ticker
    updateChartsAfterDataChange(response.id)
  })

  TickerNotificationHub.onReceiveUpdateCronTicker<GetCronTickerResponse>((response) => {
    // For paginated data, we need to refresh the current page
    loadPageData()
    
    // Update charts to reflect the updated ticker
    updateChartsAfterDataChange(response.id)
  })

  TickerNotificationHub.onReceiveDeleteCronTicker<string>((id) => {
    // Reload current page when item is deleted
    loadPageData()
    
    // Force a reactive update by triggering a re-render
    nextTick(() => {
      // Update UI after deletion
    })
    
    // Update charts to reflect the deletion
    updateChartsAfterDeletion(id)
  })
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

const chartData = ref({
  xAxisData: [] as string[],
  series: [] as any[],
  legend: {} as any,
  title: 'Job statuses for all Cron Tickers'
})

const pieChartData = ref([{ value: 1, name: 'Loading...', itemStyle: { color: '#64b5f6' } }])

const chartKey = ref(0)
const pieChartKey = ref(0)

const option = computed(() => ({
  backgroundColor: 'transparent',
  title: {
    text: 'Time Series Analysis',
    subtext: chartData.value.title,
    left: 'center',
    top: '2%',
    textStyle: {
      color: '#ffffff',
      fontSize: 14,
      fontWeight: 'bold',
      textShadow: '0 2px 4px rgba(0,0,0,0.3)',
    },
    subtextStyle: {
      color: '#bdbdbd',
      fontSize: 12,
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
  legend: chartData.value.legend,
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
    data: chartData.value.xAxisData,
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
  series: chartData.value.series,
  animation: true,
  animationDuration: 1000,
  animationEasing: 'cubicOut' as const,
}))

const totalOption = computed(() => ({
  backgroundColor: 'transparent',
  title: {
    text: selectedCronTickerGraphData.value 
      ? 'Status Distribution - Selected Ticker'
      : 'Status Distribution - All Tickers',
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
      data: pieChartData.value,
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
}))

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

const range = ref([-3, 3])

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
  try {
    if (selectedCronTickerGraphData.value == undefined) {
      const res = await getCronTickerRangeGraphData.requestAsync(min, max)
      GetCronTickerRangeGraphData(res)
    } else {
      const res = await getCronTickerRangeGraphDataById.requestAsync(selectedCronTickerGraphData.value!, min, max)
      GetCronTickerRangeGraphData(res)
      
      // Also update the pie chart for the selected ticker
      await updatePieChartForSelectedTicker(selectedCronTickerGraphData.value!, min, max)
    }
  } catch (error: any) {
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
      return
    }
  }
}, 100) // You can tweak delay to 200ms+ for inputs

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

// Debug watcher for chart data changes
watch(
  () => chartData.value,
  (newData) => {
  },
  { deep: true }
)

// Debug watcher for pie chart data changes
watch(
  () => pieChartData.value,
  (newData) => {
  },
  { deep: true }
)

const headersWithoutReadable = computed(() => {
  // Define headers manually for paginated response
  return [
    { title: 'Function', key: 'function', sortable: true, visibility: true },
    { title: 'Expression', key: 'expression', sortable: true, visibility: true },
    { title: 'Request Type', key: 'requestType', sortable: false, visibility: true },
    { title: 'Description', key: 'description', sortable: true, visibility: true },
    { title: 'Retry Configuration', key: 'retryIntervals', sortable: false, visibility: true },
    { title: 'Init Identifier', key: 'initIdentifier', sortable: false, visibility: true },
    { title: 'Created At', key: 'createdAt', sortable: true, visibility: true },
    { title: 'Updated At', key: 'updatedAt', sortable: true, visibility: true },
    { title: 'Actions', key: 'actions', sortable: false, visibility: true },
  ]
})

// Chart toggle state
const activeChart = ref('line') // Default to line chart (Time Series Analysis)

// Helper functions for enhanced functionality
const resetRange = () => {
  range.value = [-3, 3]
}

const refreshData = async () => {
  try {
    await loadPageData()
    // Reset to all tickers view when refreshing
    selectedCronTickerGraphData.value = undefined
    chartData.value.title = 'Job statuses for all Cron Tickers'
    
    // Update charts using the helper function
    await updateChartsAfterDataChange()
  } catch (error: any) {
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED') {
      return
    }
  }
}
</script> 

<template>
  <div class="cron-ticker-dashboard">
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
              :key="`chart-${chartKey}`"
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
            <p class="card-subtitle">Real-time cron ticker status breakdown</p>

            <!-- Action Buttons -->
            <div class="chart-actions">
              <v-btn
                icon
                size="small"
                variant="text"
                color="primary"
                @click="getTimeTickersGraphDataAndParseToGraph"
                class="refresh-chart-btn"
              >
                <v-icon size="small">mdi-refresh</v-icon>
              </v-btn>
            </div>
          </div>

          <div class="chart-container">
            <v-chart
              class="status-chart"
              :option="totalOption"
              :key="`pie-chart-${pieChartKey}`"
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
            Cron Ticker Operations
          </h2>
          <div class="table-controls">
            <!-- Primary Actions Group -->
            <div class="primary-actions">
              <div class="action-group">
                <button
                  class="premium-action-btn primary-action"
                  @click="
                    crudCronTickerDialog.open({
                      ...({} as GetCronTickerResponse),
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
              </div>
            </div>

            <!-- Search and Info Group -->
            <div class="search-info-group">
              <v-chip
                :color="getCronTickersPaginated.loader.value ? 'warning' : 'success'"
                variant="tonal"
                size="small"
                class="status-chip"
              >
                <v-icon size="small" class="mr-1">
                  {{ getCronTickersPaginated.loader.value ? 'mdi-loading' : 'mdi-check' }}
                </v-icon>
                {{
                  getCronTickersPaginated.loader.value ? 'Loading...' : `${totalCount} total items`
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
            </div>
          </div>
        </div>

        <div class="table-container">
          <v-data-table
            density="compact"
            :headers="headersWithoutReadable"
            :loading="getCronTickersPaginated.loader.value"
            :items="getCronTickersPaginated.response.value?.items || []"
            item-value="id"
            :items-per-page="-1"
            hide-default-footer
            class="enhanced-table"
          >
            <template v-slot:item.expression="{ item }">
              <v-tooltip location="top">
                <template #activator="{ props }">
                  <span v-bind="props" class="expression-tooltip">
                    {{ item.expression }}
                  </span>
                </template>
                <span>{{ getReadableCronExpression(item.expression) }}</span>
              </v-tooltip>
            </template>

            <template v-slot:item.retryIntervals="{ item }">
              <div class="retry-display" v-if="item.retryIntervals?.length || (item.retries && item.retries > 0)">
                <div class="retry-header" v-if="item.retries > 0">
                  <span class="retry-count-label">Max Retries: {{ item.retries }}</span>
                </div>
                <span class="retry-sequence" v-if="item.retryIntervals?.length">
                  <template v-for="(interval, index) in item.retryIntervals" :key="index">
                    <span class="retry-item">{{ index + 1 }}:{{ interval }}</span>
                  </template>
                </span>
                <span v-else class="no-intervals">(Default: 30s)</span>
              </div>
              <span v-else class="no-retries">—</span>
            </template>

            <template v-slot:item.actions="{ item }">
              <div class="action-buttons-container">
                <!-- Chart Button -->
                <div class="action-btn-wrapper">
                  <v-tooltip location="top">
                    <template v-slot:activator="{ props }">
                      <button
                        v-bind="props"
                        @click="ShowCronTickerOccurrenceGraphData(item.function, item.id, -3, 3)"
                        class="modern-action-btn chart-btn"
                        :class="{ active: selectedCronTickerGraphData === item.id }"
                        :disabled="chartLoading"
                      >
                        <v-icon v-if="chartLoading" size="16" class="loading-spin">mdi-loading</v-icon>
                        <v-icon v-else size="16">mdi-chart-areaspline</v-icon>
                      </button>
                    </template>
                    <span>View Occurrences</span>
                  </v-tooltip>
                </div>

                <!-- Occurrences Button -->
                <div class="action-btn-wrapper">
                  <v-tooltip location="top">
                    <template v-slot:activator="{ props }">
                      <button
                        v-bind="props"
                        @click="
                          (event) => {
                            cronOccurrenceDialog.open({
                              id: item.id,
                              retries: item.retries,
                              retryIntervals: item.retryIntervals,
                            });
                          }
                        "
                        class="modern-action-btn occurrences-btn"
                      >
                        <v-icon size="16">mdi-folder-open</v-icon>
                      </button>
                    </template>
                    <span>View Occurrences</span>
                  </v-tooltip>
                </div>

                <!-- Edit Button -->
                <div class="action-btn-wrapper">
                  <v-tooltip location="top">
                    <template v-slot:activator="{ props }">
                      <button
                        v-bind="props"
                        @click="crudCronTickerDialog.open({ ...item, isFromDuplicate: false })"
                        class="modern-action-btn edit-btn"
                      >
                        <v-icon size="16">mdi-pencil</v-icon>
                      </button>
                    </template>
                    <span>Edit Ticker</span>
                  </v-tooltip>
                </div>

                <!-- Run Button -->
                <div class="action-btn-wrapper">
                  <v-tooltip location="top">
                    <template v-slot:activator="{ props }">
                      <button
                        v-bind="props"
                        @click="RunCronTickerOnDemand(item.id)"
                        class="modern-action-btn run-btn"
                      >
                        <v-icon size="16">mdi-play-outline</v-icon>
                      </button>
                    </template>
                    <span>Run Now</span>
                  </v-tooltip>
                </div>

                <!-- Delete Button -->
                <div class="action-btn-wrapper">
                  <v-tooltip location="top">
                    <template v-slot:activator="{ props }">
                      <button
                        v-bind="props"
                        @click="
                          confirmDialog.open({
                            ...new ConfirmDialogProps(),
                            id: item.id,
                            showWarningAlert: item.initIdentifier != undefined ? true : false,
                            warningAlertMessage:
                              'System-seeded ticker. To remove permanently, delete its cron expression from code.',
                          })
                        "
                        class="modern-action-btn delete-btn"
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

    <cronOccurrenceDialog.Component
      :ticker-notification-hub="TickerNotificationHub"
      :dialog-props="cronOccurrenceDialog.propData"
      @close="cronOccurrenceDialog.close()"
      :is-open="cronOccurrenceDialog.isOpen"
    />
    <crudCronTickerDialog.Component
      ref="crudCronTickerDialogRef"
      :dialog-props="crudCronTickerDialog.propData"
      @close="crudCronTickerDialog.close()"
      :is-open="crudCronTickerDialog.isOpen"
    />
    <confirmDialog.Component
      :is-open="confirmDialog.isOpen"
      :dialog-props="confirmDialog.propData"
      @close="confirmDialog.close()"
      @confirm="onSubmitConfirmDialog"
    />
  </div>
</template> 

<style scoped>
/* Dashboard Layout */
.cron-ticker-dashboard {
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
  height: 400px;
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
  height: 400px;
  min-height: 350px;
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

/* Button-specific colors */
.chart-btn {
  color: #64b5f6;
  border-color: rgba(100, 181, 246, 0.2);
}

.chart-btn.active {
  background: rgba(100, 181, 246, 0.15);
  color: #1976d2;
  border-color: rgba(25, 118, 210, 0.4);
  box-shadow: 0 0 20px rgba(25, 118, 210, 0.3);
}

.chart-btn:hover {
  border-color: rgba(100, 181, 246, 0.5);
  box-shadow: 0 8px 25px rgba(100, 181, 246, 0.4);
}

.loading-spin {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

.occurrences-btn {
  color: #64b5f6;
  border-color: rgba(100, 181, 246, 0.2);
}

.occurrences-btn:hover {
  border-color: rgba(100, 181, 246, 0.5);
  box-shadow: 0 8px 25px rgba(100, 181, 246, 0.4);
  background: rgba(100, 181, 246, 0.15);
}

.edit-btn {
  color: #ffb74d;
  border-color: rgba(255, 183, 77, 0.2);
}

.edit-btn:hover {
  border-color: rgba(255, 183, 77, 0.5);
  box-shadow: 0 8px 25px rgba(255, 183, 77, 0.4);
  background: rgba(255, 183, 77, 0.15);
}

.run-btn {
  color: #4caf50;
  border-color: rgba(76, 175, 80, 0.2);
}

.run-btn:hover {
  border-color: rgba(76, 175, 80, 0.5);
  box-shadow: 0 8px 25px rgba(76, 175, 80, 0.4);
  background: rgba(76, 175, 80, 0.15);
}

.delete-btn {
  color: #e57373;
  border-color: rgba(229, 115, 115, 0.2);
}

.delete-btn:hover {
  border-color: rgba(229, 115, 115, 0.5);
  box-shadow: 0 8px 25px rgba(229, 115, 115, 0.4);
  background: rgba(229, 115, 115, 0.15);
}

.expression-tooltip {
  cursor: help;
  text-decoration: underline;
  text-underline-offset: 2px;
  color: #64b5f6;
  transition: all 0.2s ease;
}

.expression-tooltip:hover {
  color: #90caf9;
  text-shadow: 0 0 8px rgba(100, 181, 246, 0.3);
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
  color: #4caf50;
  font-size: 0.8rem;
  padding: 2px 6px;
  background: rgba(76, 175, 80, 0.15);
  border-radius: 4px;
}

.retry-sequence {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: nowrap;
}

.retry-item {
  font-weight: 500;
  color: #4caf50;
  transition: color 0.2s ease;
  white-space: nowrap;
}

.no-retries {
  color: #6b7280;
  font-style: italic;
}

.no-intervals {
  color: #888;
  font-size: 0.7rem;
  font-style: italic;
}

/* Responsive Design */
@media (max-width: 1400px) {
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
    height: 350px;
    max-width: 100%;
  }

  .card-title {
    font-size: 1.125rem;
  }

  .dashboard-content {
    padding-left: 24px;
    padding-right: 24px;
  }
}

@media (max-width: 768px) {
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
    height: 300px;
  }

  .card-title {
    font-size: 1rem;
    gap: 8px;
  }

  .title-icon {
    padding: 8px;
    border-radius: 8px;
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

  .content-card {
    padding: 16px;
  }

  .table-section {
    padding: 16px;
  }
}
</style> 
