import { createVNode as _createVNode } from "vue";
// Styles
import "./VColorPickerPreview.css";

// Components
import { VBtn } from "../VBtn/index.mjs";
import { VSlider } from "../VSlider/index.mjs"; // Composables
import { makeComponentProps } from "../../composables/component.mjs"; // Utilities
import { onUnmounted } from 'vue';
import { nullColor } from "./util/index.mjs";
import { defineComponent, HexToHSV, HSVtoCSS, propsFactory, SUPPORTS_EYE_DROPPER, useRender } from "../../util/index.mjs"; // Types
export const makeVColorPickerPreviewProps = propsFactory({
  color: {
    type: Object
  },
  disabled: Boolean,
  hideAlpha: Boolean,
  ...makeComponentProps()
}, 'VColorPickerPreview');
export const VColorPickerPreview = defineComponent({
  name: 'VColorPickerPreview',
  props: makeVColorPickerPreviewProps(),
  emits: {
    'update:color': color => true
  },
  setup(props, _ref) {
    let {
      emit
    } = _ref;
    const abortController = new AbortController();
    onUnmounted(() => abortController.abort());
    async function openEyeDropper() {
      if (!SUPPORTS_EYE_DROPPER) return;
      const eyeDropper = new window.EyeDropper();
      try {
        const result = await eyeDropper.open({
          signal: abortController.signal
        });
        const colorHexValue = HexToHSV(result.sRGBHex);
        emit('update:color', {
          ...(props.color ?? nullColor),
          ...colorHexValue
        });
      } catch (e) {}
    }
    useRender(() => _createVNode("div", {
      "class": ['v-color-picker-preview', {
        'v-color-picker-preview--hide-alpha': props.hideAlpha
      }, props.class],
      "style": props.style
    }, [SUPPORTS_EYE_DROPPER && _createVNode("div", {
      "class": "v-color-picker-preview__eye-dropper",
      "key": "eyeDropper"
    }, [_createVNode(VBtn, {
      "onClick": openEyeDropper,
      "icon": "$eyeDropper",
      "variant": "plain",
      "density": "comfortable"
    }, null)]), _createVNode("div", {
      "class": "v-color-picker-preview__dot"
    }, [_createVNode("div", {
      "style": {
        background: HSVtoCSS(props.color ?? nullColor)
      }
    }, null)]), _createVNode("div", {
      "class": "v-color-picker-preview__sliders"
    }, [_createVNode(VSlider, {
      "class": "v-color-picker-preview__track v-color-picker-preview__hue",
      "modelValue": props.color?.h,
      "onUpdate:modelValue": h => emit('update:color', {
        ...(props.color ?? nullColor),
        h
      }),
      "step": 0,
      "min": 0,
      "max": 360,
      "disabled": props.disabled,
      "thumbSize": 14,
      "trackSize": 8,
      "trackFillColor": "white",
      "hideDetails": true
    }, null), !props.hideAlpha && _createVNode(VSlider, {
      "class": "v-color-picker-preview__track v-color-picker-preview__alpha",
      "modelValue": props.color?.a ?? 1,
      "onUpdate:modelValue": a => emit('update:color', {
        ...(props.color ?? nullColor),
        a
      }),
      "step": 1 / 256,
      "min": 0,
      "max": 1,
      "disabled": props.disabled,
      "thumbSize": 14,
      "trackSize": 8,
      "trackFillColor": "white",
      "hideDetails": true
    }, null)])]));
    return {};
  }
});
//# sourceMappingURL=VColorPickerPreview.mjs.map