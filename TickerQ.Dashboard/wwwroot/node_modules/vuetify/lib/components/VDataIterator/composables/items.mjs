// Utilities
import { computed } from 'vue';
import { getPropertyFromItem, propsFactory } from "../../../util/index.mjs"; // Types
// Composables
export const makeDataIteratorItemsProps = propsFactory({
  items: {
    type: Array,
    default: () => []
  },
  itemValue: {
    type: [String, Array, Function],
    default: 'id'
  },
  itemSelectable: {
    type: [String, Array, Function],
    default: null
  },
  returnObject: Boolean
}, 'DataIterator-items');
export function transformItem(props, item) {
  const value = props.returnObject ? item : getPropertyFromItem(item, props.itemValue);
  const selectable = getPropertyFromItem(item, props.itemSelectable, true);
  return {
    type: 'item',
    value,
    selectable,
    raw: item
  };
}
export function transformItems(props, items) {
  const array = [];
  for (const item of items) {
    array.push(transformItem(props, item));
  }
  return array;
}
export function useDataIteratorItems(props) {
  const items = computed(() => transformItems(props, props.items));
  return {
    items
  };
}
//# sourceMappingURL=items.mjs.map