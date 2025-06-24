// Utilities
import { computed, watch } from 'vue';
import { deepEqual, getCurrentInstance } from "../../../util/index.mjs"; // Types
export function useOptions(_ref) {
  let {
    page,
    itemsPerPage,
    sortBy,
    groupBy,
    search
  } = _ref;
  const vm = getCurrentInstance('VDataTable');
  const options = computed(() => ({
    page: page.value,
    itemsPerPage: itemsPerPage.value,
    sortBy: sortBy.value,
    groupBy: groupBy.value,
    search: search.value
  }));
  let oldOptions = null;
  watch(options, () => {
    if (deepEqual(oldOptions, options.value)) return;

    // Reset page when searching
    if (oldOptions && oldOptions.search !== options.value.search) {
      page.value = 1;
    }
    vm.emit('update:options', options.value);
    oldOptions = options.value;
  }, {
    deep: true,
    immediate: true
  });
}
//# sourceMappingURL=options.mjs.map