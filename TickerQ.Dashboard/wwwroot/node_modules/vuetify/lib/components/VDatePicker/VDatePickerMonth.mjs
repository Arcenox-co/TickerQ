import { createVNode as _createVNode, createTextVNode as _createTextVNode } from "vue";
// Styles
import "./VDatePickerMonth.css";

// Components
import { VBtn } from "../VBtn/index.mjs";
import { VDefaultsProvider } from "../VDefaultsProvider/index.mjs"; // Composables
import { makeCalendarProps, useCalendar } from "../../composables/calendar.mjs";
import { useDate } from "../../composables/date/date.mjs";
import { MaybeTransition } from "../../composables/transition.mjs"; // Utilities
import { computed, ref, shallowRef, watch } from 'vue';
import { genericComponent, propsFactory } from "../../util/index.mjs"; // Types
export const makeVDatePickerMonthProps = propsFactory({
  color: String,
  hideWeekdays: Boolean,
  multiple: [Boolean, Number, String],
  showWeek: Boolean,
  transition: {
    type: String,
    default: 'picker-transition'
  },
  reverseTransition: {
    type: String,
    default: 'picker-reverse-transition'
  },
  ...makeCalendarProps()
}, 'VDatePickerMonth');
export const VDatePickerMonth = genericComponent()({
  name: 'VDatePickerMonth',
  props: makeVDatePickerMonthProps(),
  emits: {
    'update:modelValue': date => true,
    'update:month': date => true,
    'update:year': date => true
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    const daysRef = ref();
    const {
      daysInMonth,
      model,
      weekNumbers
    } = useCalendar(props);
    const adapter = useDate();
    const rangeStart = shallowRef();
    const rangeStop = shallowRef();
    const isReverse = shallowRef(false);
    const transition = computed(() => {
      return !isReverse.value ? props.transition : props.reverseTransition;
    });
    if (props.multiple === 'range' && model.value.length > 0) {
      rangeStart.value = model.value[0];
      if (model.value.length > 1) {
        rangeStop.value = model.value[model.value.length - 1];
      }
    }
    const atMax = computed(() => {
      const max = ['number', 'string'].includes(typeof props.multiple) ? Number(props.multiple) : Infinity;
      return model.value.length >= max;
    });
    watch(daysInMonth, (val, oldVal) => {
      if (!oldVal) return;
      isReverse.value = adapter.isBefore(val[0].date, oldVal[0].date);
    });
    function onRangeClick(value) {
      const _value = adapter.startOfDay(value);
      if (model.value.length === 0) {
        rangeStart.value = undefined;
      } else if (model.value.length === 1) {
        rangeStart.value = model.value[0];
        rangeStop.value = undefined;
      }
      if (!rangeStart.value) {
        rangeStart.value = _value;
        model.value = [rangeStart.value];
      } else if (!rangeStop.value) {
        if (adapter.isSameDay(_value, rangeStart.value)) {
          rangeStart.value = undefined;
          model.value = [];
          return;
        } else if (adapter.isBefore(_value, rangeStart.value)) {
          rangeStop.value = adapter.endOfDay(rangeStart.value);
          rangeStart.value = _value;
        } else {
          rangeStop.value = adapter.endOfDay(_value);
        }
        const diff = adapter.getDiff(rangeStop.value, rangeStart.value, 'days');
        const datesInRange = [rangeStart.value];
        for (let i = 1; i < diff; i++) {
          const nextDate = adapter.addDays(rangeStart.value, i);
          datesInRange.push(nextDate);
        }
        datesInRange.push(rangeStop.value);
        model.value = datesInRange;
      } else {
        rangeStart.value = value;
        rangeStop.value = undefined;
        model.value = [rangeStart.value];
      }
    }
    function onMultipleClick(value) {
      const index = model.value.findIndex(selection => adapter.isSameDay(selection, value));
      if (index === -1) {
        model.value = [...model.value, value];
      } else {
        const value = [...model.value];
        value.splice(index, 1);
        model.value = value;
      }
    }
    function onClick(value) {
      if (props.multiple === 'range') {
        onRangeClick(value);
      } else if (props.multiple) {
        onMultipleClick(value);
      } else {
        model.value = [value];
      }
    }
    return () => _createVNode("div", {
      "class": "v-date-picker-month"
    }, [props.showWeek && _createVNode("div", {
      "key": "weeks",
      "class": "v-date-picker-month__weeks"
    }, [!props.hideWeekdays && _createVNode("div", {
      "key": "hide-week-days",
      "class": "v-date-picker-month__day"
    }, [_createTextVNode("\xA0")]), weekNumbers.value.map(week => _createVNode("div", {
      "class": ['v-date-picker-month__day', 'v-date-picker-month__day--adjacent']
    }, [week]))]), _createVNode(MaybeTransition, {
      "name": transition.value
    }, {
      default: () => [_createVNode("div", {
        "ref": daysRef,
        "key": daysInMonth.value[0].date?.toString(),
        "class": "v-date-picker-month__days"
      }, [!props.hideWeekdays && adapter.getWeekdays(props.firstDayOfWeek).map(weekDay => _createVNode("div", {
        "class": ['v-date-picker-month__day', 'v-date-picker-month__weekday']
      }, [weekDay])), daysInMonth.value.map((item, i) => {
        const slotProps = {
          props: {
            onClick: () => onClick(item.date)
          },
          item,
          i
        };
        if (atMax.value && !item.isSelected) {
          item.isDisabled = true;
        }
        return _createVNode("div", {
          "class": ['v-date-picker-month__day', {
            'v-date-picker-month__day--adjacent': item.isAdjacent,
            'v-date-picker-month__day--hide-adjacent': item.isHidden,
            'v-date-picker-month__day--selected': item.isSelected,
            'v-date-picker-month__day--week-end': item.isWeekEnd,
            'v-date-picker-month__day--week-start': item.isWeekStart
          }],
          "data-v-date": !item.isDisabled ? item.isoDate : undefined
        }, [(props.showAdjacentMonths || !item.isAdjacent) && _createVNode(VDefaultsProvider, {
          "defaults": {
            VBtn: {
              class: 'v-date-picker-month__day-btn',
              color: (item.isSelected || item.isToday) && !item.isDisabled ? props.color : undefined,
              disabled: item.isDisabled,
              icon: true,
              ripple: false,
              text: item.localized,
              variant: item.isDisabled ? item.isToday ? 'outlined' : 'text' : item.isToday && !item.isSelected ? 'outlined' : 'flat',
              onClick: () => onClick(item.date)
            }
          }
        }, {
          default: () => [slots.day?.(slotProps) ?? _createVNode(VBtn, slotProps.props, null)]
        })]);
      })])]
    })]);
  }
});
//# sourceMappingURL=VDatePickerMonth.mjs.map