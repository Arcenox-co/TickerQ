import { createVNode as _createVNode } from "vue";
// Styles
import "./VCalendarIntervalEvent.css";

// Components
import { VSheet } from "../../components/VSheet/index.mjs"; // Composables
import { useDate } from "../../composables/date/index.mjs"; // Utilities
import { convertToUnit, genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVCalendarIntervalEventProps = propsFactory({
  allDay: Boolean,
  interval: Object,
  intervalDivisions: {
    type: Number,
    required: true
  },
  intervalDuration: {
    type: Number,
    required: true
  },
  intervalHeight: {
    type: Number,
    required: true
  },
  event: Object
}, 'VCalendarIntervalEvent');
export const VCalendarIntervalEvent = genericComponent()({
  name: 'VCalendarIntervalEvent',
  props: makeVCalendarIntervalEventProps(),
  setup(props) {
    const adapter = useDate();
    const calcHeight = () => {
      if (!props.event?.first && !props.event?.last || adapter.isEqual(props.event?.start, props.interval?.start)) {
        return {
          height: '100%',
          margin: convertToUnit(0)
        };
      } else {
        const {
          height,
          margin
        } = Array.from({
          length: props.intervalDivisions
        }, x => x * (props.intervalDuration / props.intervalDivisions)).reduce((total, div, index) => {
          if (adapter.isBefore(adapter.addMinutes(props.interval?.start, div), props.event?.start)) {
            return {
              height: convertToUnit(props.intervalHeight / props.intervalDivisions * index),
              margin: convertToUnit(props.intervalHeight / props.intervalDivisions * index)
            };
          }
          return {
            height: total.height,
            margin: total.margin
          };
        }, {
          height: '',
          margin: ''
        });
        return {
          height,
          margin
        };
      }
    };
    useRender(() => {
      return _createVNode(VSheet, {
        "height": calcHeight().height,
        "density": "comfortable",
        "style": `margin-top: ${calcHeight().margin}`,
        "class": "v-calendar-internal-event",
        "color": props.event?.color ?? undefined,
        "rounded": props.event?.first && props.event?.last ? true : props.event?.first ? 't' : props.event?.last ? 'b' : false
      }, {
        default: () => [props.event?.first ? props.event?.title : '']
      });
    });
    return {};
  }
});
//# sourceMappingURL=VCalendarIntervalEvent.mjs.map