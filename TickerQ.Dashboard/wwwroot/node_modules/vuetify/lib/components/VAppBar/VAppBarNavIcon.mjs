import { createVNode as _createVNode, mergeProps as _mergeProps, resolveDirective as _resolveDirective } from "vue";
// Components
import { makeVBtnProps, VBtn } from "../VBtn/VBtn.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVAppBarNavIconProps = propsFactory({
  ...makeVBtnProps({
    icon: '$menu',
    variant: 'text'
  })
}, 'VAppBarNavIcon');
export const VAppBarNavIcon = genericComponent()({
  name: 'VAppBarNavIcon',
  props: makeVAppBarNavIconProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    useRender(() => _createVNode(VBtn, _mergeProps(props, {
      "class": ['v-app-bar-nav-icon']
    }), slots));
    return {};
  }
});
//# sourceMappingURL=VAppBarNavIcon.mjs.map