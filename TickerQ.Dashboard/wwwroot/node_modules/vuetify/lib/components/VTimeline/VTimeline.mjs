import { createVNode as _createVNode, resolveDirective as _resolveDirective } from "vue";
// Styles
import "./VTimeline.css";

// Composables
import { makeComponentProps } from "../../composables/component.mjs";
import { provideDefaults } from "../../composables/defaults.mjs";
import { makeDensityProps, useDensity } from "../../composables/density.mjs";
import { useRtl } from "../../composables/locale.mjs";
import { makeTagProps } from "../../composables/tag.mjs";
import { makeThemeProps, provideTheme } from "../../composables/theme.mjs"; // Utilities
import { computed, toRef } from 'vue';
import { convertToUnit, genericComponent, only, propsFactory, useRender } from "../../util/index.mjs"; // Types
import { makeVTimelineItemProps } from "./VTimelineItem.mjs";
export const makeVTimelineProps = propsFactory({
  align: {
    type: String,
    default: 'center',
    validator: v => ['center', 'start'].includes(v)
  },
  direction: {
    type: String,
    default: 'vertical',
    validator: v => ['vertical', 'horizontal'].includes(v)
  },
  justify: {
    type: String,
    default: 'auto',
    validator: v => ['auto', 'center'].includes(v)
  },
  side: {
    type: String,
    validator: v => v == null || ['start', 'end'].includes(v)
  },
  lineThickness: {
    type: [String, Number],
    default: 2
  },
  lineColor: String,
  truncateLine: {
    type: String,
    validator: v => ['start', 'end', 'both'].includes(v)
  },
  ...only(makeVTimelineItemProps({
    lineInset: 0
  }), ['dotColor', 'fillDot', 'hideOpposite', 'iconColor', 'lineInset', 'size']),
  ...makeComponentProps(),
  ...makeDensityProps(),
  ...makeTagProps(),
  ...makeThemeProps()
}, 'VTimeline');
export const VTimeline = genericComponent()({
  name: 'VTimeline',
  props: makeVTimelineProps(),
  setup(props, _ref) {
    let {
      slots
    } = _ref;
    const {
      themeClasses
    } = provideTheme(props);
    const {
      densityClasses
    } = useDensity(props);
    const {
      rtlClasses
    } = useRtl();
    provideDefaults({
      VTimelineDivider: {
        lineColor: toRef(props, 'lineColor')
      },
      VTimelineItem: {
        density: toRef(props, 'density'),
        dotColor: toRef(props, 'dotColor'),
        fillDot: toRef(props, 'fillDot'),
        hideOpposite: toRef(props, 'hideOpposite'),
        iconColor: toRef(props, 'iconColor'),
        lineColor: toRef(props, 'lineColor'),
        lineInset: toRef(props, 'lineInset'),
        size: toRef(props, 'size')
      }
    });
    const sideClasses = computed(() => {
      const side = props.side ? props.side : props.density !== 'default' ? 'end' : null;
      return side && `v-timeline--side-${side}`;
    });
    const truncateClasses = computed(() => {
      const classes = ['v-timeline--truncate-line-start', 'v-timeline--truncate-line-end'];
      switch (props.truncateLine) {
        case 'both':
          return classes;
        case 'start':
          return classes[0];
        case 'end':
          return classes[1];
        default:
          return null;
      }
    });
    useRender(() => _createVNode(props.tag, {
      "class": ['v-timeline', `v-timeline--${props.direction}`, `v-timeline--align-${props.align}`, `v-timeline--justify-${props.justify}`, truncateClasses.value, {
        'v-timeline--inset-line': !!props.lineInset
      }, themeClasses.value, densityClasses.value, sideClasses.value, rtlClasses.value, props.class],
      "style": [{
        '--v-timeline-line-thickness': convertToUnit(props.lineThickness)
      }, props.style]
    }, slots));
    return {};
  }
});
//# sourceMappingURL=VTimeline.mjs.map