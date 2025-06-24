import { createVNode as _createVNode, mergeProps as _mergeProps, Fragment as _Fragment } from "vue";
// Components
import { VBtn } from "../VBtn/index.mjs"; // Composables
import { useLocale } from "../../composables/index.mjs";
import { useProxiedModel } from "../../composables/proxiedModel.mjs"; // Utilities
import { computed, ref, toRaw, watchEffect } from 'vue';
import { deepEqual, genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVConfirmEditProps = propsFactory({
  modelValue: null,
  color: String,
  cancelText: {
    type: String,
    default: '$vuetify.confirmEdit.cancel'
  },
  okText: {
    type: String,
    default: '$vuetify.confirmEdit.ok'
  }
}, 'VConfirmEdit');
export const VConfirmEdit = genericComponent()({
  name: 'VConfirmEdit',
  props: makeVConfirmEditProps(),
  emits: {
    cancel: () => true,
    save: value => true,
    'update:modelValue': value => true
  },
  setup(props, _ref) {
    let {
      emit,
      slots
    } = _ref;
    const model = useProxiedModel(props, 'modelValue');
    const internalModel = ref();
    watchEffect(() => {
      internalModel.value = structuredClone(toRaw(model.value));
    });
    const {
      t
    } = useLocale();
    const isPristine = computed(() => {
      return deepEqual(model.value, internalModel.value);
    });
    function save() {
      model.value = internalModel.value;
      emit('save', internalModel.value);
    }
    function cancel() {
      internalModel.value = structuredClone(toRaw(model.value));
      emit('cancel');
    }
    function actions(actionsProps) {
      return _createVNode(_Fragment, null, [_createVNode(VBtn, _mergeProps({
        "disabled": isPristine.value,
        "variant": "text",
        "color": props.color,
        "onClick": cancel,
        "text": t(props.cancelText)
      }, actionsProps), null), _createVNode(VBtn, _mergeProps({
        "disabled": isPristine.value,
        "variant": "text",
        "color": props.color,
        "onClick": save,
        "text": t(props.okText)
      }, actionsProps), null)]);
    }
    let actionsUsed = false;
    useRender(() => {
      return _createVNode(_Fragment, null, [slots.default?.({
        model: internalModel,
        save,
        cancel,
        isPristine: isPristine.value,
        get actions() {
          actionsUsed = true;
          return actions;
        }
      }), !actionsUsed && actions()]);
    });
    return {
      save,
      cancel,
      isPristine
    };
  }
});
//# sourceMappingURL=VConfirmEdit.mjs.map