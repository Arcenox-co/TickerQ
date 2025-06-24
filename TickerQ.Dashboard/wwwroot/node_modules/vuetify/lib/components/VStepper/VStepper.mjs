import { mergeProps as _mergeProps, resolveDirective as _resolveDirective, createVNode as _createVNode, Fragment as _Fragment } from "vue";
// Styles
import "./VStepper.css";

// Components
import { VStepperSymbol } from "./shared.mjs";
import { makeVStepperActionsProps, VStepperActions } from "./VStepperActions.mjs";
import { VStepperHeader } from "./VStepperHeader.mjs";
import { VStepperItem } from "./VStepperItem.mjs";
import { VStepperWindow } from "./VStepperWindow.mjs";
import { VStepperWindowItem } from "./VStepperWindowItem.mjs";
import { VDivider } from "../VDivider/index.mjs";
import { makeVSheetProps, VSheet } from "../VSheet/VSheet.mjs"; // Composables
import { provideDefaults } from "../../composables/defaults.mjs";
import { makeDisplayProps, useDisplay } from "../../composables/display.mjs";
import { makeGroupProps, useGroup } from "../../composables/group.mjs";
import { IconValue } from "../../composables/icons.mjs"; // Utilities
import { computed, toRefs } from 'vue';
import { genericComponent, getPropertyFromItem, only, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeStepperProps = propsFactory({
  altLabels: Boolean,
  bgColor: String,
  completeIcon: IconValue,
  editIcon: IconValue,
  editable: Boolean,
  errorIcon: IconValue,
  hideActions: Boolean,
  items: {
    type: Array,
    default: () => []
  },
  itemTitle: {
    type: String,
    default: 'title'
  },
  itemValue: {
    type: String,
    default: 'value'
  },
  nonLinear: Boolean,
  flat: Boolean,
  ...makeDisplayProps()
}, 'Stepper');
export const makeVStepperProps = propsFactory({
  ...makeStepperProps(),
  ...makeGroupProps({
    mandatory: 'force',
    selectedClass: 'v-stepper-item--selected'
  }),
  ...makeVSheetProps(),
  ...only(makeVStepperActionsProps(), ['prevText', 'nextText'])
}, 'VStepper');
export const VStepper = genericComponent()({
  name: 'VStepper',
  props: makeVStepperProps(),
  emits: {
    'update:modelValue': v => true
  },
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const {
      items: _items,
      next,
      prev,
      selected
    } = useGroup(props, VStepperSymbol);
    const {
      displayClasses,
      mobile
    } = useDisplay(props);
    const {
      completeIcon,
      editIcon,
      errorIcon,
      color,
      editable,
      prevText,
      nextText
    } = toRefs(props);
    const items = computed(() => props.items.map((item, index) => {
      const title = getPropertyFromItem(item, props.itemTitle, item);
      const value = getPropertyFromItem(item, props.itemValue, index + 1);
      return {
        title,
        value,
        raw: item
      };
    }));
    const activeIndex = computed(() => {
      return _items.value.findIndex(item => selected.value.includes(item.id));
    });
    const disabled = computed(() => {
      if (props.disabled) return props.disabled;
      if (activeIndex.value === 0) return 'prev';
      if (activeIndex.value === _items.value.length - 1) return 'next';
      return false;
    });
    provideDefaults({
      VStepperItem: {
        editable,
        errorIcon,
        completeIcon,
        editIcon,
        prevText,
        nextText
      },
      VStepperActions: {
        color,
        disabled,
        prevText,
        nextText
      }
    });
    useRender(() => {
      const sheetProps = VSheet.filterProps(props);
      const hasHeader = !!(slots.header || props.items.length);
      const hasWindow = props.items.length > 0;
      const hasActions = !props.hideActions && !!(hasWindow || slots.actions);
      return _createVNode(VSheet, _mergeProps(sheetProps, {
        "color": props.bgColor,
        "class": ['v-stepper', {
          'v-stepper--alt-labels': props.altLabels,
          'v-stepper--flat': props.flat,
          'v-stepper--non-linear': props.nonLinear,
          'v-stepper--mobile': mobile.value
        }, displayClasses.value, props.class],
        "style": props.style
      }), {
        default: () => [hasHeader && _createVNode(VStepperHeader, {
          "key": "stepper-header"
        }, {
          default: () => [items.value.map((_ref2, index) => {
            let {
              raw,
              ...item
            } = _ref2;
            return _createVNode(_Fragment, null, [!!index && _createVNode(VDivider, null, null), _createVNode(VStepperItem, item, {
              default: slots[`header-item.${item.value}`] ?? slots.header,
              icon: slots.icon,
              title: slots.title,
              subtitle: slots.subtitle
            })]);
          })]
        }), hasWindow && _createVNode(VStepperWindow, {
          "key": "stepper-window"
        }, {
          default: () => [items.value.map(item => _createVNode(VStepperWindowItem, {
            "value": item.value
          }, {
            default: () => slots[`item.${item.value}`]?.(item) ?? slots.item?.(item)
          }))]
        }), slots.default?.({
          prev,
          next
        }), hasActions && (slots.actions?.({
          next,
          prev
        }) ?? _createVNode(VStepperActions, {
          "key": "stepper-actions",
          "onClick:prev": prev,
          "onClick:next": next
        }, slots))]
      });
    });
    return {
      prev,
      next
    };
  }
});
//# sourceMappingURL=VStepper.mjs.map