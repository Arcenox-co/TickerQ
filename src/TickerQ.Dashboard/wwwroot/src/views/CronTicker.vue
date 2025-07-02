<script lang="ts">
import { computed, nextTick, onMounted, onUnmounted, provide, ref, watch, type Ref } from 'vue'
import { cronTickerService } from '@/http/services/cronTickerService'
import { Status } from '@/http/services/types/base/baseHttpResponse.types'
import { useDialog } from '@/composables/useDialog'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart, PieChart } from 'echarts/charts'
import { sleep } from '@/utilities/sleep'
import {
  GetCronTickerGraphDataRangeResponse,
  type GetCronTickerResponse,
} from '@/http/services/types/cronTickerService.types'
import TickerNotificationHub, { methodName } from '@/hub/tickerNotificationHub'
import {
  TitleComponent,
  TooltipComponent,
  LegendComponent,
  ToolboxComponent,
  GridComponent,
} from 'echarts/components'
import VChart, { THEME_KEY } from 'vue-echarts'
</script>

<script setup lang="ts">
const getCronTickerRangeGraphData = cronTickerService.getTimeTickersGraphDataRange()
const getCronTickers = cronTickerService.getCronTickers()
const getCronTickerRangeGraphDataById = cronTickerService.getTimeTickersGraphDataRangeById()
const getCronTickersGraphDataAndParseToGraph = cronTickerService.getTimeTickersGraphData()
const deleteCronTicker = cronTickerService.deleteCronTicker()

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

const onSubmitConfirmDialog = async () => {
  confirmDialog.close()
  await deleteCronTicker.requestAsync(confirmDialog.propData?.id!).then(async () => {
    if (selectedCronTickerGraphData.value != undefined) {
      option.value.title.subtext = 'Job statuses for all Cron Tickers'
      await getCronTickerRangeGraphData
        .requestAsync(-3, 3)
        .then((res) => GetCronTickerRangeGraphData(res))
    }
  })
}

const getTimeTickersGraphDataAndParseToGraph = async () => {
  await getCronTickersGraphDataAndParseToGraph.requestAsync().then((res) => {
    const chartData = res
      .sort((a, b) => a.item2 - b.item2)
      .map((item) => ({
        name: `${Status[item.item1]} (${item.item2})`,
        value: item.item2,
        itemStyle: {
          color: seriesColors[Status[item.item1]] || '#999', // fallback color
        },
      }))

    totalOption.value.series[0].data = chartData as any
  })
}

const GetCronTickerRangeGraphData = (res: GetCronTickerGraphDataRangeResponse[]) => {
  // Extract unique Dates for xAxis
  const uniqueDates = res.map((x) => x.date)
  option.value.xAxis.data = uniqueDates // Assign to ECharts

  // Extract all unique item1 values (Status IDs)
  const uniqueItem1Set = new Set<number>()

  res.forEach(({ results }) => {
    results.forEach(({ item1 }) => uniqueItem1Set.add(item1))
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
    const dateIndex = uniqueDates.indexOf(date) // Find index in xAxis

    results.forEach(({ item1, item2 }) => {
      const dataArray = seriesMap.get(item1)
      if (dataArray) {
        dataArray[dateIndex] = item2 // Assign value at the correct index
      }
    })
  })

  // Generate series data
  const composedData = Array.from(seriesMap.entries()).map(([item1, dataArray]) => ({
    data: dataArray,
    name: Status[item1] || `Unknown ${item1}`,
    type: 'line',
    lineStyle: { color: seriesColors[Status[item1]] },
    itemStyle: { color: seriesColors[Status[item1]] },
  }))

  const statuses = ['Idle', 'Queued', 'InProgress', 'Done', 'DueDone', 'Failed', 'Cancelled', 'Batched']

  const seriesNames = composedData.filter((x) => x.data.some((y) => y > 0)).map((x) => x.name)

  option.value.legend = {
    show: true,
    textStyle: { color: '#fff' },
    selected: Object.fromEntries(statuses.map((status) => [status, seriesNames.includes(status)])),
  }

  option.value.series = composedData as any
}

const ShowCronTickerOccurrenceGraphData = async (
  functionName: string,
  id: string,
  startDate: number,
  endDate: number,
) => {
  if (id == selectedCronTickerGraphData.value) {
    selectedCronTickerGraphData.value = undefined
    option.value.title.subtext = 'Job statuses for all Cron Tickers'
    option.value.series = []
    await getCronTickerRangeGraphData
      .requestAsync(startDate, endDate)
      .then((res) => GetCronTickerRangeGraphData(res))
    range.value = [-3, 3]
    return
  } else {
    selectedCronTickerGraphData.value = undefined
    option.value.series = []
    option.value.title.subtext = `Job statuses for selected: ${functionName}`

    await getCronTickerRangeGraphDataById
      .requestAsync(id, startDate, endDate)
      .then((res) => GetCronTickerRangeGraphData(res))
    range.value = [-3, 3]
    await sleep(100).then(() => {
      selectedCronTickerGraphData.value = id
    })
  }
}

