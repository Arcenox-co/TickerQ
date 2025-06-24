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

const requestMatchType = ref(new Map<string, number>())
const crudTimeTickerDialogRef = ref(null)

onMounted(async () => {
  await TickerNotificationHub.startConnection()
  await getTimeTickers.requestAsync()
  await getTimeTickersGraphDataRangeAndParseToGraph(range.value[0], range.value[1])
  await getTimeTickersGraphDataAndParseToGraph()
  await addHubListeners()
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
      crudTimeTickerDialog.setPropData({ ...response, executionTime: formatFromUtcToLocal(response.executionTime), isFromDuplicate: false })
      nextTick(() => {
        ;(crudTimeTickerDialogRef.value as any)?.resetForm()
      })
    }
  })

  TickerNotificationHub.onReceiveDeleteTimeTicker<string>((id) => {
    getTimeTickers.removeFromResponse('id', id)
  })
}

const closeCrudTimeTickerDialog = () => {
  crudTimeTickerDialog.close();
}

const getTimeTickersGraphDataAndParseToGraph = async () => {
  await getTimeTickersGraphData.requestAsync().then((res) => {
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

const getTimeTickersGraphDataRangeAndParseToGraph = async (startDate: number, endDate: number) => {
  await getTimeTickersGraphDataRange.requestAsync(startDate, endDate).then((res) => {
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

    const statuses = ['Idle', 'Queued', 'InProgress', 'Done', 'DueDone', 'Failed', 'Cancelled']

    const seriesNames = composedData.filter((x) => x.data.some((y) => y > 0)).map((x) => x.name)

    option.value.legend = {
      show: true,
      textStyle: { color: '#fff' },
      selected: Object.fromEntries(
        statuses.map((status) => [status, seriesNames.includes(status)]),
      ),
    }

    option.value.series = composedData as any
  })
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

const requestCancel = async (id: string) => {
  await requestCancelTicker.requestAsync(id)
}

const onSubmitConfirmDialog = async () => {
  confirmDialog.close()
  await deleteTimeTicker.requestAsync(confirmDialog.propData?.id!)
}

const setRowProp = (propContext: any) => {
  return { style: `color:${seriesColors[propContext.item.status]}` }
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
  backgroundColor: '#212121',
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
    text: 'Total Time Tickers',
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
  await getTimeTickersGraphDataRangeAndParseToGraph(min, max)
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
</script>
<template>
  <v-container fluid>
    <v-row>
      <v-col cols="12">
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
        <div class="scrollable-container bg-surface-light">
          <v-sheet rounded="lg" class="pt-5">
            <div class="d-flex justify-end px-4 mb-2">
              <v-btn
                rounded="lg"
                variant="tonal"
                prepend-icon="mdi-plus"
                color="primary"
                @click="
                  crudTimeTickerDialog.open({
                    ...({} as GetTimeTickerResponse),
                    isFromDuplicate: true,
                  })
                "
              >
                Add New
              </v-btn>
            </div>
            <v-data-table
              density="compact"
              :row-props="setRowProp"
              :headers="getTimeTickers.headers.value"
              :loading="getTimeTickers.loader.value"
              :items="getTimeTickers.response.value"
              item-value="Id"
              item-class="custom-row-class"
            >
              <template v-slot:item.status="{ item }">
                <span
                  :class="hasStatus(item.status, Status.Failed) ? 'underline' : ''"
                  @click="
                    hasStatus(item.status, Status.Failed)
                      ? exceptionDialog.open({
                          ...new ConfirmDialogProps(),
                          title: 'Exception Details',
                          text: JSON.stringify(JSON.parse(item.exception!), null, 2),
                          showConfirm: false,
                          maxWidth: '800',
                          icon: 'mdi-bug-outline',
                          isCode: true,
                        })
                      : null
                  "
                >
                  <span>{{ item.status }}</span>
                  <v-icon
                    class="ml-2 mb-1"
                    size="small"
                    v-if="hasStatus(item.status, Status.Failed)"
                    >mdi-bug-outline</v-icon
                  >
                </span>
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
                  <p class="blue-underline mr-3" @click="tickerRequestDialog.open({ id: item.id })">
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
                <div v-else>
                  {{
                    hasStatus(item.status, Status.Cancelled) ||
                    hasStatus(item.status, Status.Queued)
                      ? 'N/A'
                      : item.executedAt
                  }}
                </div>
              </template>
              <template v-slot:item.retryIntervals="{ item }">
                <span v-if="item.retryIntervals == null || item.retryIntervals.length == 0">
                  <span>N/A</span>
                </span>
                <span v-else>
                  [
                  <span v-for="(interval, index) in item.retryIntervals" :key="index">
                    <span
                      :class="
                        index == item.retryCount - 1
                          ? item.status == Status[Status.InProgress]
                            ? 'interval interval-running'
                            : item.status != Status[Status.InProgress] ||
                                item.status != Status[Status.Idle] ||
                                item.status != Status[Status.Queued]
                              ? 'active-attempt'
                              : 'interval'
                          : 'interval'
                      "
                    >
                      <span class="attempt">#{{ index + 1 }}</span>
                      <span class="retry-preview">&#x21FE;</span>
                      <span>{{ interval }}</span>
                    </span>
                    <span v-if="index < item.retryIntervals.length - 1">, </span>
                  </span>
                  <span v-if="(item.retries as number) > item.retryIntervals.length">
                    <span
                      v-if="
                        (item.retryCount as number) > item.retryIntervals.length &&
                        (item.retryCount as number) != item.retryIntervals.length &&
                        (item.retryCount as number) != item.retries
                      "
                    >
                      <span
                        class="attempt"
                        v-if="(item.retryCount as number) != item.retryIntervals.length + 1"
                        >, ...
                      </span>
                      <span v-else>, </span>
                      <span
                        :class="
                          item.status == Status[Status.InProgress]
                            ? 'interval interval-running'
                            : item.status != Status[Status.InProgress] ||
                                item.status != Status[Status.Idle] ||
                                item.status != Status[Status.Queued]
                              ? 'active-attempt'
                              : 'interval'
                        "
                      >
                        <span class="attempt">#{{ item.retryCount }}</span>
                        <span class="retry-preview">&#x21FE;</span>
                        <span>{{ item.retryIntervals[item.retryIntervals.length - 1] }}</span>
                      </span>
                      <span v-if="(item.retryCount as number) + 1 != item.retries">, ...</span>
                      <span v-else>, </span>
                    </span>
                    <span v-else-if="(item.retries as number) == 2">, </span>
                    <span v-else>, ...</span>
                    <span
                      :class="
                        (item.retryCount as number) == item.retries
                          ? item.status == Status[Status.InProgress]
                            ? 'interval interval-running'
                            : item.status != Status[Status.InProgress] ||
                                item.status != Status[Status.Idle] ||
                                item.status != Status[Status.Queued]
                              ? 'active-attempt'
                              : 'interval'
                          : 'interval'
                      "
                    >
                      <span class="attempt">#{{ item.retries }}</span>
                      <span>&#x21FE;</span>
                      <span>{{ item.retryIntervals[item.retryIntervals.length - 1] }}</span>
                    </span>
                  </span>
                  ]
                </span>
              </template>
              <template v-slot:item.lockHolder="{ item }">
                {{ item.lockHolder }}
                <v-tooltip v-if="item.lockHolder != 'N/A'" activator="parent" location="left">
                  <span>Locked At: {{ formatDate(item.lockedAt) }}</span>
                </v-tooltip>
              </template>
              <template v-slot:item.actions="{ item }">
                <v-btn
                  @click="requestCancel(item.id)"
                  :disabled="!hasStatus(item.status, Status.InProgress)"
                  icon
                  :variant="hasStatus(item.status, Status.InProgress) ? 'elevated' : 'text'"
                  density="comfortable"
                >
                  <v-icon :color="hasStatus(item.status, Status.InProgress) ? 'blue' : 'grey'"
                    >mdi-cancel</v-icon
                  >
                </v-btn>
                <v-btn
                  v-if="
                    hasStatus(item.status, Status.Queued) || hasStatus(item.status, Status.Idle)
                  "
                  icon
                  density="comfortable"
                  @click="crudTimeTickerDialog.open({ ...item, executionTime: formatFromUtcToLocal(item.executionTime), isFromDuplicate: false })"
                >
                  <v-icon color="amber">mdi-pencil</v-icon>
                </v-btn>
                <v-btn
                  v-else
                  icon
                  density="comfortable"
                  @click="crudTimeTickerDialog.open({ ...item, executionTime: formatFromUtcToLocal(item.executionTime), isFromDuplicate: true })"
                >
                  <v-icon color="grey">mdi-plus-box-multiple-outline</v-icon>
                </v-btn>
                <v-btn
                  @click="confirmDialog.open({ id: item.id })"
                  :disabled="hasStatus(item.status, Status.InProgress)"
                  :variant="!hasStatus(item.status, Status.InProgress) ? 'elevated' : 'text'"
                  icon
                  density="comfortable"
                >
                  <v-icon color="red">mdi-delete</v-icon>
                </v-btn>
              </template>
            </v-data-table>
          </v-sheet>
        </div>
      </v-col>
    </v-row>
  </v-container>
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
</template>
<style scoped>
.chart {
  height: 35vh;
}

.blue-underline {
  cursor: pointer;
  text-decoration: underline;
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

.retry-preview {
  font-family: monospace;
}

.interval > .attempt {
  font-size: 0.75em;
  color: #c8bbbb;
}

.active-attempt > .attempt {
  font-size: 0.75em;
}

.interval {
  color: #c8bbbb;
}

.interval-running {
  color: rgb(0, 145, 255);
}

.underline {
  text-decoration: underline;
  cursor: pointer;
}
</style>
