import { createVNode as _createVNode } from "vue";
// Styles
import "./VCalendarMonthDay.css";

// Components
import { VCalendarEvent } from "./VCalendarEvent.mjs";
import { VBtn } from "../../components/VBtn/index.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVCalendarMonthDayProps = propsFactory({
  active: Boolean,
  color: String,
  day: Object,
  disabled: Boolean,
  events: Array,
  title: [Number, String]
}, 'VCalendarMonthDay');
export const VCalendarMonthDay = genericComponent()({
  name: 'VCalendarMonthDay',
  props: makeVCalendarMonthDayProps(),
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    useRender(() => {
      const hasTitle = !!(props.title || slots.title?.({
        title: props.title
      }));
      return _createVNode("div", {
        "class": ['v-calendar-month__day']
      }, [!props.day?.isHidden && hasTitle && _createVNode("div", {
        "key": "title",
        "class": "v-calendar-weekly__day-label"
      }, [slots.title?.({
        title: props.title
      }) ?? _createVNode(VBtn, {
        "class": props.day?.isToday ? 'v-calendar-weekly__day-label__today' : undefined,
        "color": props.color,
        "disabled": props.disabled,
        "icon": true,
        "size": "x-small",
        "variant": props.day?.isToday ? undefined : 'flat'
      }, {
        default: () => [props.title]
      })]), !props.day?.isHidden && _createVNode("div", {
        "key": "content",
        "class": "v-calendar-weekly__day-content"
      }, [slots.content?.() ?? _createVNode("div", null, [_createVNode("div", {
        "class": "v-calendar-weekly__day-alldayevents-container"
      }, [props.events?.filter(event => event.allDay).map(event => slots.event ? slots.event({
        day: props.day,
        allDay: true,
        event
      }) : _createVNode(VCalendarEvent, {
        "day": props.day,
        "event": event,
        "allDay": true
      }, null))]), _createVNode("div", {
        "class": "v-calendar-weekly__day-events-container"
      }, [props.events?.filter(event => !event.allDay).map(event => slots.event ? slots.event({
        day: props.day,
        event,
        allDay: false
      }) : _createVNode(VCalendarEvent, {
        "day": props.day,
        "event": event
      }, null))])])]), !props.day?.isHidden && slots.default?.()]);
    });
    return {};
  }
});
//# sourceMappingURL=VCalendarMonthDay.mjs.map