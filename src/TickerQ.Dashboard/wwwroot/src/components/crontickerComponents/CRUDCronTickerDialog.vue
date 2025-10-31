<script setup lang="ts">
import { computed, ref, toRef, watch, type PropType } from 'vue'
import { useFunctionNameStore } from '@/stores/functionNames'
import { useForm } from '@/composables/useCustomForm'
import { tickerService } from '@/http/services/tickerService'
import type { GetCronTickerResponse } from '@/http/services/types/cronTickerService.types'
import { cronTickerService } from '@/http/services/cronTickerService'
import { formatTime } from '@/utilities/dateTimeParser'
import cronstrue from 'cronstrue'
const functionNamesStore = useFunctionNameStore()
const getTickerRequestData = tickerService.getRequestData()
const updateCronTicker = cronTickerService.updateCronTicker()
const addCronTicker = cronTickerService.addCronTicker()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'confirm'): void
}>()

const props = defineProps({
  dialogProps: {
    type: Object as PropType<GetCronTickerResponse & { isFromDuplicate: boolean }>,
    required: true,
  },
  isOpen: {
    type: Boolean,
    required: true,
    default: false,
  },
})

watch(
  () => props.dialogProps.id,
  async () => {
    await setRequestData()
    resetForm()
  },
)

const setRequestData = async () => {
  await getTickerRequestData.requestAsync(props.dialogProps.id, 0).then((res) => {
    const formattedJson = formatJsonForDisplay(res.result!, false)

    if (formattedJson == undefined) setFieldValue('requestData', '')
    else setFieldValue('requestData', formattedJson)
  })
}

const formatJsonForDisplay = (json: string, isHtml: boolean = false) => {
  if (json == null) return undefined
  try {
    const formatted = JSON.stringify(JSON.parse(json), null, 2)
    return isHtml ? formatted.replace(/\n/g, '<br>').replace(/ /g, '&nbsp;') : formatted
  } catch (error) {
    return undefined
  }
}

// Validate cron expression using cronstrue
// Cronstrue will throw an error if the expression is invalid
// We require 6-segment format (with seconds)
const validateCronExpression = (value: string): boolean => {
  if (!value) return false
  
  // Check if it has 6 segments (seconds included)
  const segments = value.trim().split(/\s+/)
  if (segments.length !== 6) {
    return false
  }
  
  try {
    // Try to parse the expression with cronstrue
    // If it's invalid, it will throw an error
    cronstrue.toString(value)
    return true
  } catch (error) {
    return false
  }
}

// Get readable expression for display
const readableExpression = computed(() => {
  const expression = values.expression
  if (!expression || !validateCronExpression(expression)) {
    return ''
  }
  try {
    return cronstrue.toString(expression)
  } catch (error) {
    return ''
  }
})

const { resetForm, handleSubmit, bindField, setFieldValue, getFieldValue, values } = useForm({
  initialValues: {
    exampleData: '',
    requestData: '',
    functionName: '',
    expression: '',
    description: '',
    retries: 0,
  },
  validationSchema: (validator) => ({
    functionName: validator.string().required('Function name is required'),
    expression: validator
      .string()
      .required('Expression is required')
      .test('valid-cron', 'Invalid expression. Must be a valid 6-segment cron expression (seconds minutes hours day month day-of-week)', validateCronExpression),
  }),
  onFieldUpdate: {
    functionName: (value, update) => {
      const functionData = functionNamesStore.data?.find((fn) => fn.functionName === value)
      setFieldValue(
        'exampleData',
        functionData?.functionRequestType || '',
      )
      setFieldValue(
        'requestData',
        formatJsonForDisplay(getTickerRequestData.response.value?.result || '', false) || '',
      )

      update(value)
    },
    retries: (value, update) => {
      if (value < comboBoxModel.value.length) {
        comboBoxModel.value.splice(value)
      }

      if (values.retries == comboBoxModel.value.length)
        comboBoxItems[0].title = 'Max intervals reached'
      else comboBoxItems[0].title = 'Select suggested intervals or create one'

      if (value < 0) update(0)
      else update(value)
    },
  },
  onResetForm: () => {
    setFieldValue('functionName', props.dialogProps.function)
    setFieldValue('expression', props.dialogProps.expression)
    if (props.dialogProps.isFromDuplicate && props.dialogProps.id != undefined) {
      setFieldValue('retries', props.dialogProps.retries as number)
      setFieldValue('description', props.dialogProps.description)
      ;(props.dialogProps.retryIntervals as string[]).forEach((x) => {
        comboBoxModel.value.push({
          id: Math.random().toString(36).substring(2) + Date.now().toString(36),
          title: formatTime(parseInt(x)),
          value: parseInt(x),
        })
      })
    } else if (props.dialogProps.id != undefined) {
      ;(props.dialogProps.retryIntervals as string[]).forEach((x) => {
        comboBoxModel.value.push({
          id: Math.random().toString(36).substring(2) + Date.now().toString(36),
          title: formatTime(parseInt(x)),
          value: parseInt(x),
        })
      })
      setFieldValue('retries', props.dialogProps.retries as number)
      setFieldValue('description', props.dialogProps.description)
    }
  },
  onSubmitForm: async (values, errors) => {
    if (!errors) {
      var originalRequestData = values.requestData
      if (!props.dialogProps.isFromDuplicate) {
        await updateCronTicker
          .requestAsync(props.dialogProps.id, {
            function: values.functionName,
            expression: values.expression,
            request: values.requestData,
            retries: parseInt(`${values.retries}`),
            description: values.description,
            intervals: comboBoxModel.value.map((item) => item.value),
          })
          .then(() => {
            emit('close')
            emit('confirm')
          })
      } else {
        await addCronTicker
          .requestAsync({
            function: values.functionName,
            expression: values.expression,
            request: values.requestData,
            retries: parseInt(`${values.retries}`),
            description: values.description,
            intervals: comboBoxModel.value.map((item) => item.value),
          })
          .then(() => {
            emit('close')
            emit('confirm')
          })
      }

      getTickerRequestData.updateProperty('result', originalRequestData)
      resetForm()
    }
  },
})

