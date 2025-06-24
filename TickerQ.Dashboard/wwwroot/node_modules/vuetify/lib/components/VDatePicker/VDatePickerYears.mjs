import { createVNode as _createVNode, mergeProps as _mergeProps } from "vue";
// Styles
import "./VDatePickerYears.css";

// Components
import { VBtn } from "../VBtn/index.mjs"; // Composables
import { useDate } from "../../composables/date/index.mjs";
import { useProxiedModel } from "../../composables/proxiedModel.mjs"; // Utilities
import { computed, nextTick, onMounted, watchEffect } from 'vue';
import { convertToUnit, createRange, genericComponent, propsFactory, templateRef, useRender } from "../../util/index.mjs"; // Types
// Types
export const makeVDatePickerYearsProps = propsFactory({
  color: String,
  height: [String, Number],
  min: null,
  max: null,
  modelValue: Number
}, 'VDatePickerYears');
export const VDatePickerYears = genericComponent()({
  name: 'VDatePickerYears',
  props: makeVDatePickerYearsProps(),
  emits: {
    'update:modelValue': year => true
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    const adapter = useDate();
    const model = useProxiedModel(props, 'modelValue');
    const years = computed(() => {
      const year = adapter.getYear(adapter.date());
      let min = year - 100;
      let max = year + 52;
      if (props.min) {
        min = adapter.getYear(adapter.date(props.min));
      }
      if (props.max) {
        max = adapter.getYear(adapter.date(props.max));
      }
      let date = adapter.startOfYear(adapter.date());
      date = adapter.setYear(date, min);
      return createRange(max - min + 1, min).map(i => {
        const text = adapter.format(date, 'year');
        date = adapter.setYear(date, adapter.getYear(date) + 1);
        return {
          text,
          value: i
        };
      });
    });
    watchEffect(() => {
      model.value = model.value ?? adapter.getYear(adapter.date());
    });
    const yearRef = templateRef();
    onMounted(async () => {
      await nextTick();
      yearRef.el?.scrollIntoView({
        block: 'center'
      });
    });
    useRender(() => _createVNode("div", {
      "class": "v-date-picker-years",
      "style": {
        height: convertToUnit(props.height)
      }
    }, [_createVNode("div", {
      "class": "v-date-picker-years__content"
    }, [years.value.map((year, i) => {
      const btnProps = {
        ref: model.value === year.value ? yearRef : undefined,
        active: model.value === year.value,
        color: model.value === year.value ? props.color : undefined,
        rounded: true,
        text: year.text,
        variant: model.value === year.value ? 'flat' : 'text',
        onClick: () => {
          if (model.value === year.value) {
            emit('update:modelValue', model.value);
            return;
          }
          model.value = year.value;
        }
      };
      return slots.year?.({
        year,
        i,
        props: btnProps
      }) ?? _createVNode(VBtn, _mergeProps({
        "key": "month"
      }, btnProps), null);
    })])]));
    return {};
  }
});
//# sourceMappingURL=VDatePickerYears.mjs.map