const addHubListeners = async () => {
  TickerNotificationHub.onReceiveAddCronTicker<GetCronTickerResponse>((response) => {
    getCronTickers.addToResponse(response)
  })

  TickerNotificationHub.onReceiveUpdateCronTicker<GetCronTickerResponse>((response) => {
    getCronTickers.updateByKey('id', response, []);
    if (crudCronTickerDialog.isOpen && crudCronTickerDialog.propData?.id == response.id) {
      crudCronTickerDialog.setPropData({ ...response, isFromDuplicate: false })
      nextTick(() => {
        ;(crudCronTickerDialogRef.value as any)?.resetForm()
      })
    }
  })

  TickerNotificationHub.onReceiveDeleteCronTicker<string>((id) => {
    if (crudCronTickerDialog.isOpen && crudCronTickerDialog.propData?.id == id) {
      crudCronTickerDialog.close()
      crudCronTickerDialog.propData = {} as GetCronTickerResponse & { isFromDuplicate: boolean }
    }
    getCronTickers.removeFromResponse('id', id)
  })
}

onMounted(async () => {
  await getTimeTickersGraphDataAndParseToGraph()
  await getCronTickers.requestAsync()
  await TickerNotificationHub.startConnection()
  await addHubListeners()
})

onUnmounted(() => {
  TickerNotificationHub.stopReceiver(methodName.onReceiveAddCronTicker)
  TickerNotificationHub.stopReceiver(methodName.onReceiveUpdateCronTicker)
  TickerNotificationHub.stopReceiver(methodName.onReceiveDeleteCronTicker)
})

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
  backgroundColor: '#212121',
  title: {
    subtext: 'Job statuses for all Cron Tickers',
    left: 'center',
    top: 'top',
    padding: [25, 0, 0, 0], // [top, right, bottom, left]
    subtextStyle: {
      color: '#aaa',
      fontSize: 14,
    },
  },
  tooltip: {
    trigger: 'axis',
  },
  legend: {
    show: true,
    textStyle: {
      color: '#fff',
    },
    selected: {},
  },
  grid: {
    left: '3%',
    right: '3%',
    bottom: '3%',
    containLabel: true,
  },
  toolbox: {
    feature: {
      saveAsImage: {},
    },
  },
  xAxis: {
    type: 'category',
    boundaryGap: false,
    data: [] as string[],
    axisLabel: {
      color: '#ccc',
    },
    axisLine: {
      lineStyle: {
        color: '#666',
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
    splitLine: {
      show: true,
      lineStyle: {
        color: '#444',
        width: 1,
      },
    },
  },
  series: [],
})

