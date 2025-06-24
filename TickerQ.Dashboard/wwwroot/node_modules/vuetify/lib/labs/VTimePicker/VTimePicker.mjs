import { resolveDirective as _resolveDirective, mergeProps as _mergeProps, createVNode as _createVNode } from "vue";
// Styles
import "./VTimePicker.css";

// Components
import { pad } from "./util.mjs";
import { VTimePickerClock } from "./VTimePickerClock.mjs";
import { VTimePickerControls } from "./VTimePickerControls.mjs";
import { makeVPickerProps, VPicker } from "../VPicker/VPicker.mjs"; // Composables
import { useLocale } from "../../composables/locale.mjs"; // Utilities
import { computed, onMounted, ref, watch } from 'vue';
import { SelectingTimes } from "./SelectingTimes.mjs";
import { createRange, genericComponent, omit, propsFactory, useRender } from "../../util/index.mjs"; // Types
const rangeHours24 = createRange(24);
const rangeHours12am = createRange(12);
const rangeHours12pm = rangeHours12am.map(v => v + 12);
const range60 = createRange(60);
const selectingNames = {
  1: 'hour',
  2: 'minute',
  3: 'second'
};
export { SelectingTimes };
export const makeVTimePickerProps = propsFactory({
  allowedHours: [Function, Array],
  allowedMinutes: [Function, Array],
  allowedSeconds: [Function, Array],
  ampmInTitle: Boolean,
  disabled: Boolean,
  format: {
    type: String,
    default: 'ampm'
  },
  max: String,
  min: String,
  modelValue: null,
  readonly: Boolean,
  scrollable: Boolean,
  useSeconds: Boolean,
  ...omit(makeVPickerProps({
    title: '$vuetify.timePicker.title'
  }), ['landscape'])
}, 'VTimePicker');
export const VTimePicker = genericComponent()({
  name: 'VTimePicker',
  props: makeVTimePickerProps(),
  emits: {
    'update:hour': val => true,
    'update:minute': val => true,
    'update:period': val => true,
    'update:second': val => true,
    'update:modelValue': val => true
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    const {
      t
    } = useLocale();
    const inputHour = ref(null);
    const inputMinute = ref(null);
    const inputSecond = ref(null);
    const lazyInputHour = ref(null);
    const lazyInputMinute = ref(null);
    const lazyInputSecond = ref(null);
    const period = ref('am');
    const selecting = ref(SelectingTimes.Hour);
    const controlsRef = ref(null);
    const clockRef = ref(null);
    const isAllowedHourCb = computed(() => {
      let cb;
      if (props.allowedHours instanceof Array) {
        cb = val => props.allowedHours.includes(val);
      } else {
        cb = props.allowedHours;
      }
      if (!props.min && !props.max) return cb;
      const minHour = props.min ? Number(props.min.split(':')[0]) : 0;
      const maxHour = props.max ? Number(props.max.split(':')[0]) : 23;
      return val => {
        return val >= minHour * 1 && val <= maxHour * 1 && (!cb || cb(val));
      };
    });
    const isAllowedMinuteCb = computed(() => {
      let cb;
      const isHourAllowed = !isAllowedHourCb.value || inputHour.value === null || isAllowedHourCb.value(inputHour.value);
      if (props.allowedMinutes instanceof Array) {
        cb = val => props.allowedMinutes.includes(val);
      } else {
        cb = props.allowedMinutes;
      }
      if (!props.min && !props.max) {
        return isHourAllowed ? cb : () => false;
      }
      const [minHour, minMinute] = props.min ? props.min.split(':').map(Number) : [0, 0];
      const [maxHour, maxMinute] = props.max ? props.max.split(':').map(Number) : [23, 59];
      const minTime = minHour * 60 + minMinute * 1;
      const maxTime = maxHour * 60 + maxMinute * 1;
      return val => {
        const time = 60 * inputHour.value + val;
        return time >= minTime && time <= maxTime && isHourAllowed && (!cb || cb(val));
      };
    });
    const isAllowedSecondCb = computed(() => {
      let cb;
      const isHourAllowed = !isAllowedHourCb.value || inputHour.value === null || isAllowedHourCb.value(inputHour.value);
      const isMinuteAllowed = isHourAllowed && (!isAllowedMinuteCb.value || inputMinute.value === null || isAllowedMinuteCb.value(inputMinute.value));
      if (props.allowedSeconds instanceof Array) {
        cb = val => props.allowedSeconds.includes(val);
      } else {
        cb = props.allowedSeconds;
      }
      if (!props.min && !props.max) {
        return isMinuteAllowed ? cb : () => false;
      }
      const [minHour, minMinute, minSecond] = props.min ? props.min.split(':').map(Number) : [0, 0, 0];
      const [maxHour, maxMinute, maxSecond] = props.max ? props.max.split(':').map(Number) : [23, 59, 59];
      const minTime = minHour * 3600 + minMinute * 60 + (minSecond || 0) * 1;
      const maxTime = maxHour * 3600 + maxMinute * 60 + (maxSecond || 0) * 1;
      return val => {
        const time = 3600 * inputHour.value + 60 * inputMinute.value + val;
        return time >= minTime && time <= maxTime && isMinuteAllowed && (!cb || cb(val));
      };
    });
    const isAmPm = computed(() => {
      return props.format === 'ampm';
    });
    watch(() => props.modelValue, val => setInputData(val));
    onMounted(() => {
      setInputData(props.modelValue);
    });
    function genValue() {
      if (inputHour.value != null && inputMinute.value != null && (!props.useSeconds || inputSecond.value != null)) {
        return `${pad(inputHour.value)}:${pad(inputMinute.value)}` + (props.useSeconds ? `:${pad(inputSecond.value)}` : '');
      }
      return null;
    }
    function emitValue() {
      const value = genValue();
      if (value !== null) emit('update:modelValue', value);
    }
    function convert24to12(hour) {
      return hour ? (hour - 1) % 12 + 1 : 12;
    }
    function convert12to24(hour, period) {
      return hour % 12 + (period === 'pm' ? 12 : 0);
    }
    function setInputData(value) {
      if (value == null || value === '') {
        inputHour.value = null;
        inputMinute.value = null;
        inputSecond.value = null;
      } else if (value instanceof Date) {
        inputHour.value = value.getHours();
        inputMinute.value = value.getMinutes();
        inputSecond.value = value.getSeconds();
      } else {
        const [hour,, minute,, second, period] = value.trim().toLowerCase().match(/^(\d+):(\d+)(:(\d+))?([ap]m)?$/) || new Array(6);
        inputHour.value = period ? convert12to24(parseInt(hour, 10), period) : parseInt(hour, 10);
        inputMinute.value = parseInt(minute, 10);
        inputSecond.value = parseInt(second || 0, 10);
      }
      period.value = inputHour.value == null || inputHour.value < 12 ? 'am' : 'pm';
    }
    function firstAllowed(type, value) {
      const allowedFn = type === 'hour' ? isAllowedHourCb.value : type === 'minute' ? isAllowedMinuteCb.value : isAllowedSecondCb.value;
      if (!allowedFn) return value;

      // TODO: clean up (Note from V2 code)
      const range = type === 'minute' ? range60 : type === 'second' ? range60 : isAmPm.value ? value < 12 ? rangeHours12am : rangeHours12pm : rangeHours24;
      const first = range.find(v => allowedFn((v + value) % range.length + range[0]));
      return ((first || 0) + value) % range.length + range[0];
    }
    function setPeriod(val) {
      period.value = val;
      if (inputHour.value != null) {
        const newHour = inputHour.value + (period.value === 'am' ? -12 : 12);
        inputHour.value = firstAllowed('hour', newHour);
      }
      emit('update:period', val);
      emitValue();
      return true;
    }
    function onInput(value) {
      if (selecting.value === SelectingTimes.Hour) {
        inputHour.value = isAmPm.value ? convert12to24(value, period.value) : value;
      } else if (selecting.value === SelectingTimes.Minute) {
        inputMinute.value = value;
      } else {
        inputSecond.value = value;
      }
    }
    function onChange(value) {
      switch (selectingNames[selecting.value]) {
        case 'hour':
          emit('update:hour', value);
          break;
        case 'minute':
          emit('update:minute', value);
          break;
        case 'second':
          emit('update:second', value);
          break;
        default:
          break;
      }
      const emitChange = selecting.value === (props.useSeconds ? SelectingTimes.Second : SelectingTimes.Minute);
      if (selecting.value === SelectingTimes.Hour) {
        selecting.value = SelectingTimes.Minute;
      } else if (props.useSeconds && selecting.value === SelectingTimes.Minute) {
        selecting.value = SelectingTimes.Second;
      }
      if (inputHour.value === lazyInputHour.value && inputMinute.value === lazyInputMinute.value && (!props.useSeconds || inputSecond.value === lazyInputSecond.value)) return;
      const time = genValue();
      if (time === null) return;
      lazyInputHour.value = inputHour.value;
      lazyInputMinute.value = inputMinute.value;
      props.useSeconds && (lazyInputSecond.value = inputSecond.value);
      emitChange && emitValue();
    }
    useRender(() => {
      const pickerProps = VPicker.filterProps(props);
      const timePickerControlsProps = VTimePickerControls.filterProps(props);
      const timePickerClockProps = VTimePickerClock.filterProps(omit(props, ['format', 'modelValue', 'min', 'max']));
      return _createVNode(VPicker, _mergeProps(pickerProps, {
        "color": undefined,
        "class": ['v-time-picker', props.class],
        "style": props.style
      }), {
        title: () => slots.title?.() ?? _createVNode("div", {
          "class": "v-time-picker__title"
        }, [t(props.title)]),
        header: () => _createVNode(VTimePickerControls, _mergeProps(timePickerControlsProps, {
          "ampm": isAmPm.value || props.ampmInTitle,
          "ampmReadonly": isAmPm.value && !props.ampmInTitle,
          "hour": inputHour.value,
          "minute": inputMinute.value,
          "period": period.value,
          "second": inputSecond.value,
          "selecting": selecting.value,
          "onUpdate:period": val => setPeriod(val),
          "onUpdate:selecting": value => selecting.value = value,
          "ref": controlsRef
        }), null),
        default: () => _createVNode(VTimePickerClock, _mergeProps(timePickerClockProps, {
          "allowedValues": selecting.value === SelectingTimes.Hour ? isAllowedHourCb.value : selecting.value === SelectingTimes.Minute ? isAllowedMinuteCb.value : isAllowedSecondCb.value,
          "double": selecting.value === SelectingTimes.Hour && !isAmPm.value,
          "format": selecting.value === SelectingTimes.Hour ? isAmPm.value ? convert24to12 : val => val : val => pad(val, 2),
          "max": selecting.value === SelectingTimes.Hour ? isAmPm.value && period.value === 'am' ? 11 : 23 : 59,
          "min": selecting.value === SelectingTimes.Hour && isAmPm.value && period.value === 'pm' ? 12 : 0,
          "size": 20,
          "step": selecting.value === SelectingTimes.Hour ? 1 : 5,
          "modelValue": selecting.value === SelectingTimes.Hour ? inputHour.value : selecting.value === SelectingTimes.Minute ? inputMinute.value : inputSecond.value,
          "onChange": onChange,
          "onInput": onInput,
          "ref": clockRef
        }), null),
        actions: slots.actions
      });
    });
  }
});
//# sourceMappingURL=VTimePicker.mjs.map