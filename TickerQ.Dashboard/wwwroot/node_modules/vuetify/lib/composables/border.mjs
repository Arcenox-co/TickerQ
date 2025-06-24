// Utilities
import { computed, isRef } from 'vue';
import { getCurrentInstanceName, propsFactory } from "../util/index.mjs"; // Types
// Composables
export const makeBorderProps = propsFactory({
  border: [Boolean, Number, String]
}, 'border');
export function useBorder(props) {
  let name = arguments.length > 1 && arguments[1] !== undefined ? arguments[1] : getCurrentInstanceName();
  const borderClasses = computed(() => {
    const border = isRef(props) ? props.value : props.border;
    const classes = [];
    if (border === true || border === '') {
      classes.push(`${name}--border`);
    } else if (typeof border === 'string' || border === 0) {
      for (const value of String(border).split(' ')) {
        classes.push(`border-${value}`);
      }
    }
    return classes;
  });
  return {
    borderClasses
  };
}
//# sourceMappingURL=border.mjs.map