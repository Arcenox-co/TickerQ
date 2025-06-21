<script lang="ts">
import { ref, type PropType, toRef } from 'vue'
export class ConfirmDialogProps {
  icon?:string = 'mdi-alert-circle';
  iconColor?:string = '#F44336';
  cancelText?:string = 'Cancel';
  confirmText?:string = 'Delete';
  cancelColor?:string = '#9E9E9E';
  confirmColor?:string = '#F44336';
  title?:string = 'Confirm Action';
  text?:string = 'Are you sure you want to proceed?';
  maxWidth?:string = '500';
  isCode?:boolean = false;
  showCancel?:boolean = true;
  showConfirm?:boolean = true;
  showWarningAlert?:boolean = false;
  warningAlertMessage?:string = "";
}
</script>

<script setup lang="ts">

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'confirm'): void
}>()

defineProps({
  dialogProps: {
    type: Object as PropType<ConfirmDialogProps>,
    default: () => new ConfirmDialogProps(),
  },
  isOpen: {
    type: Boolean,
    required: true
  },
})

</script>
<template>
  <div class="text-center pa-4">
    <v-dialog v-model="toRef(isOpen).value" :max-width="dialogProps.maxWidth" persistent>
      <v-card :title="dialogProps.title">
        <template #text>
          <pre v-if="dialogProps.isCode" class="json-box">{{ dialogProps.text }}</pre>
          <span v-else>{{ dialogProps.text }}</span>
          <v-alert
                v-if="dialogProps.showWarningAlert"
                class="mb-4 mt-4"
                icon="mdi-alert-circle-outline"
                color="warning"
                :text="dialogProps.warningAlertMessage"
              ></v-alert>
        </template>
        <template v-slot:prepend>
          <v-icon :color="dialogProps.iconColor">{{ dialogProps.icon }}</v-icon>
        </template>
        <template v-slot:actions>
          <v-spacer></v-spacer>
          <v-btn v-if="dialogProps.showCancel" :color="dialogProps.cancelColor" @click="emit('close')">
            {{ dialogProps.cancelText }}
          </v-btn>
          <v-btn v-if="dialogProps.showConfirm" :color="dialogProps.confirmColor" @click="emit('confirm')">
            {{ dialogProps.confirmText }}
          </v-btn>
        </template>
      </v-card>
    </v-dialog>
  </div>
</template>
<style scoped>
.json-box {
  white-space: pre-wrap;
  word-wrap: break-word;
}
</style>
