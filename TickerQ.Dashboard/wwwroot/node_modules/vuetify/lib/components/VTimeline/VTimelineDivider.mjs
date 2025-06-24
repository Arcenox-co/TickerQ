import { resolveDirective as _resolveDirective, createVNode as _createVNode } from "vue";
// Components
import { VDefaultsProvider } from "../VDefaultsProvider/index.mjs";
import { VIcon } from "../VIcon/index.mjs"; // Composables
import { useBackgroundColor } from "../../composables/color.mjs";
import { makeComponentProps } from "../../composables/component.mjs";
import { makeElevationProps, useElevation } from "../../composables/elevation.mjs";
import { IconValue } from "../../composables/icons.mjs";
import { makeRoundedProps, useRounded } from "../../composables/rounded.mjs";
import { makeSizeProps, useSize } from "../../composables/size.mjs"; // Utilities
import { toRef } from 'vue';
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVTimelineDividerProps = propsFactory({
  dotColor: String,
  fillDot: Boolean,
  hideDot: Boolean,
  icon: IconValue,
  iconColor: String,
  lineColor: String,
  ...makeComponentProps(),
  ...makeRoundedProps(),
  ...makeSizeProps(),
  ...makeElevationProps()
}, 'VTimelineDivider');
export const VTimelineDivider = genericComponent()({
  name: 'VTimelineDivider',
  props: makeVTimelineDividerProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const {
      sizeClasses,
      sizeStyles
    } = useSize(props, 'v-timeline-divider__dot');
    const {
      backgroundColorStyles,
      backgroundColorClasses
    } = useBackgroundColor(toRef(props, 'dotColor'));
    const {
      roundedClasses
    } = useRounded(props, 'v-timeline-divider__dot');
    const {
      elevationClasses
    } = useElevation(props);
    const {
      backgroundColorClasses: lineColorClasses,
      backgroundColorStyles: lineColorStyles
    } = useBackgroundColor(toRef(props, 'lineColor'));
    useRender(() => _createVNode("div", {
      "class": ['v-timeline-divider', {
        'v-timeline-divider--fill-dot': props.fillDot
      }, props.class],
      "style": props.style
    }, [_createVNode("div", {
      "class": ['v-timeline-divider__before', lineColorClasses.value],
      "style": lineColorStyles.value
    }, null), !props.hideDot && _createVNode("div", {
      "key": "dot",
      "class": ['v-timeline-divider__dot', elevationClasses.value, roundedClasses.value, sizeClasses.value],
      "style": sizeStyles.value
    }, [_createVNode("div", {
      "class": ['v-timeline-divider__inner-dot', backgroundColorClasses.value, roundedClasses.value],
      "style": backgroundColorStyles.value
    }, [!slots.default ? _createVNode(VIcon, {
      "key": "icon",
      "color": props.iconColor,
      "icon": props.icon,
      "size": props.size
    }, null) : _createVNode(VDefaultsProvider, {
      "key": "icon-defaults",
      "disabled": !props.icon,
      "defaults": {
        VIcon: {
          color: props.iconColor,
          icon: props.icon,
          size: props.size
        }
      }
    }, slots.default)])]), _createVNode("div", {
      "class": ['v-timeline-divider__after', lineColorClasses.value],
      "style": lineColorStyles.value
    }, null)]));
    return {};
  }
});
//# sourceMappingURL=VTimelineDivider.mjs.map