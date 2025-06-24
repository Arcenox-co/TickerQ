import { withDirectives as _withDirectives, createVNode as _createVNode, mergeProps as _mergeProps, resolveDirective as _resolveDirective, vShow as _vShow } from "vue";
// Styles
import "./VFab.css";

// Components
import { makeVBtnProps, VBtn } from "../VBtn/VBtn.mjs"; // Composables
import { makeLayoutItemProps, useLayoutItem } from "../../composables/layout.mjs";
import { makeLocationProps } from "../../composables/location.mjs";
import { useProxiedModel } from "../../composables/proxiedModel.mjs";
import { useResizeObserver } from "../../composables/resizeObserver.mjs";
import { useToggleScope } from "../../composables/toggleScope.mjs";
import { makeTransitionProps, MaybeTransition } from "../../composables/transition.mjs"; // Utilities
import { computed, ref, shallowRef, toRef, watchEffect } from 'vue';
import { genericComponent, omit, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVFabProps = propsFactory({
  app: Boolean,
  appear: Boolean,
  extended: Boolean,
  layout: Boolean,
  offset: Boolean,
  modelValue: {
    type: Boolean,
    default: true
  },
  ...omit(makeVBtnProps({
    active: true
  }), ['location']),
  ...makeLayoutItemProps(),
  ...makeLocationProps(),
  ...makeTransitionProps({
    transition: 'fab-transition'
  })
}, 'VFab');
export const VFab = genericComponent()({
  name: 'VFab',
  props: makeVFabProps(),
  emits: {
    'update:modelValue': value => true
  },
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const model = useProxiedModel(props, 'modelValue');
    const height = shallowRef(56);
    const layoutItemStyles = ref();
    const {
      resizeRef
    } = useResizeObserver(entries => {
      if (!entries.length) return;
      height.value = entries[0].target.clientHeight;
    });
    const hasPosition = computed(() => props.app || props.absolute);
    const position = computed(() => {
      if (!hasPosition.value) return false;
      return props.location?.split(' ').shift() ?? 'bottom';
    });
    const orientation = computed(() => {
      if (!hasPosition.value) return false;
      return props.location?.split(' ')[1] ?? 'end';
    });
    useToggleScope(() => props.app, () => {
      const layout = useLayoutItem({
        id: props.name,
        order: computed(() => parseInt(props.order, 10)),
        position,
        layoutSize: computed(() => props.layout ? height.value + 24 : 0),
        elementSize: computed(() => height.value + 24),
        active: computed(() => props.app && model.value),
        absolute: toRef(props, 'absolute')
      });
      watchEffect(() => {
        layoutItemStyles.value = layout.layoutItemStyles.value;
      });
    });
    const vFabRef = ref();
    useRender(() => {
      const btnProps = VBtn.filterProps(props);
      return _createVNode("div", {
        "ref": vFabRef,
        "class": ['v-fab', {
          'v-fab--absolute': props.absolute,
          'v-fab--app': !!props.app,
          'v-fab--extended': props.extended,
          'v-fab--offset': props.offset,
          [`v-fab--${position.value}`]: hasPosition.value,
          [`v-fab--${orientation.value}`]: hasPosition.value
        }, props.class],
        "style": [props.app ? {
          ...layoutItemStyles.value
        } : {
          height: 'inherit',
          width: undefined
        }, props.style]
      }, [_createVNode("div", {
        "class": "v-fab__container"
      }, [_createVNode(MaybeTransition, {
        "appear": props.appear,
        "transition": props.transition
      }, {
        default: () => [_withDirectives(_createVNode(VBtn, _mergeProps({
          "ref": resizeRef
        }, btnProps, {
          "active": undefined,
          "location": undefined
        }), slots), [[_vShow, props.active]])]
      })])]);
    });
    return {};
  }
});
//# sourceMappingURL=VFab.mjs.map