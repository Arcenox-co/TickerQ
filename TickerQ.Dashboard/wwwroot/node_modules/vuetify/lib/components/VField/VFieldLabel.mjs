import { createVNode as _createVNode, resolveDirective as _resolveDirective } from "vue";
// Components
import { VLabel } from "../VLabel/index.mjs"; // Composables
import { makeComponentProps } from "../../composables/component.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVFieldLabelProps = propsFactory({
  floating: Boolean,
  ...makeComponentProps()
}, 'VFieldLabel');
export const VFieldLabel = genericComponent()({
  name: 'VFieldLabel',
  props: makeVFieldLabelProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    useRender(() => _createVNode(VLabel, {
      "class": ['v-field-label', {
        'v-field-label--floating': props.floating
      }, props.class],
      "style": props.style,
      "aria-hidden": props.floating || undefined
    }, slots));
    return {};
  }
});
//# sourceMappingURL=VFieldLabel.mjs.map