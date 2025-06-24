import { mergeProps as _mergeProps, createVNode as _createVNode } from "vue";
// Styles
import "./VCalendarDay.css";

// Components
import { makeVCalendarIntervalProps, VCalendarInterval } from "./VCalendarInterval.mjs";
import { VBtn } from "../../components/VBtn/index.mjs"; // Composables
import { useDate } from "../../composables/date/index.mjs"; // Utilities
import { computed } from 'vue';
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVCalendarDayProps = propsFactory({
  hideDayHeader: Boolean,
  intervals: {
    type: Number,
    default: 24
  },
  ...makeVCalendarIntervalProps()
}, 'VCalendarDay');
export const VCalendarDay = genericComponent()({
  name: 'VCalendarDay',
  props: makeVCalendarDayProps(),
  setup(props) {
    const adapter = useDate();
    const intervals = computed(() => [...Array.from({
      length: props.intervals
    }, (v, i) => i).filter((int, index) => props.intervalDuration * (index + props.intervalStart) < 1440)]);
    useRender(() => {
      const calendarIntervalProps = VCalendarInterval.filterProps(props);
      return _createVNode("div", {
        "class": "v-calendar-day__container"
      }, [!props.hideDayHeader && _createVNode("div", {
        "key": "calender-week-name",
        "class": "v-calendar-weekly__head-weekday"
      }, [adapter.format(props.day.date, 'weekdayShort'), _createVNode("div", null, [_createVNode(VBtn, {
        "icon": true,
        "text": adapter.format(props.day.date, 'dayOfMonth'),
        "variant": "text"
      }, null)])]), intervals.value.map((_, index) => _createVNode(VCalendarInterval, _mergeProps({
        "index": index
      }, calendarIntervalProps), null))]);
    });
    return {
      intervals
    };
  }
});
//# sourceMappingURL=VCalendarDay.mjs.map