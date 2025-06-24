import { mergeProps as _mergeProps, Fragment as _Fragment, createVNode as _createVNode } from "vue";
// Styles
import "./VNumberInput.css";

// Components
import { VBtn } from "../../components/VBtn/index.mjs";
import { VDefaultsProvider } from "../../components/VDefaultsProvider/index.mjs";
import { VDivider } from "../../components/VDivider/index.mjs";
import { makeVTextFieldProps, VTextField } from "../../components/VTextField/VTextField.mjs"; // Composables
import { useForm } from "../../composables/form.mjs";
import { forwardRefs } from "../../composables/forwardRefs.mjs";
import { useProxiedModel } from "../../composables/proxiedModel.mjs"; // Utilities
import { computed, nextTick, onMounted, ref } from 'vue';
import { clamp, genericComponent, getDecimals, omit, propsFactory, useRender } from "../../util/index.mjs"; // Types
const makeVNumberInputProps = propsFactory({
  controlVariant: {
    type: String,
    default: 'default'
  },
  inset: Boolean,
  hideInput: Boolean,
  modelValue: {
    type: Number,
    default: null
  },
  min: {
    type: Number,
    default: Number.MIN_SAFE_INTEGER
  },
  max: {
    type: Number,
    default: Number.MAX_SAFE_INTEGER
  },
  step: {
    type: Number,
    default: 1
  },
  ...omit(makeVTextFieldProps({}), ['appendInnerIcon', 'modelValue', 'prependInnerIcon'])
}, 'VNumberInput');
export const VNumberInput = genericComponent()({
  name: 'VNumberInput',
  props: {
    ...makeVNumberInputProps()
  },
  emits: {
    'update:modelValue': val => true
  },
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const _model = useProxiedModel(props, 'modelValue');
    const model = computed({
      get: () => _model.value,
      // model.value could be empty string from VTextField
      // but _model.value should be eventually kept in type Number | null
      set(val) {
        if (val === null || val === '') {
          _model.value = null;
          return;
        }
        const value = Number(val);
        if (!isNaN(value) && value <= props.max && value >= props.min) {
          _model.value = value;
        }
      }
    });
    const vTextFieldRef = ref();
    const stepDecimals = computed(() => getDecimals(props.step));
    const modelDecimals = computed(() => typeof model.value === 'number' ? getDecimals(model.value) : 0);
    const form = useForm(props);
    const controlsDisabled = computed(() => form.isDisabled.value || form.isReadonly.value);
    const canIncrease = computed(() => {
      if (controlsDisabled.value) return false;
      return (model.value ?? 0) + props.step <= props.max;
    });
    const canDecrease = computed(() => {
      if (controlsDisabled.value) return false;
      return (model.value ?? 0) - props.step >= props.min;
    });
    const controlVariant = computed(() => {
      return props.hideInput ? 'stacked' : props.controlVariant;
    });
    const incrementIcon = computed(() => controlVariant.value === 'split' ? '$plus' : '$collapse');
    const decrementIcon = computed(() => controlVariant.value === 'split' ? '$minus' : '$expand');
    const controlNodeSize = computed(() => controlVariant.value === 'split' ? 'default' : 'small');
    const controlNodeDefaultHeight = computed(() => controlVariant.value === 'stacked' ? 'auto' : '100%');
    const incrementSlotProps = computed(() => ({
      click: onClickUp
    }));
    const decrementSlotProps = computed(() => ({
      click: onClickDown
    }));
    onMounted(() => {
      if (!controlsDisabled.value) {
        clampModel();
      }
    });
    function toggleUpDown() {
      let increment = arguments.length > 0 && arguments[0] !== undefined ? arguments[0] : true;
      if (controlsDisabled.value) return;
      if (model.value == null) {
        model.value = clamp(0, props.min, props.max);
        return;
      }
      const decimals = Math.max(modelDecimals.value, stepDecimals.value);
      if (increment) {
        if (canIncrease.value) model.value = +(model.value + props.step).toFixed(decimals);
      } else {
        if (canDecrease.value) model.value = +(model.value - props.step).toFixed(decimals);
      }
    }
    function onClickUp(e) {
      e.stopPropagation();
      toggleUpDown();
    }
    function onClickDown(e) {
      e.stopPropagation();
      toggleUpDown(false);
    }
    function onBeforeinput(e) {
      if (!e.data) return;
      const existingTxt = e.target?.value;
      const selectionStart = e.target?.selectionStart;
      const selectionEnd = e.target?.selectionEnd;
      const potentialNewInputVal = existingTxt ? existingTxt.slice(0, selectionStart) + e.data + existingTxt.slice(selectionEnd) : e.data;
      // Only numbers, "-", "." are allowed
      // AND "-", "." are allowed only once
      // AND "-" is only allowed at the start
      if (!/^-?(\d+(\.\d*)?|(\.\d+)|\d*|\.)$/.test(potentialNewInputVal)) {
        e.preventDefault();
      }
    }
    async function onKeydown(e) {
      if (['Enter', 'ArrowLeft', 'ArrowRight', 'Backspace', 'Delete', 'Tab'].includes(e.key) || e.ctrlKey) return;
      if (['ArrowDown', 'ArrowUp'].includes(e.key)) {
        e.preventDefault();
        clampModel();
        // _model is controlled, so need to wait until props['modelValue'] is updated
        await nextTick();
        if (e.key === 'ArrowDown') {
          toggleUpDown(false);
        } else {
          toggleUpDown();
        }
      }
    }
    function onControlMousedown(e) {
      e.stopPropagation();
    }
    function clampModel() {
      if (!vTextFieldRef.value) return;
      const inputText = vTextFieldRef.value.value;
      if (inputText && !isNaN(+inputText)) {
        model.value = clamp(+inputText, props.min, props.max);
      } else {
        model.value = null;
      }
    }
    useRender(() => {
      const {
        modelValue: _,
        ...textFieldProps
      } = VTextField.filterProps(props);
      function incrementControlNode() {
        return !slots.increment ? _createVNode(VBtn, {
          "disabled": !canIncrease.value,
          "flat": true,
          "key": "increment-btn",
          "height": controlNodeDefaultHeight.value,
          "data-testid": "increment",
          "aria-hidden": "true",
          "icon": incrementIcon.value,
          "onClick": onClickUp,
          "onMousedown": onControlMousedown,
          "size": controlNodeSize.value,
          "tabindex": "-1"
        }, null) : _createVNode(VDefaultsProvider, {
          "key": "increment-defaults",
          "defaults": {
            VBtn: {
              disabled: !canIncrease.value,
              flat: true,
              height: controlNodeDefaultHeight.value,
              size: controlNodeSize.value,
              icon: incrementIcon.value
            }
          }
        }, {
          default: () => [slots.increment(incrementSlotProps.value)]
        });
      }
      function decrementControlNode() {
        return !slots.decrement ? _createVNode(VBtn, {
          "disabled": !canDecrease.value,
          "flat": true,
          "key": "decrement-btn",
          "height": controlNodeDefaultHeight.value,
          "data-testid": "decrement",
          "aria-hidden": "true",
          "icon": decrementIcon.value,
          "size": controlNodeSize.value,
          "tabindex": "-1",
          "onClick": onClickDown,
          "onMousedown": onControlMousedown
        }, null) : _createVNode(VDefaultsProvider, {
          "key": "decrement-defaults",
          "defaults": {
            VBtn: {
              disabled: !canDecrease.value,
              flat: true,
              height: controlNodeDefaultHeight.value,
              size: controlNodeSize.value,
              icon: decrementIcon.value
            }
          }
        }, {
          default: () => [slots.decrement(decrementSlotProps.value)]
        });
      }
      function controlNode() {
        return _createVNode("div", {
          "class": "v-number-input__control"
        }, [decrementControlNode(), _createVNode(VDivider, {
          "vertical": controlVariant.value !== 'stacked'
        }, null), incrementControlNode()]);
      }
      function dividerNode() {
        return !props.hideInput && !props.inset ? _createVNode(VDivider, {
          "vertical": true
        }, null) : undefined;
      }
      const appendInnerControl = controlVariant.value === 'split' ? _createVNode("div", {
        "class": "v-number-input__control"
      }, [_createVNode(VDivider, {
        "vertical": true
      }, null), incrementControlNode()]) : props.reverse ? undefined : _createVNode(_Fragment, null, [dividerNode(), controlNode()]);
      const hasAppendInner = slots['append-inner'] || appendInnerControl;
      const prependInnerControl = controlVariant.value === 'split' ? _createVNode("div", {
        "class": "v-number-input__control"
      }, [decrementControlNode(), _createVNode(VDivider, {
        "vertical": true
      }, null)]) : props.reverse ? _createVNode(_Fragment, null, [controlNode(), dividerNode()]) : undefined;
      const hasPrependInner = slots['prepend-inner'] || prependInnerControl;
      return _createVNode(VTextField, _mergeProps({
        "ref": vTextFieldRef,
        "modelValue": model.value,
        "onUpdate:modelValue": $event => model.value = $event,
        "onBeforeinput": onBeforeinput,
        "onChange": clampModel,
        "onKeydown": onKeydown,
        "class": ['v-number-input', {
          'v-number-input--default': controlVariant.value === 'default',
          'v-number-input--hide-input': props.hideInput,
          'v-number-input--inset': props.inset,
          'v-number-input--reverse': props.reverse,
          'v-number-input--split': controlVariant.value === 'split',
          'v-number-input--stacked': controlVariant.value === 'stacked'
        }, props.class]
      }, textFieldProps, {
        "style": props.style,
        "inputmode": "decimal"
      }), {
        ...slots,
        'append-inner': hasAppendInner ? function () {
          for (var _len = arguments.length, args = new Array(_len), _key = 0; _key < _len; _key++) {
            args[_key] = arguments[_key];
          }
          return _createVNode(_Fragment, null, [slots['append-inner']?.(...args), appendInnerControl]);
        } : undefined,
        'prepend-inner': hasPrependInner ? function () {
          for (var _len2 = arguments.length, args = new Array(_len2), _key2 = 0; _key2 < _len2; _key2++) {
            args[_key2] = arguments[_key2];
          }
          return _createVNode(_Fragment, null, [prependInnerControl, slots['prepend-inner']?.(...args)]);
        } : undefined
      });
    });
    return forwardRefs({}, vTextFieldRef);
  }
});
//# sourceMappingURL=VNumberInput.mjs.map