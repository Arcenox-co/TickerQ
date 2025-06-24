// Utilities
import { getCurrentInstance } from "../util/index.mjs";
export function useScopeId() {
  const vm = getCurrentInstance('useScopeId');
  const scopeId = vm.vnode.scopeId;
  return {
    scopeId: scopeId ? {
      [scopeId]: ''
    } : undefined
  };
}
//# sourceMappingURL=scopeId.mjs.map