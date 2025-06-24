// Composables
import { makeGroupItemProps, useGroupItem } from "../../composables/group.mjs"; // Utilities
import { VSlideGroupSymbol } from "./VSlideGroup.mjs";
import { genericComponent } from "../../util/index.mjs"; // Types
export const VSlideGroupItem = genericComponent()({
  name: 'VSlideGroupItem',
  props: makeGroupItemProps(),
  emits: {
    'group:selected': val => true
  },
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const slideGroupItem = useGroupItem(props, VSlideGroupSymbol);
    return () => slots.default?.({
      isSelected: slideGroupItem.isSelected.value,
      select: slideGroupItem.select,
      toggle: slideGroupItem.toggle,
      selectedClass: slideGroupItem.selectedClass.value
    });
  }
});
//# sourceMappingURL=VSlideGroupItem.mjs.map