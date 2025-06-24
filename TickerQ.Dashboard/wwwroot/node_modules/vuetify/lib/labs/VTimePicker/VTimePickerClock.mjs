import { createVNode as _createVNode } from "vue";
// Styles
import "./VTimePickerClock.css";

// Composables
import { useBackgroundColor, useTextColor } from "../../composables/color.mjs"; // Utilities
import { computed, ref, toRef, watch } from 'vue';
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVTimePickerClockProps = propsFactory({
  allowedValues: Function,
  ampm: Boolean,
  color: String,
  disabled: Boolean,
  displayedValue: null,
  double: Boolean,
  format: {
    type: Function,
    default: val => val
  },
  max: {
    type: Number,
    required: true
  },
  min: {
    type: Number,
    required: true
  },
  scrollable: Boolean,
  readonly: Boolean,
  rotate: {
    type: Number,
    default: 0
  },
  step: {
    type: Number,
    default: 1
  },
  modelValue: {
    type: Number
  }
}, 'VTimePickerClock');
export const VTimePickerClock = genericComponent()({
  name: 'VTimePickerClock',
  props: makeVTimePickerClockProps(),
  emits: {
    change: val => true,
    input: val => true
  },
  setup(props, _ref) {
    let {
      emit
    } = _ref;
    const clockRef = ref(null);
    const innerClockRef = ref(null);
    const inputValue = ref(undefined);
    const isDragging = ref(false);
    const valueOnMouseDown = ref(null);
    const valueOnMouseUp = ref(null);
    const {
      textColorClasses,
      textColorStyles
    } = useTextColor(toRef(props, 'color'));
    const {
      backgroundColorClasses,
      backgroundColorStyles
    } = useBackgroundColor(toRef(props, 'color'));
    const count = computed(() => props.max - props.min + 1);
    const roundCount = computed(() => props.double ? count.value / 2 : count.value);
    const degreesPerUnit = computed(() => 360 / roundCount.value);
    const degrees = computed(() => degreesPerUnit.value * Math.PI / 180);
    const displayedValue = computed(() => props.modelValue == null ? props.min : props.modelValue);
    const innerRadiusScale = computed(() => 0.62);
    const genChildren = computed(() => {
      const children = [];
      for (let value = props.min; value <= props.max; value = value + props.step) {
        children.push(value);
      }
      return children;
    });
    watch(() => props.modelValue, val => {
      inputValue.value = val;
    });
    function update(value) {
      if (inputValue.value !== value) {
        inputValue.value = value;
      }
      emit('input', value);
    }
    function isAllowed(value) {
      return !props.allowedValues || props.allowedValues(value);
    }
    function wheel(e) {
      if (!props.scrollable || props.disabled) return;
      e.preventDefault();
      const delta = Math.sign(-e.deltaY || 1);
      let value = displayedValue.value;
      do {
        value = value + delta;
        value = (value - props.min + count.value) % count.value + props.min;
      } while (!isAllowed(value) && value !== displayedValue.value);
      if (value !== props.displayedValue) {
        update(value);
      }
    }
    function isInner(value) {
      return props.double && value - props.min >= roundCount.value;
    }
    function handScale(value) {
      return isInner(value) ? innerRadiusScale.value : 1;
    }
    function getPosition(value) {
      const rotateRadians = props.rotate * Math.PI / 180;
      return {
        x: Math.sin((value - props.min) * degrees.value + rotateRadians) * handScale(value),
        y: -Math.cos((value - props.min) * degrees.value + rotateRadians) * handScale(value)
      };
    }
    function angleToValue(angle, insideClick) {
      const value = (Math.round(angle / degreesPerUnit.value) + (insideClick ? roundCount.value : 0)) % count.value + props.min;

      // Necessary to fix edge case when selecting left part of the value(s) at 12 o'clock
      if (angle < 360 - degreesPerUnit.value / 2) return value;
      return insideClick ? props.max - roundCount.value + 1 : props.min;
    }
    function getTransform(i) {
      const {
        x,
        y
      } = getPosition(i);
      return {
        left: `${50 + x * 50}%`,
        top: `${50 + y * 50}%`
      };
    }
    function euclidean(p0, p1) {
      const dx = p1.x - p0.x;
      const dy = p1.y - p0.y;
      return Math.sqrt(dx * dx + dy * dy);
    }
    function angle(center, p1) {
      const value = 2 * Math.atan2(p1.y - center.y - euclidean(center, p1), p1.x - center.x);
      return Math.abs(value * 180 / Math.PI);
    }
    function setMouseDownValue(value) {
      if (valueOnMouseDown.value === null) {
        valueOnMouseDown.value = value;
      }
      valueOnMouseUp.value = value;
      update(value);
    }
    function onDragMove(e) {
      e.preventDefault();
      if (!isDragging.value && e.type !== 'click' || !clockRef.value) return;
      const {
        width,
        top,
        left
      } = clockRef.value?.getBoundingClientRect();
      const {
        width: innerWidth
      } = innerClockRef.value?.getBoundingClientRect() ?? {
        width: 0
      };
      const {
        clientX,
        clientY
      } = 'touches' in e ? e.touches[0] : e;
      const center = {
        x: width / 2,
        y: -width / 2
      };
      const coords = {
        x: clientX - left,
        y: top - clientY
      };
      const handAngle = Math.round(angle(center, coords) - props.rotate + 360) % 360;
      const insideClick = props.double && euclidean(center, coords) < (innerWidth + innerWidth * innerRadiusScale.value) / 4;
      const checksCount = Math.ceil(15 / degreesPerUnit.value);
      let value;
      for (let i = 0; i < checksCount; i++) {
        value = angleToValue(handAngle + i * degreesPerUnit.value, insideClick);
        if (isAllowed(value)) return setMouseDownValue(value);
        value = angleToValue(handAngle - i * degreesPerUnit.value, insideClick);
        if (isAllowed(value)) return setMouseDownValue(value);
      }
    }
    function onMouseDown(e) {
      if (props.disabled) return;
      e.preventDefault();
      window.addEventListener('mousemove', onDragMove);
      window.addEventListener('touchmove', onDragMove);
      window.addEventListener('mouseup', onMouseUp);
      window.addEventListener('touchend', onMouseUp);
      valueOnMouseDown.value = null;
      valueOnMouseUp.value = null;
      isDragging.value = true;
      onDragMove(e);
    }
    function onMouseUp(e) {
      e.stopPropagation();
      window.removeEventListener('mousemove', onDragMove);
      window.removeEventListener('touchmove', onDragMove);
      window.removeEventListener('mouseup', onMouseUp);
      window.removeEventListener('touchend', onMouseUp);
      isDragging.value = false;
      if (valueOnMouseUp.value !== null && isAllowed(valueOnMouseUp.value)) {
        emit('change', valueOnMouseUp.value);
      }
    }
    useRender(() => {
      return _createVNode("div", {
        "class": [{
          'v-time-picker-clock': true,
          'v-time-picker-clock--indeterminate': props.modelValue == null,
          'v-time-picker-clock--readonly': props.readonly
        }],
        "onMousedown": onMouseDown,
        "onTouchstart": onMouseDown,
        "onWheel": wheel,
        "ref": clockRef
      }, [_createVNode("div", {
        "class": "v-time-picker-clock__inner",
        "ref": innerClockRef
      }, [_createVNode("div", {
        "class": [{
          'v-time-picker-clock__hand': true,
          'v-time-picker-clock__hand--inner': isInner(props.modelValue)
        }, textColorClasses.value],
        "style": [{
          transform: `rotate(${props.rotate + degreesPerUnit.value * (displayedValue.value - props.min)}deg) scaleY(${handScale(displayedValue.value)})`
        }, textColorStyles.value]
      }, null), genChildren.value.map(value => {
        const isActive = value === displayedValue.value;
        return _createVNode("div", {
          "class": [{
            'v-time-picker-clock__item': true,
            'v-time-picker-clock__item--active': isActive,
            'v-time-picker-clock__item--disabled': props.disabled || !isAllowed(value)
          }, isActive && backgroundColorClasses.value],
          "style": [getTransform(value), isActive && backgroundColorStyles.value]
        }, [_createVNode("span", null, [props.format(value)])]);
      })])]);
    });
  }
});
//# sourceMappingURL=VTimePickerClock.mjs.map