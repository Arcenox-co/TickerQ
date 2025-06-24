import { createVNode as _createVNode, mergeProps as _mergeProps } from "vue";
// Styles
import "./VBtnToggle.css";

// Components
import { makeVBtnGroupProps, VBtnGroup } from "../VBtnGroup/VBtnGroup.mjs"; // Composables
import { makeGroupProps, useGroup } from "../../composables/group.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const VBtnToggleSymbol = Symbol.for('vuetify:v-btn-toggle');
export const makeVBtnToggleProps = propsFactory({
  ...makeVBtnGroupProps(),
  ...makeGroupProps()
}, 'VBtnToggle');
export const VBtnToggle = genericComponent()({
  name: 'VBtnToggle',
  props: makeVBtnToggleProps(),
  emits: {
    'update:modelValue': value => true
  },
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const {
      isSelected,
      next,
      prev,
      select,
      selected
    } = useGroup(props, VBtnToggleSymbol);
    useRender(() => {
      const btnGroupProps = VBtnGroup.filterProps(props);
      return _createVNode(VBtnGroup, _mergeProps({
        "class": ['v-btn-toggle', props.class]
      }, btnGroupProps, {
        "style": props.style
      }), {
        default: () => [slots.default?.({
          isSelected,
          next,
          prev,
          select,
          selected
        })]
      });
    });
    return {
      next,
      prev,
      select
    };
  }
});
//# sourceMappingURL=VBtnToggle.mjs.map