const reqyestType = computed(() => {
  const functionName = getFieldValue('functionName')

  if (functionName == undefined || functionName == '')
    return 'Select a function to see the request type'

  const requestType = functionNamesStore.data?.find(
    (x) => x.functionName == functionName,
  )?.functionRequestNamespace

  return requestType == '' ? 'No request data' : requestType
})

const comboBoxFilter = (_: any, queryText: any, item: any) => {
  const toLowerCaseString = (val: any) => String(val != null ? val : '').toLowerCase()
  const query = toLowerCaseString(queryText)

  if (item.raw.header) return true

  const text = toLowerCaseString(item.raw.title)
  return text.includes(query)
}

const comboBoxItems = [
  { header: true, title: 'Select suggested intervals or create one' },
  {
    id: 1,
    title: '1 min',
    value: 60,
  },
  {
    id: 2,
    title: '5 min',
    value: 300,
  },
  {
    id: 3,
    title: '10 min',
    value: 600,
  },
]

const comboBoxModel = ref<any[]>([])
const comboBoxSearch = ref<string | null>(null)

watch(comboBoxSearch, () => {
  if (parseInt(comboBoxSearch.value!) > 86400) comboBoxSearch.value = '86400'
  else if (parseInt(comboBoxSearch.value!) < 0) comboBoxSearch.value = '0'
})

watch(
  comboBoxModel,
  () => {
    if (values.retries == comboBoxModel.value.length)
      comboBoxItems[0].title = 'Max intervals reached'
    else comboBoxItems[0].title = 'Select suggested intervals or create one'

    if (values.retries < comboBoxModel.value.length) {
      comboBoxModel.value.pop()
    } else {
      let lastItem = comboBoxModel.value[comboBoxModel.value.length - 1]
      if (typeof lastItem === 'string') {
        if (parseInt(lastItem) > 86400) lastItem = '86400'
        else if (parseInt(lastItem) < 0) lastItem = '0'
        comboBoxModel.value.splice(-1, 1, {
          id: Date.now(),
          title: formatTime(parseInt(lastItem)),
          value: parseInt(lastItem),
        })
      }
    }
  },
  { deep: true },
)

const removeSelection = (index: any) => {
  comboBoxModel.value.splice(index, 1)
}

