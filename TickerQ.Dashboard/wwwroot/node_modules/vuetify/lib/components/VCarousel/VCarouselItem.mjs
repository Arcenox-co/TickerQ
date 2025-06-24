import { createVNode as _createVNode, mergeProps as _mergeProps, resolveDirective as _resolveDirective } from "vue";
// Components
import { makeVImgProps, VImg } from "../VImg/VImg.mjs";
import { makeVWindowItemProps, VWindowItem } from "../VWindow/VWindowItem.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVCarouselItemProps = propsFactory({
  ...makeVImgProps(),
  ...makeVWindowItemProps()
}, 'VCarouselItem');
export const VCarouselItem = genericComponent()({
  name: 'VCarouselItem',
  inheritAttrs: false,
  props: makeVCarouselItemProps(),
  setup(props, _ref) {
    let {
      slots,
      attrs
    } = _ref;
    useRender(() => {
      const imgProps = VImg.filterProps(props);
      const windowItemProps = VWindowItem.filterProps(props);
      return _createVNode(VWindowItem, _mergeProps({
        "class": ['v-carousel-item', props.class]
      }, windowItemProps), {
        default: () => [_createVNode(VImg, _mergeProps(attrs, imgProps), slots)]
      });
    });
  }
});
//# sourceMappingURL=VCarouselItem.mjs.map