import { mergeProps as _mergeProps, createVNode as _createVNode, Fragment as _Fragment } from "vue";
// Components
import { VBtn } from "../../components/VBtn/index.mjs";
import { VDefaultsProvider } from "../../components/VDefaultsProvider/index.mjs";
import { makeVSnackbarProps, VSnackbar } from "../../components/VSnackbar/VSnackbar.mjs"; // Composables
import { useLocale } from "../../composables/locale.mjs"; // Utilities
import { computed, nextTick, shallowRef, watch } from 'vue';
import { genericComponent, omit, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVSnackbarQueueProps = propsFactory({
  // TODO: Port this to Snackbar on dev
  closable: [Boolean, String],
  closeText: {
    type: String,
    default: '$vuetify.dismiss'
  },
  modelValue: {
    type: Array,
    default: () => []
  },
  ...omit(makeVSnackbarProps(), ['modelValue'])
}, 'VSnackbarQueue');
export const VSnackbarQueue = genericComponent()({
  name: 'VSnackbarQueue',
  props: makeVSnackbarQueueProps(),
  emits: {
    'update:modelValue': val => true
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    const {
      t
    } = useLocale();
    const isActive = shallowRef(false);
    const isVisible = shallowRef(false);
    const current = shallowRef();
    watch(() => props.modelValue.length, (val, oldVal) => {
      if (!isVisible.value && val > oldVal) {
        showNext();
      }
    });
    watch(isActive, val => {
      if (val) isVisible.value = true;
    });
    function onAfterLeave() {
      if (props.modelValue.length) {
        showNext();
      } else {
        current.value = undefined;
        isVisible.value = false;
      }
    }
    function showNext() {
      const [next, ...rest] = props.modelValue;
      emit('update:modelValue', rest);
      current.value = typeof next === 'string' ? {
        text: next
      } : next;
      nextTick(() => {
        isActive.value = true;
      });
    }
    function onClickClose() {
      isActive.value = false;
    }
    const btnProps = computed(() => ({
      color: typeof props.closable === 'string' ? props.closable : undefined,
      text: t(props.closeText)
    }));
    useRender(() => {
      const hasActions = !!(props.closable || slots.actions);
      const {
        modelValue: _,
        ...snackbarProps
      } = VSnackbar.filterProps(props);
      return _createVNode(_Fragment, null, [isVisible.value && !!current.value && (slots.default ? _createVNode(VDefaultsProvider, {
        "defaults": {
          VSnackbar: current.value
        }
      }, {
        default: () => [slots.default({
          item: current.value
        })]
      }) : _createVNode(VSnackbar, _mergeProps(snackbarProps, current.value, {
        "modelValue": isActive.value,
        "onUpdate:modelValue": $event => isActive.value = $event,
        "onAfterLeave": onAfterLeave
      }), {
        text: slots.text ? () => slots.text?.({
          item: current.value
        }) : undefined,
        actions: hasActions ? () => _createVNode(_Fragment, null, [!slots.actions ? _createVNode(VBtn, _mergeProps(btnProps.value, {
          "onClick": onClickClose
        }), null) : _createVNode(VDefaultsProvider, {
          "defaults": {
            VBtn: btnProps.value
          }
        }, {
          default: () => [slots.actions({
            item: current.value,
            props: {
              onClick: onClickClose
            }
          })]
        })]) : undefined
      }))]);
    });
  }
});
//# sourceMappingURL=VSnackbarQueue.mjs.map