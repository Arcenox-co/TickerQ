import { createVNode as _createVNode } from "vue";
// Styles
import "./VLabel.css";

// Composables
import { makeComponentProps } from "../../composables/component.mjs";
import { makeThemeProps } from "../../composables/theme.mjs"; // Utilities
import { EventProp, genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVLabelProps = propsFactory({
  text: String,
  onClick: EventProp(),
  ...makeComponentProps(),
  ...makeThemeProps()
}, 'VLabel');
export const VLabel = genericComponent()({
  name: 'VLabel',
  props: makeVLabelProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    useRender(() => _createVNode("label", {
      "class": ['v-label', {
        'v-label--clickable': !!props.onClick
      }, props.class],
      "style": props.style,
      "onClick": props.onClick
    }, [props.text, slots.default?.()]));
    return {};
  }
});
//# sourceMappingURL=VLabel.mjs.map