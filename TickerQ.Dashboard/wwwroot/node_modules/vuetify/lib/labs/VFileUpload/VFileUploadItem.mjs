import { mergeProps as _mergeProps, createVNode as _createVNode, Fragment as _Fragment } from "vue";
// Components
import { VAvatar } from "../../components/VAvatar/VAvatar.mjs";
import { VBtn } from "../../components/VBtn/VBtn.mjs";
import { VDefaultsProvider } from "../../components/VDefaultsProvider/VDefaultsProvider.mjs";
import { makeVListItemProps, VListItem } from "../../components/VList/VListItem.mjs"; // Utilities
import { computed, ref, watchEffect } from 'vue';
import { genericComponent, humanReadableFileSize, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVFileUploadItemProps = propsFactory({
  clearable: Boolean,
  file: {
    type: Object,
    default: null
  },
  fileIcon: {
    type: String,
    // TODO: setup up a proper aliased icon
    default: 'mdi-file-document'
  },
  showSize: Boolean,
  ...makeVListItemProps({
    border: true,
    rounded: true,
    lines: 'two'
  })
}, 'VFileUploadItem');
export const VFileUploadItem = genericComponent()({
  name: 'VFileUploadItem',
  props: makeVFileUploadItemProps(),
  emits: {
    'click:remove': () => true,
    click: e => true
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    const preview = ref();
    const base = computed(() => typeof props.showSize !== 'boolean' ? props.showSize : undefined);
    function onClickRemove() {
      emit('click:remove');
    }
    watchEffect(() => {
      preview.value = props.file?.type.startsWith('image') ? URL.createObjectURL(props.file) : undefined;
    });
    useRender(() => {
      const listItemProps = VListItem.filterProps(props);
      return _createVNode(VListItem, _mergeProps(listItemProps, {
        "title": props.title ?? props.file?.name,
        "subtitle": props.showSize ? humanReadableFileSize(props.file?.size, base.value) : props.file?.type,
        "class": "v-file-upload-item"
      }), {
        ...slots,
        prepend: slotProps => _createVNode(_Fragment, null, [!slots.prepend ? _createVNode(VAvatar, {
          "icon": props.fileIcon,
          "image": preview.value,
          "rounded": true
        }, null) : _createVNode(VDefaultsProvider, {
          "defaults": {
            VAvatar: {
              image: preview.value,
              icon: !preview.value ? props.fileIcon : undefined,
              rounded: true
            }
          }
        }, {
          default: () => [slots.prepend?.(slotProps) ?? _createVNode(VAvatar, null, null)]
        })]),
        append: slotProps => _createVNode(_Fragment, null, [props.clearable && _createVNode(_Fragment, null, [!slots.clear ? _createVNode(VBtn, {
          "icon": "$clear",
          "density": "comfortable",
          "variant": "text",
          "onClick": onClickRemove
        }, null) : _createVNode(VDefaultsProvider, {
          "defaults": {
            VBtn: {
              icon: '$clear',
              density: 'comfortable',
              variant: 'text'
            }
          }
        }, {
          default: () => [slots.clear?.({
            ...slotProps,
            props: {
              onClick: onClickRemove
            }
          }) ?? _createVNode(VBtn, null, null)]
        })]), slots.append?.(slotProps)])
      });
    });
  }
});
//# sourceMappingURL=VFileUploadItem.mjs.map