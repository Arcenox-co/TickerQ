// Utilities
import { effectScope, onScopeDispose, watch } from 'vue';

// Types

export function useToggleScope(source, fn) {
  let scope;
  function start() {
    scope = effectScope();
    scope.run(() => fn.length ? fn(() => {
      scope?.stop();
      start();
    }) : fn());
  }
  watch(source, active => {
    if (active && !scope) {
      start();
    } else if (!active) {
      scope?.stop();
      scope = undefined;
    }
  }, {
    immediate: true
  });
  onScopeDispose(() => {
    scope?.stop();
  });
}
//# sourceMappingURL=toggleScope.mjs.map