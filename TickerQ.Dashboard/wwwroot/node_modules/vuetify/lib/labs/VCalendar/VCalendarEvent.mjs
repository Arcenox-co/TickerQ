import { createVNode as _createVNode } from "vue";
// Components
import { VBadge } from "../../components/VBadge/index.mjs";
import { VChip } from "../../components/VChip/index.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVCalendarEventProps = propsFactory({
  allDay: Boolean,
  day: Object,
  event: Object
}, 'VCalendarEvent');
export const VCalendarEvent = genericComponent()({
  name: 'VCalendarEvent',
  props: makeVCalendarEventProps(),
  setup(props) {
    useRender(() => _createVNode(VChip, {
      "color": props.allDay ? 'primary' : undefined,
      "density": "comfortable",
      "label": props.allDay,
      "width": "100%"
    }, {
      default: () => [_createVNode(VBadge, {
        "inline": true,
        "dot": true,
        "color": props.event?.color
      }, null), props.event?.title]
    }));
    return {};
  }
});
//# sourceMappingURL=VCalendarEvent.mjs.map