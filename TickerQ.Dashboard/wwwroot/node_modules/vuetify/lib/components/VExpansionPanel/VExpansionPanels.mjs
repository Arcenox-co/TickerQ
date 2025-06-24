import { createVNode as _createVNode } from "vue";
// Styles
import "./VExpansionPanel.css";

// Components
import { VExpansionPanelSymbol } from "./shared.mjs";
import { makeVExpansionPanelProps } from "./VExpansionPanel.mjs"; // Composables
import { makeComponentProps } from "../../composables/component.mjs";
import { provideDefaults } from "../../composables/defaults.mjs";
import { makeGroupProps, useGroup } from "../../composables/group.mjs";
import { makeTagProps } from "../../composables/tag.mjs";
import { makeThemeProps, provideTheme } from "../../composables/theme.mjs"; // Utilities
import { computed, toRef } from 'vue';
import { genericComponent, pick, propsFactory, useRender } from "../../util/index.mjs"; // Types
const allowedVariants = ['default', 'accordion', 'inset', 'popout'];
export const makeVExpansionPanelsProps = propsFactory({
  flat: Boolean,
  ...makeGroupProps(),
  ...pick(makeVExpansionPanelProps(), ['bgColor', 'collapseIcon', 'color', 'eager', 'elevation', 'expandIcon', 'focusable', 'hideActions', 'readonly', 'ripple', 'rounded', 'tile', 'static']),
  ...makeThemeProps(),
  ...makeComponentProps(),
  ...makeTagProps(),
  variant: {
    type: String,
    default: 'default',
    validator: v => allowedVariants.includes(v)
  }
}, 'VExpansionPanels');
export const VExpansionPanels = genericComponent()({
  name: 'VExpansionPanels',
  props: makeVExpansionPanelsProps(),
  emits: {
    'update:modelValue': val => true
  },
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const {
      next,
      prev
    } = useGroup(props, VExpansionPanelSymbol);
    const {
      themeClasses
    } = provideTheme(props);
    const variantClass = computed(() => props.variant && `v-expansion-panels--variant-${props.variant}`);
    provideDefaults({
      VExpansionPanel: {
        bgColor: toRef(props, 'bgColor'),
        collapseIcon: toRef(props, 'collapseIcon'),
        color: toRef(props, 'color'),
        eager: toRef(props, 'eager'),
        elevation: toRef(props, 'elevation'),
        expandIcon: toRef(props, 'expandIcon'),
        focusable: toRef(props, 'focusable'),
        hideActions: toRef(props, 'hideActions'),
        readonly: toRef(props, 'readonly'),
        ripple: toRef(props, 'ripple'),
        rounded: toRef(props, 'rounded'),
        static: toRef(props, 'static')
      }
    });
    useRender(() => _createVNode(props.tag, {
      "class": ['v-expansion-panels', {
        'v-expansion-panels--flat': props.flat,
        'v-expansion-panels--tile': props.tile
      }, themeClasses.value, variantClass.value, props.class],
      "style": props.style
    }, {
      default: () => [slots.default?.({
        prev,
        next
      })]
    }));
    return {
      next,
      prev
    };
  }
});
//# sourceMappingURL=VExpansionPanels.mjs.map