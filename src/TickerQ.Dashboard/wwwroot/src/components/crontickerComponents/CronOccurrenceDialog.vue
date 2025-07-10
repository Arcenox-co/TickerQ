<script lang="ts">
import { watch, type PropType, toRef, onMounted, onUnmounted } from 'vue'
import { cronTickerOccurrenceService } from '@/http/services/cronTickerOccurrenceService'
import { Status } from '@/http/services/types/base/baseHttpResponse.types'
import { tickerService } from '@/http/services/tickerService'
import { sleep } from '@/utilities/sleep'
import { useDialog } from '@/composables/useDialog'
import { methodName, type TickerNotificationHubType } from '@/hub/tickerNotificationHub'
import type { GetCronTickerOccurrenceResponse } from '@/http/services/types/cronTickerOccurrenceService.types'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
</script>

<script setup lang="ts">
const confirmDialog = useDialog<{ data: string }>().withComponent(
  () => import('@/components/common/ConfirmDialog.vue'),
)

const exceptionDialog = useDialog<ConfirmDialogProps>().withComponent(
  () => import('@/components/common/ConfirmDialog.vue'),
)

const getByCronTickerId = cronTickerOccurrenceService.getByCronTickerId()
const requestCancelTicker = tickerService.requestCancel()
const deleteCronOccurrence = cronTickerOccurrenceService.deleteCronTickerOccurrence()

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

const addHubListeners = async () => {
  props.tickerNotificationHub.onReceiveUpdateCronTickerOccurrence((val:GetCronTickerOccurrenceResponse) => {
    getByCronTickerId.updateByKey('id', val, []);
  });

  props.tickerNotificationHub.onReceiveAddCronTickerOccurrence((val:GetCronTickerOccurrenceResponse) => {
    getByCronTickerId.addToResponse(val);
    getByCronTickerId.updatePropertyByKey('id', val.id,'retryIntervals', props.dialogProps.retryIntervals);
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
      await getByCronTickerId.requestAsync(props.dialogProps.id).then(() => {
        getByCronTickerId.updateProperty('retryIntervals', props.dialogProps.retryIntervals);
      })
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
      await getByCronTickerId.requestAsync(props.dialogProps.id)
    })
}

const onSubmitConfirmDialog = async () => {
  confirmDialog.close()
  await deleteCronOccurrence
    .requestAsync(confirmDialog.propData?.data!)
    .then(async () => await sleep(100))
    .then(async () => {
      await getByCronTickerId.requestAsync(props.dialogProps.id)
    })
}

const seriesColors: { [key: string]: string } = {
  Idle: '#A9A9A9', // Dark Gray
  Queued: '#00CED1', // Dark Turquoise
  InProgress: '#6495ED', // Royal Blue
  Done: '#32CD32', // Lime Green
  DueDone: '#008000', // Green
  Failed: '#FF0000', // Red
  Cancelled: '#FFD700', // Gold/Yellow,
  Batched: '#A9A9A9', // Dark Gray
}

const setRowProp = (propContext: any) => {
  return { style: `color:${seriesColors[propContext.item.status]}` }
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
  <div class="text-center pa-4">
    <v-dialog v-model="toRef(isOpen).value" width="auto">
      <v-card>
        <v-card-title class="d-flex justify-space-between align-center">
          <span class="headline">Cron Ticker Occurrences</span>
          <v-btn @click="emit('close')" variant="outlined" aria-label="Close">
            <v-icon>mdi-close</v-icon>
          </v-btn>
        </v-card-title>
        <v-data-table
          :headers="getByCronTickerId.headers.value"
          :loading="getByCronTickerId.loader.value"
          :items="getByCronTickerId.response.value"
          item-value="Id"
          :row-props="setRowProp"
          key="Id"
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
            <div v-else class="text-center">
              {{
                hasStatus(item.status, Status.Cancelled) || hasStatus(item.status, Status.Queued)
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
                  <span v-if="(props.dialogProps.retries as number) > item.retryIntervals.length">
                    <span
                      v-if="
                        (item.retryCount as number) > item.retryIntervals.length &&
                        (item.retryCount as number) != item.retryIntervals.length &&
                        (item.retryCount as number) != props.dialogProps.retries
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
                      <span v-if="(item.retryCount as number) + 1 != props.dialogProps.retries">, ...</span>
                      <span v-else>, </span>
                    </span>
                    <span v-else-if="(props.dialogProps.retries as number) == 2">, </span>
                    <span v-else>, ...</span>
                    <span
                      :class="
                        (item.retryCount as number) == props.dialogProps.retries
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
                      <span class="attempt">#{{ props.dialogProps.retries }}</span>
                      <span>&#x21FE;</span>
                      <span>{{ item.retryIntervals[item.retryIntervals.length - 1] }}</span>
                    </span>
                  </span>
                  ]
                </span>
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
              @click="confirmDialog.open({ data: item.id })"
              :disabled="hasStatus(item.status, Status.InProgress)"
              :variant="!hasStatus(item.status, Status.InProgress) ? 'elevated' : 'text'"
              icon
              density="comfortable"
            >
              <v-icon color="red">mdi-delete</v-icon>
            </v-btn>
          </template>
        </v-data-table>
      </v-card>
    </v-dialog>
  </div>
</template>
<style scoped>
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
