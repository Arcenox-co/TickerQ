import { withDirectives as _withDirectives, createVNode as _createVNode, vShow as _vShow } from "vue";
// Styles
import "./VCounter.css";

// Components
import { VSlideYTransition } from "../transitions/index.mjs"; // Composables
import { makeComponentProps } from "../../composables/component.mjs";
import { makeTransitionProps, MaybeTransition } from "../../composables/transition.mjs"; // Utilities
import { computed } from 'vue';
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVCounterProps = propsFactory({
  active: Boolean,
  disabled: Boolean,
  max: [Number, String],
  value: {
    type: [Number, String],
    default: 0
  },
  ...makeComponentProps(),
  ...makeTransitionProps({
    transition: {
      component: VSlideYTransition
    }
  })
}, 'VCounter');
export const VCounter = genericComponent()({
  name: 'VCounter',
  functional: true,
  props: makeVCounterProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const counter = computed(() => {
      return props.max ? `${props.value} / ${props.max}` : String(props.value);
    });
    useRender(() => _createVNode(MaybeTransition, {
      "transition": props.transition
    }, {
      default: () => [_withDirectives(_createVNode("div", {
        "class": ['v-counter', {
          'text-error': props.max && !props.disabled && parseFloat(props.value) > parseFloat(props.max)
        }, props.class],
        "style": props.style
      }, [slots.default ? slots.default({
        counter: counter.value,
        max: props.max,
        value: props.value
      }) : counter.value]), [[_vShow, props.active]])]
    }));
    return {};
  }
});
//# sourceMappingURL=VCounter.mjs.map