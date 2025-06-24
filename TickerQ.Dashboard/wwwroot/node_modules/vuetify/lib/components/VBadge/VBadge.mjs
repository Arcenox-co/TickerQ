import { withDirectives as _withDirectives, mergeProps as _mergeProps, vShow as _vShow, createVNode as _createVNode } from "vue";
// Styles
import "./VBadge.css";

// Components
import { VIcon } from "../VIcon/index.mjs"; // Composables
import { useBackgroundColor, useTextColor } from "../../composables/color.mjs";
import { makeComponentProps } from "../../composables/component.mjs";
import { IconValue } from "../../composables/icons.mjs";
import { useLocale } from "../../composables/locale.mjs";
import { makeLocationProps, useLocation } from "../../composables/location.mjs";
import { makeRoundedProps, useRounded } from "../../composables/rounded.mjs";
import { makeTagProps } from "../../composables/tag.mjs";
import { makeThemeProps, useTheme } from "../../composables/theme.mjs";
import { makeTransitionProps, MaybeTransition } from "../../composables/transition.mjs"; // Utilities
import { toRef } from 'vue';
import { genericComponent, pickWithRest, propsFactory, useRender } from "../../util/index.mjs";
export const makeVBadgeProps = propsFactory({
  bordered: Boolean,
  color: String,
  content: [Number, String],
  dot: Boolean,
  floating: Boolean,
  icon: IconValue,
  inline: Boolean,
  label: {
    type: String,
    default: '$vuetify.badge'
  },
  max: [Number, String],
  modelValue: {
    type: Boolean,
    default: true
  },
  offsetX: [Number, String],
  offsetY: [Number, String],
  textColor: String,
  ...makeComponentProps(),
  ...makeLocationProps({
    location: 'top end'
  }),
  ...makeRoundedProps(),
  ...makeTagProps(),
  ...makeThemeProps(),
  ...makeTransitionProps({
    transition: 'scale-rotate-transition'
  })
}, 'VBadge');
export const VBadge = genericComponent()({
  name: 'VBadge',
  inheritAttrs: false,
  props: makeVBadgeProps(),
  setup(props, ctx) {
    const {
      backgroundColorClasses,
      backgroundColorStyles
    } = useBackgroundColor(toRef(props, 'color'));
    const {
      roundedClasses
    } = useRounded(props);
    const {
      t
    } = useLocale();
    const {
      textColorClasses,
      textColorStyles
    } = useTextColor(toRef(props, 'textColor'));
    const {
      themeClasses
    } = useTheme();
    const {
      locationStyles
    } = useLocation(props, true, side => {
      const base = props.floating ? props.dot ? 2 : 4 : props.dot ? 8 : 12;
      return base + (['top', 'bottom'].includes(side) ? +(props.offsetY ?? 0) : ['left', 'right'].includes(side) ? +(props.offsetX ?? 0) : 0);
    });
    useRender(() => {
      const value = Number(props.content);
      const content = !props.max || isNaN(value) ? props.content : value <= +props.max ? value : `${props.max}+`;
      const [badgeAttrs, attrs] = pickWithRest(ctx.attrs, ['aria-atomic', 'aria-label', 'aria-live', 'role', 'title']);
      return _createVNode(props.tag, _mergeProps({
        "class": ['v-badge', {
          'v-badge--bordered': props.bordered,
          'v-badge--dot': props.dot,
          'v-badge--floating': props.floating,
          'v-badge--inline': props.inline
        }, props.class]
      }, attrs, {
        "style": props.style
      }), {
        default: () => [_createVNode("div", {
          "class": "v-badge__wrapper"
        }, [ctx.slots.default?.(), _createVNode(MaybeTransition, {
          "transition": props.transition
        }, {
          default: () => [_withDirectives(_createVNode("span", _mergeProps({
            "class": ['v-badge__badge', themeClasses.value, backgroundColorClasses.value, roundedClasses.value, textColorClasses.value],
            "style": [backgroundColorStyles.value, textColorStyles.value, props.inline ? {} : locationStyles.value],
            "aria-atomic": "true",
            "aria-label": t(props.label, value),
            "aria-live": "polite",
            "role": "status"
          }, badgeAttrs), [props.dot ? undefined : ctx.slots.badge ? ctx.slots.badge?.() : props.icon ? _createVNode(VIcon, {
            "icon": props.icon
          }, null) : content]), [[_vShow, props.modelValue]])]
        })])]
      });
    });
    return {};
  }
});
//# sourceMappingURL=VBadge.mjs.map