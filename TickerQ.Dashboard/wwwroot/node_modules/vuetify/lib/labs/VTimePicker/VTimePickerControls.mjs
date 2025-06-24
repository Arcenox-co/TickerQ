import { createTextVNode as _createTextVNode, createVNode as _createVNode } from "vue";
// Styles
import "./VTimePickerControls.css";

// Components
import { pad } from "./util.mjs";
import { VBtn } from "../../components/VBtn/index.mjs"; // Composables
import { useLocale } from "../../composables/locale.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
import { SelectingTimes } from "./SelectingTimes.mjs";
export const makeVTimePickerControlsProps = propsFactory({
  ampm: Boolean,
  ampmInTitle: Boolean,
  ampmReadonly: Boolean,
  color: String,
  disabled: Boolean,
  hour: Number,
  minute: Number,
  second: Number,
  period: String,
  readonly: Boolean,
  useSeconds: Boolean,
  selecting: Number,
  value: Number
}, 'VTimePickerControls');
export const VTimePickerControls = genericComponent()({
  name: 'VTimePickerControls',
  props: makeVTimePickerControlsProps(),
  emits: {
    'update:period': data => true,
    'update:selecting': data => true
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    const {
      t
    } = useLocale();
    useRender(() => {
      let hour = props.hour;
      if (props.ampm) {
        hour = hour ? (hour - 1) % 12 + 1 : 12;
      }
      return _createVNode("div", {
        "class": "v-time-picker-controls"
      }, [_createVNode("div", {
        "class": {
          'v-time-picker-controls__time': true,
          'v-time-picker-controls__time--with-seconds': props.useSeconds
        }
      }, [_createVNode(VBtn, {
        "active": props.selecting === 1,
        "color": props.selecting === 1 ? props.color : undefined,
        "disabled": props.disabled,
        "variant": "tonal",
        "class": {
          'v-time-picker-controls__time__btn': true,
          'v-time-picker-controls__time--with-ampm__btn': props.ampm,
          'v-time-picker-controls__time--with-seconds__btn': props.useSeconds
        },
        "text": props.hour == null ? '--' : pad(`${hour}`),
        "onClick": () => emit('update:selecting', SelectingTimes.Hour)
      }, null), _createVNode("span", {
        "class": ['v-time-picker-controls__time__separator', {
          'v-time-picker-controls--with-seconds__time__separator': props.useSeconds
        }]
      }, [_createTextVNode(":")]), _createVNode(VBtn, {
        "active": props.selecting === 2,
        "color": props.selecting === 2 ? props.color : undefined,
        "class": {
          'v-time-picker-controls__time__btn': true,
          'v-time-picker-controls__time__btn__active': props.selecting === 2,
          'v-time-picker-controls__time--with-ampm__btn': props.ampm,
          'v-time-picker-controls__time--with-seconds__btn': props.useSeconds
        },
        "disabled": props.disabled,
        "variant": "tonal",
        "text": props.minute == null ? '--' : pad(props.minute),
        "onClick": () => emit('update:selecting', SelectingTimes.Minute)
      }, null), props.useSeconds && _createVNode("span", {
        "class": ['v-time-picker-controls__time__separator', {
          'v-time-picker-controls--with-seconds__time__separator': props.useSeconds
        }],
        "key": "secondsDivider"
      }, [_createTextVNode(":")]), props.useSeconds && _createVNode(VBtn, {
        "key": "secondsVal",
        "variant": "tonal",
        "onClick": () => emit('update:selecting', SelectingTimes.Second),
        "class": {
          'v-time-picker-controls__time__btn': true,
          'v-time-picker-controls__time__btn__active': props.selecting === 3,
          'v-time-picker-controls__time--with-seconds__btn': props.useSeconds
        },
        "disabled": props.disabled,
        "text": props.second == null ? '--' : pad(props.second)
      }, null), props.ampm && props.ampmInTitle && _createVNode("div", {
        "class": ['v-time-picker-controls__ampm', {
          'v-time-picker-controls__ampm--readonly': props.ampmReadonly
        }]
      }, [_createVNode(VBtn, {
        "active": props.period === 'am',
        "color": props.period === 'am' ? props.color : undefined,
        "class": {
          'v-time-picker-controls__ampm__am': true,
          'v-time-picker-controls__ampm__btn': true,
          'v-time-picker-controls__ampm__btn__active': props.period === 'am'
        },
        "disabled": props.disabled,
        "text": t('$vuetify.timePicker.am'),
        "variant": props.disabled && props.period === 'am' ? 'elevated' : 'tonal',
        "onClick": () => props.period !== 'am' ? emit('update:period', 'am') : null
      }, null), _createVNode(VBtn, {
        "active": props.period === 'pm',
        "color": props.period === 'pm' ? props.color : undefined,
        "class": {
          'v-time-picker-controls__ampm__pm': true,
          'v-time-picker-controls__ampm__btn': true,
          'v-time-picker-controls__ampm__btn__active': props.period === 'pm'
        },
        "disabled": props.disabled,
        "text": t('$vuetify.timePicker.pm'),
        "variant": props.disabled && props.period === 'pm' ? 'elevated' : 'tonal',
        "onClick": () => props.period !== 'pm' ? emit('update:period', 'pm') : null
      }, null)])])]);
    });
    return {};
  }
});
//# sourceMappingURL=VTimePickerControls.mjs.map