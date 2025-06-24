import { mergeProps as _mergeProps, createVNode as _createVNode, Fragment as _Fragment } from "vue";
// Components
import { VDefaultsProvider } from "../../components/VDefaultsProvider/index.mjs";
import { makeVListGroupProps, VListGroup } from "../../components/VList/VListGroup.mjs"; // Utilities
import { computed, ref } from 'vue';
import { genericComponent, omit, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVTreeviewGroupProps = propsFactory({
  ...omit(makeVListGroupProps({
    collapseIcon: '$treeviewCollapse',
    expandIcon: '$treeviewExpand'
  }), ['subgroup'])
}, 'VTreeviewGroup');
export const VTreeviewGroup = genericComponent()({
  name: 'VTreeviewGroup',
  props: makeVTreeviewGroupProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const vListGroupRef = ref();
    const toggleIcon = computed(() => vListGroupRef.value?.isOpen ? props.collapseIcon : props.expandIcon);
    const activatorDefaults = computed(() => ({
      VTreeviewItem: {
        prependIcon: undefined,
        appendIcon: undefined,
        active: vListGroupRef.value?.isOpen,
        toggleIcon: toggleIcon.value
      }
    }));
    useRender(() => {
      const listGroupProps = VListGroup.filterProps(props);
      return _createVNode(VListGroup, _mergeProps(listGroupProps, {
        "ref": vListGroupRef,
        "class": ['v-treeview-group', props.class],
        "subgroup": true
      }), {
        ...slots,
        activator: slots.activator ? slotProps => _createVNode(_Fragment, null, [_createVNode(VDefaultsProvider, {
          "defaults": activatorDefaults.value
        }, {
          default: () => [slots.activator?.(slotProps)]
        })]) : undefined
      });
    });
    return {};
  }
});
//# sourceMappingURL=VTreeviewGroup.mjs.map