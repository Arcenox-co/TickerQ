<script setup lang="ts">
import type { GetTimeTickerResponse } from '@/http/services/types/timeTickerService.types'
import { computed, ref, toRef, watch, type PropType } from 'vue'
import { useFunctionNameStore } from '@/stores/functionNames'
import { useForm } from '@/composables/useCustomForm'
import { tickerService } from '@/http/services/tickerService'
import { timeTickerService } from '@/http/services/timeTickerService'
import { formatTime } from '@/utilities/dateTimeParser'
import { useTimeZoneStore } from '@/stores/timeZoneStore'

const functionNamesStore = useFunctionNameStore()
const timeZoneStore = useTimeZoneStore()
const getTickerRequestData = tickerService.getRequestData()
const addTimeTicker = timeTickerService.addTimeTicker()
const updateTimeTicker = timeTickerService.updateTimeTicker()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'confirm'): void
}>()

const props = defineProps({
  dialogProps: {
    type: Object as PropType<GetTimeTickerResponse & { isFromDuplicate: boolean }>,
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
  await getTickerRequestData.requestAsync(props.dialogProps.id, 1).then((res) => {
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

const parseUtcToDisplayDateTime = (utcString: string) => {
  if (!utcString) {
    return { date: null as Date | null, time: '' }
  }

  let iso = utcString.trim()
  if (!iso.endsWith('Z')) {
    iso = iso.replace(' ', 'T') + 'Z'
  }

  const utcDate = new Date(iso)
  // Use the dashboard's effective display timezone for UI
  const tz = timeZoneStore.effectiveTimeZone || 'UTC'

  try {
    const fmt = new Intl.DateTimeFormat('en-CA', {
      timeZone: tz,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false
    })

    const parts = fmt.formatToParts(utcDate)
    const get = (type: string) => parts.find(p => p.type === type)?.value ?? '00'

    const year = Number(get('year'))
    const month = Number(get('month')) - 1
    const day = Number(get('day'))
    const hour = get('hour')
    const minute = get('minute')
    const second = get('second')

    const dateObj = new Date(year, month, day)
    const timeStr = `${hour}:${minute}:${second}`

    return { date: dateObj, time: timeStr }
  } catch {
    // If timeZone is invalid in this environment, fall back to UTC date-only
    return { date: new Date(utcDate.getFullYear(), utcDate.getMonth(), utcDate.getDate()), time: '' }
  }
}

const formatLocalDateTimeWithoutZ = (date: Date): string => {
  const yyyy = date.getFullYear()
  const MM = String(date.getMonth() + 1).padStart(2, '0')
  const dd = String(date.getDate()).padStart(2, '0')
  const hh = String(date.getHours()).padStart(2, '0')
  const mm = String(date.getMinutes()).padStart(2, '0')
  const ss = String(date.getSeconds()).padStart(2, '0')

  // Return ISO-like string without timezone suffix so the server treats it as "unspecified"
  return `${yyyy}-${MM}-${dd}T${hh}:${mm}:${ss}`
}

const { resetForm, handleSubmit, bindField, setFieldValue, getFieldValue, values } = useForm({
  initialValues: {
    exampleData: '',
    requestData: '',
    functionName: '',
    executionDate: undefined as Date | undefined,
    executionTime: '',
    ignoreDateTime: false,
    description: '',
    retries: 0,
  },
  validationSchema: (validator) => ({
    functionName: validator.string().required('Function name is required'),
    executionDate: validator.date().when('ignoreDateTime', {
      is: true,
      then: (v) => v.notRequired(),
      otherwise: (v) => v.required('Date is required'),
    }),
    executionTime: validator.string().when('ignoreDateTime', {
      is: true,
      then: (v) => v.notRequired(),
      otherwise: (v) => v.required('Time is required'),
    }),
    ignoreDateTime: validator.boolean(),
  }),
  onFieldUpdate: {
    executionTime: (value, update) => {
      let cleaned = value.replace(/[^0-9]/g, '')

      if (!cleaned) {
        update('')
        return
      }

      const timeSegments = {
        hh: cleaned.slice(0, 2),
        mm: cleaned.slice(2, 4),
        ss: cleaned.slice(4, 6),
      }

      const normalizeSegments = (segment: string, max: number) =>
        Math.min(parseInt(segment, 10), max).toString().padStart(2, '0')

      const normalized = {
        hh: timeSegments.mm.length === 2 ? normalizeSegments(timeSegments.hh, 23) : timeSegments.hh,
        mm: timeSegments.mm.length === 2 ? normalizeSegments(timeSegments.mm, 59) : timeSegments.mm,
        ss: timeSegments.ss.length === 2 ? normalizeSegments(timeSegments.ss, 59) : timeSegments.ss,
      }

      const formattedValue = [
        normalized.hh,
        cleaned.length > 2 ? `:${normalized.mm}` : '',
        cleaned.length > 4 ? `:${normalized.ss}` : '',
      ].join('')

      update(formattedValue)
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
    executionTime__blur: (value, update) => {
      const [hh = '00', mm = '00', ss = '00'] = value.split(':')
      const formatTime = (val: string) => val.padStart(2, '0')

      update(`${formatTime(hh)}:${formatTime(mm)}:${formatTime(ss)}`)
    },
    functionName: (value, update) => {
      setFieldValue(
        'exampleData',
        functionNamesStore.data!.find((fn) => fn.functionName === value)?.functionRequestType!,
      )

      if (props.dialogProps.id == undefined || props.dialogProps.function != value)
        setFieldValue('requestData', '')
      else
        setFieldValue(
          'requestData',
          formatJsonForDisplay(getTickerRequestData.response.value?.result!, false)!,
        )

      update(value)
    },
  },
  onResetForm: () => {     
    setFieldValue('functionName', props.dialogProps.function);

    if (props.dialogProps.isFromDuplicate && props.dialogProps.id != undefined) {
      setFieldValue('retries', props.dialogProps.retries as number);
      setFieldValue('ignoreDateTime', true);
      setFieldValue('description', props.dialogProps.description);
      (props.dialogProps.retryIntervals as string[]).forEach(x => {
        comboBoxModel.value.push({
          id: Math.random().toString(36).substring(2) + Date.now().toString(36),
          title: formatTime(parseInt(x)),
          value: parseInt(x),
        });
      });
    }
    else if(props.dialogProps.id != undefined){
      (props.dialogProps.retryIntervals as string[]).forEach(x => {
        comboBoxModel.value.push({
          id: Math.random().toString(36).substring(2) + Date.now().toString(36),
          title: formatTime(parseInt(x)),
          value: parseInt(x),
        });
      });
      setFieldValue('retries', props.dialogProps.retries as number);
      setFieldValue('description', props.dialogProps.description);
      const parsed = parseUtcToDisplayDateTime(props.dialogProps.executionTime)
      if (parsed.date) {
        setFieldValue('executionDate', parsed.date)
      }
      if (parsed.time) {
        setFieldValue('executionTime', parsed.time)
      }
      setFieldValue('ignoreDateTime', false);
    }
    else{
      setFieldValue('ignoreDateTime', false);
    }
  },
  onSubmitForm: (values, errors) => {
    if (!errors) {
      const [hours, minutes, seconds] = values.executionTime.split(':').map(Number)

      const localDate = new Date(values.executionDate!)
      localDate.setHours(hours, minutes, seconds, 0)

      const executionDateTime = !values.ignoreDateTime
        ? formatLocalDateTimeWithoutZ(localDate)
        : undefined

      // Use the scheduler timezone for scheduling semantics (fallback to effective/display timezone)
      const schedulingTimeZone =
        timeZoneStore.schedulerTimeZone || timeZoneStore.effectiveTimeZone

      if (props.dialogProps.isFromDuplicate) {
        addTimeTicker
          .requestAsync({
            function: values.functionName,
            request: values.requestData,
            executionTime: executionDateTime,
            retries: parseInt(`${values.retries}`),
            description: values.description,
            intervals: comboBoxModel.value.map((item) => item.value),
          }, schedulingTimeZone)
          .then(() => {
            emit('confirm')
          })
      } else {
        updateTimeTicker
          .requestAsync(props.dialogProps.id, {
            function: values.functionName,
            request: values.requestData,
            executionTime: executionDateTime,
            retries: parseInt(`${values.retries}`),
            description: values.description,
            intervals: comboBoxModel.value.map((item) => item.value),
          }, schedulingTimeZone)
          .then(() => {
            emit('confirm')
          })
      }
    }
  },
})
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

const comboBoxFilter = (_: any, queryText: any, item: any) => {
  const toLowerCaseString = (val: any) => String(val != null ? val : '').toLowerCase()
  const query = toLowerCaseString(queryText)

  if (item.raw.header) return true

  const text = toLowerCaseString(item.raw.title)
  return text.includes(query)
}

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

const reqyestType = computed(() => {
  const functionName = getFieldValue('functionName')

  if (functionName == undefined || functionName == '')
    return 'Select a function to see the request type'

  const requestType = functionNamesStore.data?.find(
    (x) => x.functionName == functionName,
  )?.functionRequestNamespace

  return requestType == '' ? 'No request data' : requestType
})

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
              {{
                !props.dialogProps.isFromDuplicate
                  ? 'Update'
                  : props.dialogProps.id == undefined
                    ? 'Add New'
                    : 'Clone'
              }}
              Time Ticker
            </v-card-title>
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
                  <v-col cols="6" v-if="!getFieldValue('ignoreDateTime')">
                    <v-date-input
                      v-bind="bindField('executionDate', (value) => new Date(value))"
                      label="Date input"
                      :min="new Date().toISOString().split('T')[0]"
                      prepend-icon=""
                      prepend-inner-icon="$calendar"
                      variant="outlined"
                    ></v-date-input>
                  </v-col>
                  <v-col cols="6" v-if="!getFieldValue('ignoreDateTime')">
                    <v-text-field
                      v-bind="bindField('executionTime')"
                      label="Enter Time (HH:mm:ss)"
                      placeholder="HH:mm:ss"
                      v-mask="'##:##:##'"
                      prepend-icon=""
                      prepend-inner-icon="mdi-clock-outline"
                      variant="outlined"
                    />
                  </v-col>
                  <v-col>
                    <v-checkbox-btn
                      label="Execute immediately (with a one-second delay)"
                      class="pe-2"
                      v-bind="bindField('ignoreDateTime')"
                    ></v-checkbox-btn>
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
            {{
              !props.dialogProps.isFromDuplicate
                ? 'Update'
                : props.dialogProps.id == undefined
                  ? 'Create'
                  : 'Clone'
            }}
          </v-btn>
        </template>
      </v-card>
    </v-dialog>
  </div>
</template>
<style scoped>
/* For Chrome, Safari, Edge, Opera */
input::-webkit-outer-spin-button,
input::-webkit-inner-spin-button {
  -webkit-appearance: none !important;
  margin: 0;
}
</style>
