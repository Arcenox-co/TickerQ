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
  <v-container fluid>
    <v-row>
      <v-col cols="8">
        <v-card class="mx-auto">
          <template #title>
            Actions:
            <span v-if="!tickerHostStatus">
              <v-btn
                class="mx-2"
                rounded="lg"
                size="xl"
                variant="outlined"
                color="success"
                icon="mdi-play"
                @click="startTicker.requestAsync()"
                :loading="startTicker.loader.value"
              />
            </span>
            <span v-if="tickerHostStatus">
              <v-btn
                class="mx-2"
                rounded="lg"
                size="xl"
                variant="outlined"
                color="error"
                icon="mdi-stop"
                @click="confirmDialog.open({ ...new ConfirmDialogProps(), confirmText: 'Stop' })"
                :loading="stopTicker.loader.value"
              />
            </span>
            <span v-if="tickerHostStatus">
              <v-btn
                class="mx-2"
                rounded="lg"
                size="xl"
                variant="outlined"
                color="warning"
                icon="mdi-restart"
                @click="
                  restartTicker.requestAsync().then(async () => {
                    restartTicker.loader.value = true;
                    await sleep(1000).then(() => {
                      restartTicker.loader.value = false
                    })
                  })
                "
                :loading="restartTicker.loader.value"
              />
            </span>
          </template>
          <v-divider></v-divider>
          <div
            class="v-card-title"
            style="opacity: var(--v-list-item-subtitle-opacity, var(--v-medium-emphasis-opacity))"
          >
            Overview
          </div>
          <v-container fluid>
            <v-row>
              <v-col cols="3">
                <v-list-item class="w-100" v-if="tickerHostStatus">
                  <v-list-item-subtitle class="mb-2"> Status: </v-list-item-subtitle>
                  <v-icon color="green" class="">mdi-check-circle-outline </v-icon>
                  <span class="pt-4 mx-2">Active</span>
                </v-list-item>
                <v-list-item class="w-100" v-else>
                  <v-list-item-subtitle class="mb-2"> Status: </v-list-item-subtitle>
                  <v-icon color="red" class="">mdi-stop-circle-outline </v-icon>
                  <span class="pt-4 mx-2">Stopped</span>
                </v-list-item>
              </v-col>
              <v-col cols="3">
                <v-list-item-subtitle class="mb-2">
                  Success Jobs (past 7 days)</v-list-item-subtitle
                >
                <span class="pt-4">{{
                  getJobStatusesPastWeek.response.value != undefined
                    ? getJobStatusesPastWeek.response.value[0].item2
                    : 0
                }}</span>
              </v-col>
              <v-col cols="3">
                <v-list-item-subtitle class="mb-2"> Failed Jobs (past 7 days)</v-list-item-subtitle>
                <span class="pt-4">{{
                  getJobStatusesPastWeek.response.value != undefined
                    ? getJobStatusesPastWeek.response.value[1].item2
                    : 0
                }}</span>
              </v-col>
              <v-col cols="3">
                <v-list-item-subtitle class="mb-2"> Total Jobs (past 7 days)</v-list-item-subtitle>
                <span class="pt-4">{{
                  getJobStatusesPastWeek.response.value != undefined
                    ? getJobStatusesPastWeek.response.value[2].item2
                    : 0
                }}</span>
              </v-col>
            </v-row>
            <v-divider class="my-2"></v-divider>
            <v-row class="mt-3">
              <v-col cols="3">
                <v-list-item class="w-100">
                  <v-list-item-subtitle class="mb-2"> Next Occurrence: </v-list-item-subtitle>
                  <span class="pt-4">{{
                    getNextPlannedTicker.response.value?.nextOccurrence == undefined
                      ? 'N/A'
                      : formatDate(getNextPlannedTicker.response.value?.nextOccurrence)
                  }}</span>
                </v-list-item>
              </v-col>
              <v-col cols="3">
                <v-list-item-subtitle class="mb-2"> Max Concurrency: </v-list-item-subtitle>
                <span class="pt-4">{{ getOptions.response.value?.maxConcurrency }}</span>
              </v-col>
              <v-col cols="3">
                <v-list-item-subtitle class="mb-2"> Active Threads: </v-list-item-subtitle>
                <span class="pt-4">{{ activeThreads }}</span>
              </v-col>
              <v-col cols="3">
                <v-list-item-subtitle class="mb-2"> Current Machine: </v-list-item-subtitle>
                <span class="pt-4">{{ getOptions.response.value?.currentMachine }}</span>
              </v-col>
            </v-row>
          </v-container>
        </v-card>
        <v-row class="mt-2">
          <v-col cols="8">
            <v-card-title> Declared Functions </v-card-title>
            <v-data-table hide-default-footer density="compact" :items="functionItems">
              <template #item.request="{ item }">
                <div class="text-truncate" style="max-width: 200px">
                  {{ item.request }}
                </div>
              </template>
            </v-data-table>
          </v-col>
          <v-col cols="4">
            <v-card-title> Used Machines </v-card-title>
            <v-data-table
              hide-default-footer
              density="compact"
              :items="machineItems"
            ></v-data-table>
          </v-col>
        </v-row>
      </v-col>
      <v-col cols="4">
        <v-card class="mx-auto" subtitle="Time and Cron Tickers" title="Status Overview">
          <v-container fluid>
            <div v-for="status in statuses">
              <span>{{ `${status.name} (${status.count})` }}</span>
              <v-progress-linear
                :color="seriesColors[status.name]"
                :model-value="status.percentage"
                rounded
              ></v-progress-linear>
              <div class="text-right">{{ status.percentage }}%</div>
            </div>
          </v-container>
        </v-card>
      </v-col>
      <v-col cols="8">
        <v-alert color="#212121" title="Core Alerts:">
          <template #text>
            <span v-if="hasError">
              <v-alert
                class="mt-4"
                color="red"
                icon="mdi-close-circle"
                :text="`Error Message: ${getOptions.response.value?.lastHostExceptionMessage} `"
              ></v-alert>
            </span>
            <span v-else-if="warningMessage">
              <v-alert
                class="mt-4"
                icon="mdi-alert-circle-outline"
                color="warning"
                text="All worker threads are currently active. To avoid task delays, consider optimizing task execution or increasing the maximum concurrency setting."
              ></v-alert>
            </span>
            <span v-else> - </span>
          </template>
        </v-alert>
      </v-col>
    </v-row>
  </v-container>
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
::v-deep(
  .v-data-table .v-table__wrapper > table > thead > tr > th,
  .v-data-table .v-table__wrapper > table tbody > tr > th
) {
  background-color: #383131;
}
</style>
