// Composables
import { VItemGroupSymbol } from "./VItemGroup.mjs";
import { makeGroupItemProps, useGroupItem } from "../../composables/group.mjs"; // Utilities
import { genericComponent } from "../../util/index.mjs";
export const VItem = genericComponent()({
  name: 'VItem',
  props: makeGroupItemProps(),
  emits: {
    'group:selected': val => true
  },
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const {
      isSelected,
      select,
      toggle,
      selectedClass,
      value,
      disabled
    } = useGroupItem(props, VItemGroupSymbol);
    return () => slots.default?.({
      isSelected: isSelected.value,
      selectedClass: selectedClass.value,
      select,
      toggle,
      value: value.value,
      disabled: disabled.value
    });
  }
});
//# sourceMappingURL=VItem.mjs.map