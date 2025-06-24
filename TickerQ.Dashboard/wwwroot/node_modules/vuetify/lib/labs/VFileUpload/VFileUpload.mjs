import { resolveDirective as _resolveDirective, Fragment as _Fragment, createVNode as _createVNode, mergeProps as _mergeProps } from "vue";
// Styles
import "./VFileUpload.css";

// Components
import { VFileUploadItem } from "./VFileUploadItem.mjs";
import { VBtn } from "../../components/VBtn/VBtn.mjs";
import { VDefaultsProvider } from "../../components/VDefaultsProvider/VDefaultsProvider.mjs";
import { makeVDividerProps, VDivider } from "../../components/VDivider/VDivider.mjs";
import { VIcon } from "../../components/VIcon/VIcon.mjs";
import { VOverlay } from "../../components/VOverlay/VOverlay.mjs";
import { makeVSheetProps, VSheet } from "../../components/VSheet/VSheet.mjs"; // Composables
import { makeDelayProps } from "../../composables/delay.mjs";
import { makeDensityProps, useDensity } from "../../composables/density.mjs";
import { IconValue } from "../../composables/icons.mjs";
import { useLocale } from "../../composables/locale.mjs";
import { useProxiedModel } from "../../composables/proxiedModel.mjs"; // Utilities
import { onMounted, onUnmounted, ref, shallowRef } from 'vue';
import { filterInputAttrs, genericComponent, only, propsFactory, useRender, wrapInArray } from "../../util/index.mjs"; // Types
export const makeVFileUploadProps = propsFactory({
  browseText: {
    type: String,
    default: '$vuetify.fileUpload.browse'
  },
  dividerText: {
    type: String,
    default: '$vuetify.fileUpload.divider'
  },
  title: {
    type: String,
    default: '$vuetify.fileUpload.title'
  },
  subtitle: String,
  icon: {
    type: IconValue,
    default: '$upload'
  },
  modelValue: {
    type: [Array, Object],
    default: null,
    validator: val => {
      return wrapInArray(val).every(v => v != null && typeof v === 'object');
    }
  },
  clearable: Boolean,
  disabled: Boolean,
  hideBrowse: Boolean,
  multiple: Boolean,
  scrim: {
    type: [Boolean, String],
    default: true
  },
  showSize: Boolean,
  name: String,
  ...makeDelayProps(),
  ...makeDensityProps(),
  ...only(makeVDividerProps({
    length: 150
  }), ['length', 'thickness', 'opacity']),
  ...makeVSheetProps()
}, 'VFileUpload');
export const VFileUpload = genericComponent()({
  name: 'VFileUpload',
  inheritAttrs: false,
  props: makeVFileUploadProps(),
  emits: {
    'update:modelValue': files => true
  },
  setup(props, _ref) {
    let {
      attrs,
      slots
    } = _ref;
    const {
      t
    } = useLocale();
    const {
      densityClasses
    } = useDensity(props);
    const model = useProxiedModel(props, 'modelValue', props.modelValue, val => wrapInArray(val), val => props.multiple || Array.isArray(props.modelValue) ? val : val[0]);
    const dragOver = shallowRef(false);
    const vSheetRef = ref(null);
    const inputRef = ref(null);
    onMounted(() => {
      vSheetRef.value?.$el.addEventListener('dragover', onDragOver);
      vSheetRef.value?.$el.addEventListener('drop', onDrop);
    });
    onUnmounted(() => {
      vSheetRef.value?.$el.removeEventListener('dragover', onDragOver);
      vSheetRef.value?.$el.removeEventListener('drop', onDrop);
    });
    function onDragOver(e) {
      e.preventDefault();
      e.stopImmediatePropagation();
      dragOver.value = true;
    }
    function onDragLeave(e) {
      e.preventDefault();
      dragOver.value = false;
    }
    function onDrop(e) {
      e.preventDefault();
      e.stopImmediatePropagation();
      dragOver.value = false;
      const files = Array.from(e.dataTransfer?.files ?? []);
      if (!files.length) return;
      if (!props.multiple) {
        model.value = [files[0]];
        return;
      }
      const array = model.value.slice();
      for (const file of files) {
        if (!array.some(f => f.name === file.name)) {
          array.push(file);
        }
      }
      model.value = array;
    }
    function onClick() {
      inputRef.value?.click();
    }
    function onClickRemove(index) {
      model.value = model.value.filter((_, i) => i !== index);
      if (model.value.length > 0 || !inputRef.value) return;
      inputRef.value.value = '';
    }
    useRender(() => {
      const hasTitle = !!(slots.title || props.title);
      const hasIcon = !!(slots.icon || props.icon);
      const hasBrowse = !!(!props.hideBrowse && (slots.browse || props.density === 'default'));
      const cardProps = VSheet.filterProps(props);
      const dividerProps = VDivider.filterProps(props);
      const [rootAttrs, inputAttrs] = filterInputAttrs(attrs);
      const inputNode = _createVNode("input", _mergeProps({
        "ref": inputRef,
        "type": "file",
        "disabled": props.disabled,
        "multiple": props.multiple,
        "name": props.name,
        "onChange": e => {
          if (!e.target) return;
          const target = e.target;
          model.value = [...(target.files ?? [])];
        }
      }, inputAttrs), null);
      return _createVNode(_Fragment, null, [_createVNode(VSheet, _mergeProps({
        "ref": vSheetRef
      }, cardProps, {
        "class": ['v-file-upload', {
          'v-file-upload--clickable': !hasBrowse,
          'v-file-upload--disabled': props.disabled,
          'v-file-upload--dragging': dragOver.value
        }, densityClasses.value],
        "onDragleave": onDragLeave,
        "onDragover": onDragOver,
        "onDrop": onDrop,
        "onClick": !hasBrowse ? onClick : undefined
      }, rootAttrs), {
        default: () => [hasIcon && _createVNode("div", {
          "key": "icon",
          "class": "v-file-upload-icon"
        }, [!slots.icon ? _createVNode(VIcon, {
          "key": "icon-icon",
          "icon": props.icon
        }, null) : _createVNode(VDefaultsProvider, {
          "key": "icon-defaults",
          "defaults": {
            VIcon: {
              icon: props.icon
            }
          }
        }, {
          default: () => [slots.icon()]
        })]), hasTitle && _createVNode("div", {
          "key": "title",
          "class": "v-file-upload-title"
        }, [slots.title?.() ?? t(props.title)]), props.density === 'default' && _createVNode(_Fragment, null, [_createVNode("div", {
          "key": "upload-divider",
          "class": "v-file-upload-divider"
        }, [slots.divider?.() ?? _createVNode(VDivider, dividerProps, {
          default: () => [t(props.dividerText)]
        })]), hasBrowse && _createVNode(_Fragment, null, [!slots.browse ? _createVNode(VBtn, {
          "readonly": props.disabled,
          "size": "large",
          "text": t(props.browseText),
          "variant": "tonal",
          "onClick": onClick
        }, null) : _createVNode(VDefaultsProvider, {
          "defaults": {
            VBtn: {
              readonly: props.disabled,
              size: 'large',
              text: t(props.browseText),
              variant: 'tonal'
            }
          }
        }, {
          default: () => [slots.browse({
            props: {
              onClick
            }
          })]
        })]), props.subtitle && _createVNode("div", {
          "class": "v-file-upload-subtitle"
        }, [props.subtitle])]), _createVNode(VOverlay, {
          "model-value": dragOver.value,
          "contained": true,
          "scrim": props.scrim
        }, null), slots.input?.({
          inputNode
        }) ?? inputNode]
      }), model.value.length > 0 && _createVNode("div", {
        "class": "v-file-upload-items"
      }, [model.value.map((file, i) => {
        const slotProps = {
          file,
          props: {
            'onClick:remove': () => onClickRemove(i)
          }
        };
        return _createVNode(VDefaultsProvider, {
          "key": i,
          "defaults": {
            VFileUploadItem: {
              file,
              clearable: props.clearable,
              disabled: props.disabled,
              showSize: props.showSize
            }
          }
        }, {
          default: () => [slots.item?.(slotProps) ?? _createVNode(VFileUploadItem, {
            "key": i,
            "onClick:remove": () => onClickRemove(i)
          }, slots)]
        });
      })])]);
    });
  }
});
//# sourceMappingURL=VFileUpload.mjs.map