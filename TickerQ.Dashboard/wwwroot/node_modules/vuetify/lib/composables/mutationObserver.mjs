// Utilities
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { refElement } from "../util/index.mjs"; // Types
export function useMutationObserver(handler, options) {
  const mutationRef = ref();
  const {
    once,
    immediate,
    ...optionKeys
  } = options || {};
  const defaultValue = !Object.keys(optionKeys).length;
  const observer = new MutationObserver((mutations, observer) => {
    handler?.(mutations, observer);
    if (options?.once) observer.disconnect();
  });
  onMounted(() => {
    if (!options?.immediate) return;
    handler?.([], observer);
  });
  onBeforeUnmount(() => {
    observer.disconnect();
  });
  watch(mutationRef, (newValue, oldValue) => {
    if (oldValue) observer.disconnect();
    const el = refElement(newValue);
    if (!el) return;
    observer.observe(el, {
      attributes: options?.attr ?? defaultValue,
      characterData: options?.char ?? defaultValue,
      childList: options?.child ?? defaultValue,
      subtree: options?.sub ?? defaultValue
    });
  }, {
    flush: 'post'
  });
  return {
    mutationRef
  };
}
//# sourceMappingURL=mutationObserver.mjs.map