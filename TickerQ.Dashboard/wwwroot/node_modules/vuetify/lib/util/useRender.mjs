// Utilities
import { getCurrentInstance } from "./getCurrentInstance.mjs"; // Types
export function useRender(render) {
  const vm = getCurrentInstance('useRender');
  vm.render = render;
}
//# sourceMappingURL=useRender.mjs.map