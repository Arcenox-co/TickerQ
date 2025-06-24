// Utilities
import { computed } from 'vue';
import { getCurrentInstanceName, propsFactory } from "../util/index.mjs"; // Types
const allowedDensities = [null, 'default', 'comfortable', 'compact'];

// typeof allowedDensities[number] evalutes to any
// when generating api types for whatever reason.

// Composables
export const makeDensityProps = propsFactory({
  density: {
    type: String,
    default: 'default',
    validator: v => allowedDensities.includes(v)
  }
}, 'density');
export function useDensity(props) {
  let name = arguments.length > 1 && arguments[1] !== undefined ? arguments[1] : getCurrentInstanceName();
  const densityClasses = computed(() => {
    return `${name}--density-${props.density}`;
  });
  return {
    densityClasses
  };
}
//# sourceMappingURL=density.mjs.map