import { createVNode as _createVNode } from "vue";
// Styles
import "./VLayoutItem.css";

// Composables
import { makeComponentProps } from "../../composables/component.mjs";
import { makeLayoutItemProps, useLayoutItem } from "../../composables/layout.mjs"; // Utilities
import { computed, toRef } from 'vue';
import { genericComponent, propsFactory } from "../../util/index.mjs"; // Types
export const makeVLayoutItemProps = propsFactory({
  position: {
    type: String,
    required: true
  },
  size: {
    type: [Number, String],
    default: 300
  },
  modelValue: Boolean,
  ...makeComponentProps(),
  ...makeLayoutItemProps()
}, 'VLayoutItem');
export const VLayoutItem = genericComponent()({
  name: 'VLayoutItem',
  props: makeVLayoutItemProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const {
      layoutItemStyles
    } = useLayoutItem({
      id: props.name,
      order: computed(() => parseInt(props.order, 10)),
      position: toRef(props, 'position'),
      elementSize: toRef(props, 'size'),
      layoutSize: toRef(props, 'size'),
      active: toRef(props, 'modelValue'),
      absolute: toRef(props, 'absolute')
    });
    return () => _createVNode("div", {
      "class": ['v-layout-item', props.class],
      "style": [layoutItemStyles.value, props.style]
    }, [slots.default?.()]);
  }
});
//# sourceMappingURL=VLayoutItem.mjs.map