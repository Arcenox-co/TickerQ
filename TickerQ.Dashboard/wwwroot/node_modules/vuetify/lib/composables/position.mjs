// Utilities
import { computed } from 'vue';
import { getCurrentInstanceName, propsFactory } from "../util/index.mjs"; // Types
const positionValues = ['static', 'relative', 'fixed', 'absolute', 'sticky'];
// Composables
export const makePositionProps = propsFactory({
  position: {
    type: String,
    validator: /* istanbul ignore next */v => positionValues.includes(v)
  }
}, 'position');
export function usePosition(props) {
  let name = arguments.length > 1 && arguments[1] !== undefined ? arguments[1] : getCurrentInstanceName();
  const positionClasses = computed(() => {
    return props.position ? `${name}--${props.position}` : undefined;
  });
  return {
    positionClasses
  };
}
//# sourceMappingURL=position.mjs.map