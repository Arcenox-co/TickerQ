import { createVNode as _createVNode } from "vue";
// Components
import { VBtn } from "../VBtn/VBtn.mjs";
import { VDefaultsProvider } from "../VDefaultsProvider/VDefaultsProvider.mjs"; // Composables
import { useLocale } from "../../composables/locale.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVStepperActionsProps = propsFactory({
  color: String,
  disabled: {
    type: [Boolean, String],
    default: false
  },
  prevText: {
    type: String,
    default: '$vuetify.stepper.prev'
  },
  nextText: {
    type: String,
    default: '$vuetify.stepper.next'
  }
}, 'VStepperActions');
export const VStepperActions = genericComponent()({
  name: 'VStepperActions',
  props: makeVStepperActionsProps(),
  emits: {
    'click:prev': () => true,
    'click:next': () => true
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    const {
      t
    } = useLocale();
    function onClickPrev() {
      emit('click:prev');
    }
    function onClickNext() {
      emit('click:next');
    }
    useRender(() => {
      const prevSlotProps = {
        onClick: onClickPrev
      };
      const nextSlotProps = {
        onClick: onClickNext
      };
      return _createVNode("div", {
        "class": "v-stepper-actions"
      }, [_createVNode(VDefaultsProvider, {
        "defaults": {
          VBtn: {
            disabled: ['prev', true].includes(props.disabled),
            text: t(props.prevText),
            variant: 'text'
          }
        }
      }, {
        default: () => [slots.prev?.({
          props: prevSlotProps
        }) ?? _createVNode(VBtn, prevSlotProps, null)]
      }), _createVNode(VDefaultsProvider, {
        "defaults": {
          VBtn: {
            color: props.color,
            disabled: ['next', true].includes(props.disabled),
            text: t(props.nextText),
            variant: 'tonal'
          }
        }
      }, {
        default: () => [slots.next?.({
          props: nextSlotProps
        }) ?? _createVNode(VBtn, nextSlotProps, null)]
      })]);
    });
    return {};
  }
});
//# sourceMappingURL=VStepperActions.mjs.map