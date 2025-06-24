// Utilities
import { propsFactory } from "../util/propsFactory.mjs"; // Types
// Composables
export const makeComponentProps = propsFactory({
  class: [String, Array, Object],
  style: {
    type: [String, Array, Object],
    default: null
  }
}, 'component');
//# sourceMappingURL=component.mjs.map