const totalOption = ref({
  backgroundColor: '#212121',
  title: {
    text: 'Total Cron Tickers',
    subtext: 'Pie chart of job statuses',
    left: 'center',
    top: 'top',
    textStyle: {
      color: '#fff',
      fontSize: 20,
      fontWeight: 'bold',
    },
    subtextStyle: {
      color: '#aaa',
      fontSize: 14,
    },
  },
  tooltip: {
    trigger: 'item',
    formatter: '{b}: {c} ({d}%)',
  },
  legend: {
    show: false,
  },
  series: [
    {
      type: 'pie',
      radius: ['40%', '70%'],
      center: ['50%', '100%'],
      startAngle: 180,
      endAngle: 360,
      data: [],
    },
  ],
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

const fetchGraphData = debounce(async ([min, max]: number[]) => {
  if (selectedCronTickerGraphData.value == undefined) {
    await getCronTickerRangeGraphData
      .requestAsync(min, max)
      .then((res) => GetCronTickerRangeGraphData(res))
  } else {
    await getCronTickerRangeGraphDataById
      .requestAsync(selectedCronTickerGraphData.value!, min, max)
      .then((res) => GetCronTickerRangeGraphData(res))
  }
}, 100) // You can tweak delay to 200ms+ for inputs

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
  <v-container fluid>
    <v-row>
      <v-col>
        <v-row>
          <v-col style="background-color: #212121" cols="4">
            <v-sheet min-height="35vh" rounded="lg">
              <v-chart class="chart" :option="totalOption" autoresize />
            </v-sheet>
          </v-col>
          <v-col style="background-color: #212121" cols="8">
            <v-sheet min-height="35vh" rounded="lg">
              <v-range-slider
                v-model="safeRange"
                :max="10"
                :min="-10"
                :step="1"
                show-ticks="always"
                :ticks="{ '-10': 'Past', 10: 'Future' }"
                hide-spin-buttons
                class="align-center pb-2"
                hide-details
                density="compact"
                thumb-size="12"
              >
                <template v-slot:prepend>
                  <v-text-field
                    v-model="safeMin"
                    @update:model-value="(x: any) => (x > 0 ? 0 : 2)"
                    density="compact"
                    style="width: 30px"
                    type="number"
                    variant="underlined"
                    hide-details
                    single-line
                  ></v-text-field>
                </template>
                <template v-slot:append>
                  <v-text-field
                    v-model="safeMax"
                    density="compact"
                    style="width: 30px"
                    type="number"
                    variant="underlined"
                    hide-details
                    single-line
                  ></v-text-field>
                </template>
              </v-range-slider>
              <v-chart class="chart" :option="option" autoresize />
            </v-sheet>
          </v-col>
        </v-row>
      </v-col>
      <v-col cols="12">
        <v-sheet rounded="lg" class="pt-5">
          <div class="d-flex justify-end px-4 mb-2">
            <v-btn
              rounded="lg"
              variant="tonal"
              prepend-icon="mdi-plus"
              color="primary"
              @click="
                crudCronTickerDialog.open({
                  ...({} as GetCronTickerResponse),
                  isFromDuplicate: true,
                })
              "
            >
              Add New
            </v-btn>
          </div>
          <v-data-table
            :headers="getCronTickers.headers.value"
            :loading="getCronTickers.loader.value"
            :items="getCronTickers.response.value"
            item-value="id"
            item-class="custom-row-class"
            density="compact"
          >
            <template v-slot:item.retryIntervals="{ item }">
              <span v-if="item.retryIntervals == null || item.retryIntervals.length == 0">
                <span>N/A</span>
              </span>
              <span v-else>
                [
                <span v-for="(interval, index) in item.retryIntervals" :key="index">
                  <span class="attempt">#{{ index + 1 }}</span>
                  <span class="retry-preview">&#x21FE;</span>
                  <span>{{ interval }}</span>
                  <span v-if="index < item.retryIntervals.length - 1">, </span>
                  <span v-else></span>
                </span>
                <span v-if="(item.retries as number) > item.retryIntervals.length">
                  <span v-if="item.retryIntervals.length < item.retries - 1">, ...</span>
                  <span v-else>, </span>
                  <span class="attempt">#{{ item.retries }}</span>
                  <span class="retry-preview">&#x21FE;</span>
                  <span>{{ item.retryIntervals[item.retryIntervals.length - 1] }}</span>
                </span>
                ]
              </span>
            </template>
            <template v-slot:item.actions="{ item }">
              <v-btn
                icon
                density="comfortable"
                @click="ShowCronTickerOccurrenceGraphData(item.function, item.id, -3, 3)"
              >
                <v-icon :color="selectedCronTickerGraphData == item.id ? 'grey' : 'light-blue'"
                  >mdi-chart-areaspline</v-icon
                >
              </v-btn>
              <v-btn
                @click="
                  cronOccurrenceDialog.open({
                    id: item.id,
                    retries: item.retries,
                    retryIntervals: item.retryIntervals,
                  })
                "
                icon
                density="comfortable"
              >
                <v-icon color="light-blue">mdi-folder-open</v-icon>
              </v-btn>
              <v-btn
                icon
                density="comfortable"
                @click="crudCronTickerDialog.open({ ...item, isFromDuplicate: false })"
              >
                <v-icon color="amber">mdi-pencil</v-icon>
              </v-btn>
              <v-btn
                @click="
                  confirmDialog.open({
                    ...new ConfirmDialogProps(),
                    id: item.id,
                    showWarningAlert: item.initIdentifier != undefined ? true : false,
                    warningAlertMessage:
                      'System-seeded ticker. To remove permanently, delete its cron expression from code.',
                  })
                "
                icon
                density="comfortable"
              >
                <v-icon color="red-lighten-1">mdi-delete</v-icon>
              </v-btn>
            </template>
          </v-data-table>
        </v-sheet>
      </v-col>
    </v-row>
  </v-container>
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
</template>
<style scoped>
.chart {
  height: 35vh;
}

.retry-preview {
  font-family: monospace;
}

.attempt {
  font-size: 0.75em;
  color: #c8bbbb;
}
</style>
