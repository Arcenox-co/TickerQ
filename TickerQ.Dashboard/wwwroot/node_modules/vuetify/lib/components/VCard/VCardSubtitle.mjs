import { createVNode as _createVNode, resolveDirective as _resolveDirective } from "vue";
// Composables
import { makeComponentProps } from "../../composables/component.mjs";
import { makeTagProps } from "../../composables/tag.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVCardSubtitleProps = propsFactory({
  opacity: [Number, String],
  ...makeComponentProps(),
  ...makeTagProps()
}, 'VCardSubtitle');
export const VCardSubtitle = genericComponent()({
  name: 'VCardSubtitle',
  props: makeVCardSubtitleProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    useRender(() => _createVNode(props.tag, {
      "class": ['v-card-subtitle', props.class],
      "style": [{
        '--v-card-subtitle-opacity': props.opacity
      }, props.style]
    }, slots));
    return {};
  }
});
//# sourceMappingURL=VCardSubtitle.mjs.map