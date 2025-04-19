<script setup lang="ts">
import { tickerService } from '@/http/services/tickerService'
import { computed, ref, toRef, watch, type PropType } from 'vue'

const getTickerRequestData = tickerService.getRequestData()

const props = defineProps({
  isOpen: {
    type: Boolean,
    required: true,
    default: false,
  },
  dialogProps: {
    type: Object as PropType<{ id: string }>,
    required: true,
  },
})

const formattedJson = computed(() => {
  try {
    const formatted = JSON.stringify(JSON.parse(getTickerRequestData.response.value?.result!), null, 2);
    return formatted.replace(/\n/g, "<br>").replace(/ /g, "&nbsp;");
  } catch (error) {
    return "Invalid JSON";
  }
});

watch(
  () => props.dialogProps.id,
  async () => {
    if (props.dialogProps.id != undefined) {
      await getTickerRequestData.requestAsync(props.dialogProps.id, 1).then((res) => {
        emit('pushMatchType', res.matchType);
      })
    }
  },
);


const emit = defineEmits<{
  (e: 'close'): void
  (e: 'confirm'): void
  (e: 'pushMatchType', matchType: number): void
}>();
</script>


<template>
  <div class="text-center pa-4">
    <v-dialog v-model="toRef(isOpen).value" max-width="700" max-height="700" persistent>
      <v-card>
        <v-card-title>Request Data</v-card-title>
        <v-card-text>
          <v-sheet v-if="getTickerRequestData.response.value?.result" class="json-container">
            <pre v-html="formattedJson"></pre>
          </v-sheet>
          <v-sheet v-else class="no-data">No request data is defined</v-sheet>
        </v-card-text>
        <v-card-actions class="blue-border">
          <v-spacer></v-spacer>
          <v-btn @click="emit('close')" color="primary"> Close </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </div>
</template>
<style scoped>
.json-container {
  padding: 10px;
  border-radius: 5px;
  overflow: auto; /* Enables scrolling for JSON */
  max-height: 300px; /* Limits height so it scrolls */
  white-space: pre-wrap; /* Preserves JSON formatting */
  font-family: monospace;
}

/* Keep actions (buttons) fixed at the bottom */
.v-card-actions {
  position: sticky;
  bottom: 0;
  background: #212121;
  z-index: 2;
}

.blue-border {
  border-top: 1px solid #1976d2; /* Vuetify primary blue */
}
</style>