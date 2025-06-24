// Utilities
import { computed, inject, provide, shallowRef } from 'vue';

// Types

// Depth
export const DepthKey = Symbol.for('vuetify:depth');
export function useDepth(hasPrepend) {
  const parent = inject(DepthKey, shallowRef(-1));
  const depth = computed(() => parent.value + 1 + (hasPrepend?.value ? 1 : 0));
  provide(DepthKey, depth);
  return depth;
}

// List
export const ListKey = Symbol.for('vuetify:list');
export function createList() {
  const parent = inject(ListKey, {
    hasPrepend: shallowRef(false),
    updateHasPrepend: () => null
  });
  const data = {
    hasPrepend: shallowRef(false),
    updateHasPrepend: value => {
      if (value) data.hasPrepend.value = value;
    }
  };
  provide(ListKey, data);
  return parent;
}
export function useList() {
  return inject(ListKey, null);
}
//# sourceMappingURL=list.mjs.map