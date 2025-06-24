import { createVNode as _createVNode, mergeProps as _mergeProps, resolveDirective as _resolveDirective } from "vue";
// Components
import { makeVStepperActionsProps, VStepperActions } from "../../components/VStepper/VStepperActions.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVStepperVerticalActionsProps = propsFactory({
  ...makeVStepperActionsProps()
}, 'VStepperActions');
export const VStepperVerticalActions = genericComponent()({
  name: 'VStepperVerticalActions',
  props: makeVStepperVerticalActionsProps(),
  emits: {
    'click:prev': () => true,
    'click:next': () => true
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    function onClickPrev() {
      emit('click:prev');
    }
    function onClickNext() {
      emit('click:next');
    }
    useRender(() => {
      const stepperActionsProps = VStepperActions.filterProps(props);
      return _createVNode(VStepperActions, _mergeProps({
        "class": "v-stepper-vertical-actions"
      }, stepperActionsProps, {
        "onClick:prev": onClickPrev,
        "onClick:next": onClickNext
      }), slots);
    });
    return {};
  }
});
//# sourceMappingURL=VStepperVerticalActions.mjs.map