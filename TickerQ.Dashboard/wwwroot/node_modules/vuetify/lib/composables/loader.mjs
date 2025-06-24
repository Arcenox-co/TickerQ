import { createVNode as _createVNode } from "vue";
// Components
import { VProgressLinear } from "../components/VProgressLinear/index.mjs"; // Utilities
import { computed } from 'vue';
import { getCurrentInstanceName, propsFactory } from "../util/index.mjs"; // Types
// Composables
export const makeLoaderProps = propsFactory({
  loading: [Boolean, String]
}, 'loader');
export function useLoader(props) {
  let name = arguments.length > 1 && arguments[1] !== undefined ? arguments[1] : getCurrentInstanceName();
  const loaderClasses = computed(() => ({
    [`${name}--loading`]: props.loading
  }));
  return {
    loaderClasses
  };
}
export function LoaderSlot(props, _ref) {
  let {
    slots
  } = _ref;
  return _createVNode("div", {
    "class": `${props.name}__loader`
  }, [slots.default?.({
    color: props.color,
    isActive: props.active
  }) || _createVNode(VProgressLinear, {
    "absolute": props.absolute,
    "active": props.active,
    "color": props.color,
    "height": "2",
    "indeterminate": true
  }, null)]);
}
//# sourceMappingURL=loader.mjs.map