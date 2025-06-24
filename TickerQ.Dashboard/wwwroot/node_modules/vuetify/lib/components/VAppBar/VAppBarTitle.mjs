import { createVNode as _createVNode, mergeProps as _mergeProps, resolveDirective as _resolveDirective } from "vue";
// Components
import { makeVToolbarTitleProps, VToolbarTitle } from "../VToolbar/VToolbarTitle.mjs"; // Utilities
import { genericComponent, useRender } from "../../util/index.mjs"; // Types
export const VAppBarTitle = genericComponent()({
  name: 'VAppBarTitle',
  props: makeVToolbarTitleProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    useRender(() => _createVNode(VToolbarTitle, _mergeProps(props, {
      "class": "v-app-bar-title"
    }), slots));
    return {};
  }
});
//# sourceMappingURL=VAppBarTitle.mjs.map