// Utilities
import { onBeforeUnmount, ref, shallowRef, watch } from 'vue';
import { SUPPORTS_INTERSECTION } from "../util/index.mjs";
export function useIntersectionObserver(callback, options) {
  const intersectionRef = ref();
  const isIntersecting = shallowRef(false);
  if (SUPPORTS_INTERSECTION) {
    const observer = new IntersectionObserver(entries => {
      callback?.(entries, observer);
      isIntersecting.value = !!entries.find(entry => entry.isIntersecting);
    }, options);
    onBeforeUnmount(() => {
      observer.disconnect();
    });
    watch(intersectionRef, (newValue, oldValue) => {
      if (oldValue) {
        observer.unobserve(oldValue);
        isIntersecting.value = false;
      }
      if (newValue) observer.observe(newValue);
    }, {
      flush: 'post'
    });
  }
  return {
    intersectionRef,
    isIntersecting
  };
}
//# sourceMappingURL=intersectionObserver.mjs.map