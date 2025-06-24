import { createVNode as _createVNode, resolveDirective as _resolveDirective } from "vue";
// Composables
import { makeComponentProps } from "../../composables/component.mjs";
import { makeTagProps } from "../../composables/tag.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVListItemSubtitleProps = propsFactory({
  opacity: [Number, String],
  ...makeComponentProps(),
  ...makeTagProps()
}, 'VListItemSubtitle');
export const VListItemSubtitle = genericComponent()({
  name: 'VListItemSubtitle',
  props: makeVListItemSubtitleProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    useRender(() => _createVNode(props.tag, {
      "class": ['v-list-item-subtitle', props.class],
      "style": [{
        '--v-list-item-subtitle-opacity': props.opacity
      }, props.style]
    }, slots));
    return {};
  }
});
//# sourceMappingURL=VListItemSubtitle.mjs.map