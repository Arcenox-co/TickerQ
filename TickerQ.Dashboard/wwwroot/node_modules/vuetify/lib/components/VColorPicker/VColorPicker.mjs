import { mergeProps as _mergeProps, createVNode as _createVNode } from "vue";
// Styles
import "./VColorPicker.css";

// Components
import { VColorPickerCanvas } from "./VColorPickerCanvas.mjs";
import { VColorPickerEdit } from "./VColorPickerEdit.mjs";
import { VColorPickerPreview } from "./VColorPickerPreview.mjs";
import { VColorPickerSwatches } from "./VColorPickerSwatches.mjs";
import { makeVSheetProps, VSheet } from "../VSheet/VSheet.mjs"; // Composables
import { provideDefaults } from "../../composables/defaults.mjs";
import { useRtl } from "../../composables/locale.mjs";
import { useProxiedModel } from "../../composables/proxiedModel.mjs"; // Utilities
import { computed, onBeforeMount, ref, watch } from 'vue';
import { extractColor, modes, nullColor } from "./util/index.mjs";
import { consoleWarn, defineComponent, HSVtoCSS, omit, parseColor, propsFactory, RGBtoHSV, useRender } from "../../util/index.mjs"; // Types
export const makeVColorPickerProps = propsFactory({
  canvasHeight: {
    type: [String, Number],
    default: 150
  },
  disabled: Boolean,
  dotSize: {
    type: [Number, String],
    default: 10
  },
  hideCanvas: Boolean,
  hideSliders: Boolean,
  hideInputs: Boolean,
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
  showSwatches: Boolean,
  swatches: Array,
  swatchesMaxHeight: {
    type: [Number, String],
    default: 150
  },
  modelValue: {
    type: [Object, String]
  },
  ...omit(makeVSheetProps({
    width: 300
  }), ['height', 'location', 'minHeight', 'maxHeight', 'minWidth', 'maxWidth'])
}, 'VColorPicker');
export const VColorPicker = defineComponent({
  name: 'VColorPicker',
  props: makeVColorPickerProps(),
  emits: {
    'update:modelValue': color => true,
    'update:mode': mode => true
  },
  setup(props) {
    const mode = useProxiedModel(props, 'mode');
    const hue = ref(null);
    const model = useProxiedModel(props, 'modelValue', undefined, v => {
      if (v == null || v === '') return null;
      let c;
      try {
        c = RGBtoHSV(parseColor(v));
      } catch (err) {
        consoleWarn(err);
        return null;
      }
      return c;
    }, v => {
      if (!v) return null;
      return extractColor(v, props.modelValue);
    });
    const currentColor = computed(() => {
      return model.value ? {
        ...model.value,
        h: hue.value ?? model.value.h
      } : null;
    });
    const {
      rtlClasses
    } = useRtl();
    let externalChange = true;
    watch(model, v => {
      if (!externalChange) {
        // prevent hue shift from rgb conversion inaccuracy
        externalChange = true;
        return;
      }
      if (!v) return;
      hue.value = v.h;
    }, {
      immediate: true
    });
    const updateColor = hsva => {
      externalChange = false;
      hue.value = hsva.h;
      model.value = hsva;
    };
    onBeforeMount(() => {
      if (!props.modes.includes(mode.value)) mode.value = props.modes[0];
    });
    provideDefaults({
      VSlider: {
        color: undefined,
        trackColor: undefined,
        trackFillColor: undefined
      }
    });
    useRender(() => {
      const sheetProps = VSheet.filterProps(props);
      return _createVNode(VSheet, _mergeProps({
        "rounded": props.rounded,
        "elevation": props.elevation,
        "theme": props.theme,
        "class": ['v-color-picker', rtlClasses.value, props.class],
        "style": [{
          '--v-color-picker-color-hsv': HSVtoCSS({
            ...(currentColor.value ?? nullColor),
            a: 1
          })
        }, props.style]
      }, sheetProps, {
        "maxWidth": props.width
      }), {
        default: () => [!props.hideCanvas && _createVNode(VColorPickerCanvas, {
          "key": "canvas",
          "color": currentColor.value,
          "onUpdate:color": updateColor,
          "disabled": props.disabled,
          "dotSize": props.dotSize,
          "width": props.width,
          "height": props.canvasHeight
        }, null), (!props.hideSliders || !props.hideInputs) && _createVNode("div", {
          "key": "controls",
          "class": "v-color-picker__controls"
        }, [!props.hideSliders && _createVNode(VColorPickerPreview, {
          "key": "preview",
          "color": currentColor.value,
          "onUpdate:color": updateColor,
          "hideAlpha": !mode.value.endsWith('a'),
          "disabled": props.disabled
        }, null), !props.hideInputs && _createVNode(VColorPickerEdit, {
          "key": "edit",
          "modes": props.modes,
          "mode": mode.value,
          "onUpdate:mode": m => mode.value = m,
          "color": currentColor.value,
          "onUpdate:color": updateColor,
          "disabled": props.disabled
        }, null)]), props.showSwatches && _createVNode(VColorPickerSwatches, {
          "key": "swatches",
          "color": currentColor.value,
          "onUpdate:color": updateColor,
          "maxHeight": props.swatchesMaxHeight,
          "swatches": props.swatches,
          "disabled": props.disabled
        }, null)]
      });
    });
    return {};
  }
});
//# sourceMappingURL=VColorPicker.mjs.map