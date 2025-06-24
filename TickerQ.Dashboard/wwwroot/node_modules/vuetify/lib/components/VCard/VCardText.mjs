import { createVNode as _createVNode, resolveDirective as _resolveDirective } from "vue";
// Composables
import { makeComponentProps } from "../../composables/component.mjs";
import { makeTagProps } from "../../composables/tag.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVCardTextProps = propsFactory({
  opacity: [Number, String],
  ...makeComponentProps(),
  ...makeTagProps()
}, 'VCardText');
export const VCardText = genericComponent()({
  name: 'VCardText',
  props: makeVCardTextProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    useRender(() => _createVNode(props.tag, {
      "class": ['v-card-text', props.class],
      "style": [{
        '--v-card-text-opacity': props.opacity
      }, props.style]
    }, slots));
    return {};
  }
});
//# sourceMappingURL=VCardText.mjs.map