import { createVNode as _createVNode } from "vue";
// Styles
import "./VCalendarHeader.css";

// Components
import { VBtn } from "../../components/VBtn/index.mjs"; // Composables
import { useLocale } from "../../composables/locale.mjs"; // Utilities
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVCalendarHeaderProps = propsFactory({
  nextIcon: {
    type: String,
    default: '$next'
  },
  prevIcon: {
    type: String,
    default: '$prev'
  },
  title: String,
  text: {
    type: String,
    default: '$vuetify.calendar.today'
  },
  viewMode: {
    type: String,
    default: 'month'
  }
}, 'VCalendarHeader');
export const VCalendarHeader = genericComponent()({
  name: 'VCalendarHeader',
  props: makeVCalendarHeaderProps(),
  emits: {
    'click:next': () => true,
    'click:prev': () => true,
    'click:toToday': () => true
  },
  setup(props, _ref) {
    let {
      emit
    } = _ref;
    const {
      t
    } = useLocale();
    function prev() {
      emit('click:prev');
    }
    function next() {
      emit('click:next');
    }
    function toToday() {
      emit('click:toToday');
    }
    useRender(() => _createVNode("div", {
      "class": "v-calendar-header"
    }, [props.text && _createVNode(VBtn, {
      "key": "today",
      "class": "v-calendar-header__today",
      "text": t(props.text),
      "variant": "outlined",
      "onClick": toToday
    }, null), _createVNode(VBtn, {
      "density": "comfortable",
      "icon": props.prevIcon,
      "variant": "text",
      "onClick": prev
    }, null), _createVNode(VBtn, {
      "density": "comfortable",
      "icon": props.nextIcon,
      "variant": "text",
      "onClick": next
    }, null), _createVNode("div", {
      "class": "v-calendar-header__title"
    }, [props.title])]));
    return {};
  }
});
//# sourceMappingURL=VCalendarHeader.mjs.map