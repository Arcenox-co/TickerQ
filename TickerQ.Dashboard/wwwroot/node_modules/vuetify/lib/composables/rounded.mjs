// Utilities
import { computed, isRef } from 'vue';
import { getCurrentInstanceName, propsFactory } from "../util/index.mjs"; // Types
// Composables
export const makeRoundedProps = propsFactory({
  rounded: {
    type: [Boolean, Number, String],
    default: undefined
  },
  tile: Boolean
}, 'rounded');
export function useRounded(props) {
  let name = arguments.length > 1 && arguments[1] !== undefined ? arguments[1] : getCurrentInstanceName();
  const roundedClasses = computed(() => {
    const rounded = isRef(props) ? props.value : props.rounded;
    const tile = isRef(props) ? props.value : props.tile;
    const classes = [];
    if (rounded === true || rounded === '') {
      classes.push(`${name}--rounded`);
    } else if (typeof rounded === 'string' || rounded === 0) {
      for (const value of String(rounded).split(' ')) {
        classes.push(`rounded-${value}`);
      }
    } else if (tile || rounded === false) {
      classes.push('rounded-0');
    }
    return classes;
  });
  return {
    roundedClasses
  };
}
//# sourceMappingURL=rounded.mjs.map