defineExpose({
  resetForm,
})
</script>
<template>
  <div class="text-center pa-4">
    <v-dialog v-model="toRef(isOpen).value" max-width="1000" persistent>
      <v-card>
        <v-divider></v-divider>
        <v-row>
          <v-col cols="5">
            <v-card-title class="py-4"> Example Data </v-card-title>
            <v-divider></v-divider>
            <v-container>
              <v-card class="mx-auto my-8" elevation="16" max-width="344">
                <v-card-item>
                  <template v-slot:append>
                    <v-btn
                      icon="mdi-share-outline"
                      @click="setFieldValue('requestData', getFieldValue('exampleData'))"
                    ></v-btn>
                  </template>
                  <v-card-title> Example Data </v-card-title>
                  <v-card-subtitle>{{ reqyestType }}</v-card-subtitle>
                  <v-divider></v-divider>
                </v-card-item>

                <v-card-text>
                  <div v-html="formatJsonForDisplay(getFieldValue('exampleData'), true)"></div>
                </v-card-text>
              </v-card>
            </v-container>
          </v-col>
          <v-col cols="7">
            <v-card-title class="py-4">
              {{ !props.dialogProps.isFromDuplicate ? 'Update' : 'Add New' }} Cron Ticker
            </v-card-title>

            <v-card-subtitle v-if="props.dialogProps.initIdentifier">
              <v-alert
                class="mb-4"
                icon="mdi-alert-circle-outline"
                color="warning"
                text="This ticker is system-seeded. Changes will reset on app restart."
              ></v-alert>
            </v-card-subtitle>

            <v-divider></v-divider>
            <v-form>
              <v-container>
                <v-row>
                  <v-col cols="6">
                    <v-select
                      v-bind="bindField('functionName')"
                      item-title="functionName"
                      item-value="functionName"
                      :items="functionNamesStore.data"
                      variant="outlined"
                      label="Functions"
                    ></v-select>
                  </v-col>
                  <v-col cols="6">
                    <v-text-field
                      v-bind="bindField('expression')"
                      variant="outlined"
                      label="Expression"
                      :hint="readableExpression || 'Enter a valid 6-segment cron expression (seconds minutes hours day month day-of-week)'"
                      persistent-hint
                    ></v-text-field>
                  </v-col>
                  <v-col cols="12">
                    <v-text-field
                      v-bind="bindField('description')"
                      label="Short description"
                      variant="outlined"
                    />
                  </v-col>
                  <v-col cols="4">
                    <v-text-field
                      v-bind="bindField('retries')"
                      type="number"
                      label="Retries"
                      variant="outlined"
                      :min="0"
                      :max="10"
                    />
                  </v-col>
                  <v-col cols="8">
                    <v-combobox
                      :custom-filter="comboBoxFilter"
                      v-model="comboBoxModel"
                      v-model:search="comboBoxSearch"
                      :items="comboBoxItems"
                      type="number"
                      :min="0"
                      :max="86400"
                      :label="
                        values.retries == 0
                          ? 'Retries: 0 (no intervals)'
                          : comboBoxModel.length >= 1
                            ? 'Unset intervals use (default: last set)'
                            : 'Set intervals or (default: 30s)'
                      "
                      :item-title="(item) => item.title"
                      :item-value="(item) => item.id"
                      hide-selected
                      multiple
                      hide-spin-buttons
                      :disabled="values.retries == 0"
                      :hint="`Max intervals (retries): ${values.retries}`"
                      persistent-hint
                      variant="outlined"
                    >
                      <template v-slot:selection="{ item, index }">
                        <v-chip
                          v-if="item === Object(item)"
                          :text="item.title"
                          size="small"
                          variant="flat"
                          closable
                          label
                          @click:close="removeSelection(index)"
                        ></v-chip>
                      </template>
                      <template v-slot:item="{ props, item }">
                        <v-list-item v-if="item.raw.header && comboBoxSearch">
                          <span v-if="comboBoxModel.length < values.retries">
                            <v-chip size="small" variant="flat" label>
                              {{ formatTime(parseInt(comboBoxSearch!)) }}
                              {{ parseInt(comboBoxSearch!) >= 86400 ? '(max value)' : '' }}
                            </v-chip>
                            <span class="ml-3">Press enter to add</span>
                          </span>
                          <span v-else>
                            <v-chip
                              text="Max intervals reached"
                              variant="plain"
                              size="small"
                              label
                            />
                          </span>
                        </v-list-item>
                        <v-list-subheader
                          v-else-if="item.raw.header"
                          :title="item.title"
                        ></v-list-subheader>
                        <v-list-item
                          v-if="!item.raw.header && comboBoxModel.length < values.retries"
                          @click="props.onClick as any"
                        >
                          <v-chip :text="item.raw.title" variant="flat" label></v-chip>
                        </v-list-item>
                      </template>
                    </v-combobox>
                  </v-col>
                  <v-col cols="12">
                    <v-textarea
                      rows="10"
                      v-bind="bindField('requestData')"
                      label="Request Data"
                      variant="outlined"
                      :disabled="
                        formatJsonForDisplay(getFieldValue('exampleData'), false) == undefined
                      "
                    />
                  </v-col>
                </v-row>
              </v-container>
            </v-form>
          </v-col>
        </v-row>

        <template v-slot:actions>
          <v-spacer></v-spacer>
          <v-btn color="grey" @click="emit('close')"> Close </v-btn>
          <v-btn color="orange" @click="handleSubmit">
            {{ props.dialogProps.isFromDuplicate ? 'Create' : 'Update' }}
          </v-btn>
        </template>
      </v-card>
    </v-dialog>
  </div>
</template>
