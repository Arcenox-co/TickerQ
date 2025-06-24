// Composables
import { useProxiedModel } from "../../../composables/proxiedModel.mjs"; // Utilities
import { inject, provide, toRef } from 'vue';
import { propsFactory } from "../../../util/index.mjs"; // Types
export const makeDataTableExpandProps = propsFactory({
  expandOnClick: Boolean,
  showExpand: Boolean,
  expanded: {
    type: Array,
    default: () => []
  }
}, 'DataTable-expand');
export const VDataTableExpandedKey = Symbol.for('vuetify:datatable:expanded');
export function provideExpanded(props) {
  const expandOnClick = toRef(props, 'expandOnClick');
  const expanded = useProxiedModel(props, 'expanded', props.expanded, v => {
    return new Set(v);
  }, v => {
    return [...v.values()];
  });
  function expand(item, value) {
    const newExpanded = new Set(expanded.value);
    if (!value) {
      newExpanded.delete(item.value);
    } else {
      newExpanded.add(item.value);
    }
    expanded.value = newExpanded;
  }
  function isExpanded(item) {
    return expanded.value.has(item.value);
  }
  function toggleExpand(item) {
    expand(item, !isExpanded(item));
  }
  const data = {
    expand,
    expanded,
    expandOnClick,
    isExpanded,
    toggleExpand
  };
  provide(VDataTableExpandedKey, data);
  return data;
}
export function useExpanded() {
  const data = inject(VDataTableExpandedKey);
  if (!data) throw new Error('foo');
  return data;
}
//# sourceMappingURL=expand.mjs.map