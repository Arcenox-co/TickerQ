import { createVNode as _createVNode } from "vue";
// Styles
import "./VCalendarInterval.css";

// Components
import { VCalendarIntervalEvent } from "./VCalendarIntervalEvent.mjs"; // Composables
import { useDate } from "../../composables/date/index.mjs"; // Utilities
import { computed } from 'vue';
import { convertToUnit, genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVCalendarIntervalProps = propsFactory({
  day: {
    type: Object,
    default: () => ({})
  },
  dayIndex: Number,
  events: Array,
  intervalDivisions: {
    type: Number,
    default: 2
  },
  intervalDuration: {
    type: Number,
    default: 60
  },
  intervalHeight: {
    type: Number,
    default: 48
  },
  intervalFormat: {
    type: [String, Function],
    default: 'fullTime12h'
  },
  intervalStart: {
    type: Number,
    default: 0
  }
}, 'VCalendarInterval');
export const VCalendarInterval = genericComponent()({
  name: 'VCalendarInterval',
  props: {
    index: {
      type: Number,
      required: true
    },
    ...makeVCalendarIntervalProps()
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    const adapter = useDate();
    const interval = computed(() => {
      const start = adapter.addMinutes(adapter.startOfDay(props.day.date), props.intervalDuration * (props.index + props.intervalStart));
      const end = adapter.addMinutes(adapter.startOfDay(props.day.date), props.intervalDuration * (props.index + props.intervalStart + 1) - 1);
      return {
        ...props.day,
        label: adapter.format(start, 'fullTime24h'),
        start,
        end,
        events: props.events ? props.events.filter(e => !e.allDay && (adapter.isEqual(start, e.start) || adapter.isWithinRange(e.start, [start, end]) || adapter.isWithinRange(start, [e.start, e.end]) || adapter.isEqual(end, e.end))).map(e => {
          return {
            ...e,
            first: adapter.isEqual(start, e.start) || adapter.isWithinRange(e.start, [start, end]),
            last: adapter.isEqual(end, e.end) || adapter.isWithinRange(e.end, [start, end])
          };
        }) : []
      };
    });
    useRender(() => {
      return props.dayIndex === 0 ? _createVNode("div", {
        "class": "v-calendar-day__row-with-label",
        "style": `height: ${convertToUnit(props.intervalHeight)}`
      }, [_createVNode("div", {
        "class": "v-calendar-day__row-label"
      }, [_createVNode("slot", {
        "name": "intervalFormat",
        "interval": interval.value
      }, [props.index ? props.intervalFormat ? typeof props.intervalFormat === 'string' ? adapter.format(interval.value.start, 'hours12h') : props.intervalFormat(interval.value) : interval.value.label : ''])]), _createVNode("div", {
        "class": "v-calendar-day__row-hairline"
      }, null), _createVNode("div", {
        "class": ['v-calendar-day__row-content', interval.value.events.some(e => !e.last) ? 'v-calendar-day__row-content-through' : '']
      }, [_createVNode("slot", {
        "name": "intervalBody",
        "interval": interval.value
      }, [interval.value.events?.map(event => _createVNode(VCalendarIntervalEvent, {
        "event": event,
        "interval": interval.value,
        "intervalDivisions": props.intervalDivisions,
        "intervalDuration": props.intervalDuration,
        "intervalHeight": props.intervalHeight
      }, null))])])]) : _createVNode("div", {
        "class": "v-calendar-day__row-without-label",
        "style": `height: ${convertToUnit(props.intervalHeight)}`
      }, [_createVNode("div", {
        "class": ['v-calendar-day__row-content', interval.value.events.some(e => !e.last) ? 'v-calendar-day__row-content-through' : '']
      }, [_createVNode("slot", {
        "name": "intervalBody",
        "interval": interval.value
      }, [interval.value.events?.filter(event => !event.allDay).map(event => _createVNode(VCalendarIntervalEvent, {
        "event": event,
        "interval": interval.value,
        "intervalDivisions": props.intervalDivisions,
        "intervalDuration": props.intervalDuration,
        "intervalHeight": props.intervalHeight
      }, null))])])]);
    });
    return {
      interval
    };
  }
});
//# sourceMappingURL=VCalendarInterval.mjs.map