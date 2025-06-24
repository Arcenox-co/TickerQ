import { createVNode as _createVNode } from "vue";
// Styles
import "./VColorPickerSwatches.css";

// Components
import { VIcon } from "../VIcon/index.mjs"; // Composables
import { makeComponentProps } from "../../composables/component.mjs"; // Utilities
import { convertToUnit, deepEqual, defineComponent, getContrast, parseColor, propsFactory, RGBtoCSS, RGBtoHSV, useRender } from "../../util/index.mjs";
import colors from "../../util/colors.mjs"; // Types
export const makeVColorPickerSwatchesProps = propsFactory({
  swatches: {
    type: Array,
    default: () => parseDefaultColors(colors)
  },
  disabled: Boolean,
  color: Object,
  maxHeight: [Number, String],
  ...makeComponentProps()
}, 'VColorPickerSwatches');
function parseDefaultColors(colors) {
  return Object.keys(colors).map(key => {
    const color = colors[key];
    return color.base ? [color.base, color.darken4, color.darken3, color.darken2, color.darken1, color.lighten1, color.lighten2, color.lighten3, color.lighten4, color.lighten5] : [color.black, color.white, color.transparent];
  });
}
export const VColorPickerSwatches = defineComponent({
  name: 'VColorPickerSwatches',
  props: makeVColorPickerSwatchesProps(),
  emits: {
    'update:color': color => true
  },
  setup(props, _ref) {
    let {
      emit
    } = _ref;
    useRender(() => _createVNode("div", {
      "class": ['v-color-picker-swatches', props.class],
      "style": [{
        maxHeight: convertToUnit(props.maxHeight)
      }, props.style]
    }, [_createVNode("div", null, [props.swatches.map(swatch => _createVNode("div", {
      "class": "v-color-picker-swatches__swatch"
    }, [swatch.map(color => {
      const rgba = parseColor(color);
      const hsva = RGBtoHSV(rgba);
      const background = RGBtoCSS(rgba);
      return _createVNode("div", {
        "class": "v-color-picker-swatches__color",
        "onClick": () => hsva && emit('update:color', hsva)
      }, [_createVNode("div", {
        "style": {
          background
        }
      }, [props.color && deepEqual(props.color, hsva) ? _createVNode(VIcon, {
        "size": "x-small",
        "icon": "$success",
        "color": getContrast(color, '#FFFFFF') > 2 ? 'white' : 'black'
      }, null) : undefined])]);
    })]))])]));
    return {};
  }
});
//# sourceMappingURL=VColorPickerSwatches.mjs.map