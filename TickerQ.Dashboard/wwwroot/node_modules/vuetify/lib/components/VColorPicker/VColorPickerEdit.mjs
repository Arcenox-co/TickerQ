import { createVNode as _createVNode } from "vue";
// Styles
import "./VColorPickerEdit.css";

// Components
import { VBtn } from "../VBtn/index.mjs"; // Composables
import { makeComponentProps } from "../../composables/component.mjs"; // Utilities
import { computed } from 'vue';
import { modes, nullColor } from "./util/index.mjs";
import { defineComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
const VColorPickerInput = _ref => {
  let {
    label,
    ...rest
  } = _ref;
  return _createVNode("div", {
    "class": "v-color-picker-edit__input"
  }, [_createVNode("input", rest, null), _createVNode("span", null, [label])]);
};
export const makeVColorPickerEditProps = propsFactory({
  color: Object,
  disabled: Boolean,
  mode: {
    type: String,
    default: 'rgba',
    validator: v => Object.keys(modes).includes(v)
  },
  modes: {
    type: Array,
    default: () => Object.keys(modes),
    validator: v => Array.isArray(v) && v.every(m => Object.keys(modes).includes(m))
  },
  ...makeComponentProps()
}, 'VColorPickerEdit');
export const VColorPickerEdit = defineComponent({
  name: 'VColorPickerEdit',
  props: makeVColorPickerEditProps(),
  emits: {
    'update:color': color => true,
    'update:mode': mode => true
  },
  setup(props, _ref2) {
    let {
      emit
    } = _ref2;
    const enabledModes = computed(() => {
      return props.modes.map(key => ({
        ...modes[key],
        name: key
      }));
    });
    const inputs = computed(() => {
      const mode = enabledModes.value.find(m => m.name === props.mode);
      if (!mode) return [];
      const color = props.color ? mode.to(props.color) : null;
      return mode.inputs?.map(_ref3 => {
        let {
          getValue,
          getColor,
          ...inputProps
        } = _ref3;
        return {
          ...mode.inputProps,
          ...inputProps,
          disabled: props.disabled,
          value: color && getValue(color),
          onChange: e => {
            const target = e.target;
            if (!target) return;
            emit('update:color', mode.from(getColor(color ?? mode.to(nullColor), target.value)));
          }
        };
      });
    });
    useRender(() => _createVNode("div", {
      "class": ['v-color-picker-edit', props.class],
      "style": props.style
    }, [inputs.value?.map(props => _createVNode(VColorPickerInput, props, null)), enabledModes.value.length > 1 && _createVNode(VBtn, {
      "icon": "$unfold",
      "size": "x-small",
      "variant": "plain",
      "onClick": () => {
        const mi = enabledModes.value.findIndex(m => m.name === props.mode);
        emit('update:mode', enabledModes.value[(mi + 1) % enabledModes.value.length].name);
      }
    }, null)]));
    return {};
  }
});
//# sourceMappingURL=VColorPickerEdit.mjs.map