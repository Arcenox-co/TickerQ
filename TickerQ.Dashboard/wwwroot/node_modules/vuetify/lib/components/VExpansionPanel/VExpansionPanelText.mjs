import { withDirectives as _withDirectives, vShow as _vShow, createVNode as _createVNode } from "vue";
// Components
import { VExpansionPanelSymbol } from "./shared.mjs";
import { VExpandTransition } from "../transitions/index.mjs"; // Composables
import { makeComponentProps } from "../../composables/component.mjs";
import { makeLazyProps, useLazy } from "../../composables/lazy.mjs"; // Utilities
import { inject } from 'vue';
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVExpansionPanelTextProps = propsFactory({
  ...makeComponentProps(),
  ...makeLazyProps()
}, 'VExpansionPanelText');
export const VExpansionPanelText = genericComponent()({
  name: 'VExpansionPanelText',
  props: makeVExpansionPanelTextProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const expansionPanel = inject(VExpansionPanelSymbol);
    if (!expansionPanel) throw new Error('[Vuetify] v-expansion-panel-text needs to be placed inside v-expansion-panel');
    const {
      hasContent,
      onAfterLeave
    } = useLazy(props, expansionPanel.isSelected);
    useRender(() => _createVNode(VExpandTransition, {
      "onAfterLeave": onAfterLeave
    }, {
      default: () => [_withDirectives(_createVNode("div", {
        "class": ['v-expansion-panel-text', props.class],
        "style": props.style
      }, [slots.default && hasContent.value && _createVNode("div", {
        "class": "v-expansion-panel-text__wrapper"
      }, [slots.default?.()])]), [[_vShow, expansionPanel.isSelected.value]])]
    }));
    return {};
  }
});
//# sourceMappingURL=VExpansionPanelText.mjs.map