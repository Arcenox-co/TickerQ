import { mergeProps as _mergeProps, resolveDirective as _resolveDirective, createVNode as _createVNode, Fragment as _Fragment } from "vue";
// Styles
import "./VStepperVerticalItem.css";

// Components
import { VStepperVerticalActions } from "./VStepperVerticalActions.mjs";
import { VAvatar } from "../../components/VAvatar/VAvatar.mjs";
import { VDefaultsProvider } from "../../components/VDefaultsProvider/VDefaultsProvider.mjs";
import { makeVExpansionPanelProps, VExpansionPanel } from "../../components/VExpansionPanel/VExpansionPanel.mjs";
import { VIcon } from "../../components/VIcon/VIcon.mjs";
import { makeStepperItemProps } from "../../components/VStepper/VStepperItem.mjs"; // Utilities
import { computed, ref } from 'vue';
import { genericComponent, omit, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVStepperVerticalItemProps = propsFactory({
  hideActions: Boolean,
  ...makeStepperItemProps(),
  ...omit(makeVExpansionPanelProps({
    expandIcon: '',
    collapseIcon: ''
  }), ['hideActions'])
}, 'VStepperVerticalItem');
export const VStepperVerticalItem = genericComponent()({
  name: 'VStepperVerticalItem',
  props: makeVStepperVerticalItemProps(),
  emits: {
    'click:next': () => true,
    'click:prev': () => true,
    'click:finish': () => true
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    const vExpansionPanelRef = ref();
    const step = computed(() => !isNaN(parseInt(props.value)) ? Number(props.value) : props.value);
    const groupItem = computed(() => vExpansionPanelRef.value?.groupItem);
    const isSelected = computed(() => groupItem.value?.isSelected.value ?? false);
    const isValid = computed(() => isSelected.value ? props.rules.every(handler => handler() === true) : null);
    const canEdit = computed(() => !props.disabled && props.editable);
    const hasError = computed(() => props.error || isSelected.value && !isValid.value);
    const hasCompleted = computed(() => props.complete || props.rules.length > 0 && isValid.value === true);
    const disabled = computed(() => {
      if (props.disabled) return props.disabled;
      if (groupItem.value?.isFirst.value) return 'prev';
      return false;
    });
    const icon = computed(() => {
      if (hasError.value) return props.errorIcon;
      if (hasCompleted.value) return props.completeIcon;
      if (groupItem.value?.isSelected.value && props.editable) return props.editIcon;
      return props.icon;
    });
    const slotProps = computed(() => ({
      canEdit: canEdit.value,
      hasError: hasError.value,
      hasCompleted: hasCompleted.value,
      title: props.title,
      subtitle: props.subtitle,
      step: step.value,
      value: props.value
    }));
    const actionProps = computed(() => ({
      ...slotProps.value,
      prev: onClickPrev,
      next: onClickNext
    }));
    function onClickNext() {
      emit('click:next');
      if (groupItem.value?.isLast.value) return;
      groupItem.value.group.next();
    }
    function onClickPrev() {
      emit('click:prev');
      groupItem.value.group.prev();
    }
    useRender(() => {
      const hasColor = (hasCompleted.value || groupItem.value?.isSelected.value) && !hasError.value && !props.disabled;
      const hasActions = !props.hideActions || !!slots.actions;
      const expansionPanelProps = VExpansionPanel.filterProps(props);
      return _createVNode(VExpansionPanel, _mergeProps({
        "_as": "VStepperVerticalItem",
        "ref": vExpansionPanelRef
      }, expansionPanelProps, {
        "class": ['v-stepper-vertical-item', {
          'v-stepper-vertical-item--complete': hasCompleted.value,
          'v-stepper-vertical-item--disabled': props.disabled,
          'v-stepper-vertical-item--editable': canEdit.value,
          'v-stepper-vertical-item--error': hasError.value
        }, props.class],
        "readonly": !props.editable,
        "style": props.style,
        "color": "",
        "hide-actions": false,
        "value": step.value
      }), {
        title: () => _createVNode(_Fragment, null, [_createVNode(VAvatar, {
          "key": "stepper-avatar",
          "class": "v-stepper-vertical-item__avatar",
          "color": hasColor ? props.color : undefined,
          "size": 24,
          "start": true
        }, {
          default: () => [slots.icon?.(slotProps.value) ?? (icon.value ? _createVNode(VIcon, {
            "icon": icon.value
          }, null) : step.value)]
        }), _createVNode("div", null, [_createVNode("div", {
          "class": "v-stepper-vertical-item__title"
        }, [slots.title?.(slotProps.value) ?? props.title]), _createVNode("div", {
          "class": "v-stepper-vertical-item__subtitle"
        }, [slots.subtitle?.(slotProps.value) ?? props.subtitle])])]),
        text: () => _createVNode(_Fragment, null, [slots.default?.(slotProps.value) ?? props.text, hasActions && _createVNode(VDefaultsProvider, {
          "defaults": {
            VStepperVerticalActions: {
              disabled: disabled.value,
              finish: groupItem.value?.isLast.value
            }
          }
        }, {
          default: () => [slots.actions?.(actionProps.value) ?? _createVNode(VStepperVerticalActions, {
            "onClick:next": onClickNext,
            "onClick:prev": onClickPrev
          }, {
            prev: slots.prev ? () => slots.prev?.(actionProps.value) : undefined,
            next: slots.next ? () => slots.next?.(actionProps.value) : undefined
          })]
        })])
      });
    });
    return {};
  }
});
//# sourceMappingURL=VStepperVerticalItem.mjs.map