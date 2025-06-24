import { withDirectives as _withDirectives, resolveDirective as _resolveDirective, createVNode as _createVNode } from "vue";
// Composables
import { makeComponentProps } from "../../composables/component.mjs";
import { makeDimensionProps, useDimension } from "../../composables/dimensions.mjs";
import { useProxiedModel } from "../../composables/proxiedModel.mjs";
import { makeTagProps } from "../../composables/tag.mjs";
import { makeTransitionProps, MaybeTransition } from "../../composables/transition.mjs"; // Directives
import intersect from "../../directives/intersect/index.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVLazyProps = propsFactory({
  modelValue: Boolean,
  options: {
    type: Object,
    // For more information on types, navigate to:
    // https://developer.mozilla.org/en-US/docs/Web/API/Intersection_Observer_API
    default: () => ({
      root: undefined,
      rootMargin: undefined,
      threshold: undefined
    })
  },
  ...makeComponentProps(),
  ...makeDimensionProps(),
  ...makeTagProps(),
  ...makeTransitionProps({
    transition: 'fade-transition'
  })
}, 'VLazy');
export const VLazy = genericComponent()({
  name: 'VLazy',
  directives: {
    intersect
  },
  props: makeVLazyProps(),
  emits: {
    'update:modelValue': value => true
  },
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const {
      dimensionStyles
    } = useDimension(props);
    const isActive = useProxiedModel(props, 'modelValue');
    function onIntersect(isIntersecting) {
      if (isActive.value) return;
      isActive.value = isIntersecting;
    }
    useRender(() => _withDirectives(_createVNode(props.tag, {
      "class": ['v-lazy', props.class],
      "style": [dimensionStyles.value, props.style]
    }, {
      default: () => [isActive.value && _createVNode(MaybeTransition, {
        "transition": props.transition,
        "appear": true
      }, {
        default: () => [slots.default?.()]
      })]
    }), [[_resolveDirective("intersect"), {
      handler: onIntersect,
      options: props.options
    }, null]]));
    return {};
  }
});
//# sourceMappingURL=VLazy.mjs.map