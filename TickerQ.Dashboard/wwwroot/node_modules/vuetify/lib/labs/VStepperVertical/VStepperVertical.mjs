import { mergeProps as _mergeProps, createVNode as _createVNode, Fragment as _Fragment } from "vue";
// Components
import { VStepperVerticalItem } from "./VStepperVerticalItem.mjs";
import { makeVExpansionPanelsProps, VExpansionPanels } from "../../components/VExpansionPanel/VExpansionPanels.mjs";
import { makeStepperProps } from "../../components/VStepper/VStepper.mjs"; // Composables
import { provideDefaults } from "../../composables/defaults.mjs";
import { useProxiedModel } from "../../composables/proxiedModel.mjs"; // Utilities
import { computed, ref, toRefs } from 'vue';
import { genericComponent, getPropertyFromItem, omit, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVStepperVerticalProps = propsFactory({
  prevText: {
    type: String,
    default: '$vuetify.stepper.prev'
  },
  nextText: {
    type: String,
    default: '$vuetify.stepper.next'
  },
  ...makeStepperProps(),
  ...omit(makeVExpansionPanelsProps({
    mandatory: 'force',
    variant: 'accordion'
  }), ['static'])
}, 'VStepperVertical');
export const VStepperVertical = genericComponent()({
  name: 'VStepperVertical',
  props: makeVStepperVerticalProps(),
  emits: {
    'update:modelValue': val => true
  },
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const vExpansionPanelsRef = ref();
    const {
      color,
      eager,
      editable,
      prevText,
      nextText,
      hideActions
    } = toRefs(props);
    const model = useProxiedModel(props, 'modelValue');
    const items = computed(() => props.items.map((item, index) => {
      const title = getPropertyFromItem(item, props.itemTitle, item);
      const value = getPropertyFromItem(item, props.itemValue, index + 1);
      return {
        title,
        value,
        raw: item
      };
    }));
    provideDefaults({
      VStepperVerticalItem: {
        color,
        eager,
        editable,
        prevText,
        nextText,
        hideActions,
        static: true
      },
      VStepperActions: {
        color
      }
    });
    useRender(() => {
      const expansionPanelProps = VExpansionPanels.filterProps(props);
      return _createVNode(VExpansionPanels, _mergeProps(expansionPanelProps, {
        "modelValue": model.value,
        "onUpdate:modelValue": $event => model.value = $event,
        "ref": vExpansionPanelsRef,
        "class": ['v-stepper', {
          'v-stepper--alt-labels': props.altLabels,
          'v-stepper--flat': props.flat,
          'v-stepper--non-linear': props.nonLinear,
          'v-stepper--mobile': props.mobile
        }, props.class],
        "style": props.style
      }), {
        ...slots,
        default: _ref2 => {
          let {
            prev,
            next
          } = _ref2;
          return _createVNode(_Fragment, null, [items.value.map(_ref3 => {
            let {
              raw,
              ...item
            } = _ref3;
            return _createVNode(VStepperVerticalItem, item, {
              ...slots,
              default: slots[`item.${item.value}`]
            });
          }), slots.default?.({
            prev,
            next,
            step: model.value
          })]);
        }
      });
    });
    return {};
  }
});
//# sourceMappingURL=VStepperVertical.mjs.map