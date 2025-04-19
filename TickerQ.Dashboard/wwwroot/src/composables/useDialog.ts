import {
  defineAsyncComponent,
  reactive,
  proxyRefs,
  type Component,
  ref,
  type Reactive,
  type Ref,
  type ComponentPublicInstance,
  type AsyncComponentLoader,
  type AsyncComponentOptions,
  shallowRef
} from 'vue';

export interface UseDialogReturnWithComponent<TData extends object, T extends Component = { new(): ComponentPublicInstance; }> {
  isOpen: boolean;
  propData: Reactive<TData>;
  open: (data?: TData) => void;
  close: () => void;
  Component: T;
  cleanUpData: () => void;
  setPropData: (data: TData) => void;
}

export interface UseDialogReturn<TData extends object> {
  isOpen: boolean;
  propData: Reactive<TData>;
  open: (data?: TData) => void;
  close: () => void;
}

export function useDialog<TData extends object>() {
  const isOpen: Ref<boolean> = ref(false);
  const propData: Ref<Reactive<TData>> = ref({} as Reactive<TData>);
  /**
   * Opens the dialog and optionally sets the dialog data.
   *
   * @param newData - The data to set for the dialog.
   */
  function open(newData?: TData) {
    isOpen.value = true;
    if (newData) {
      // Wrap the data with reactive to ensure deep reactivity
      propData.value = reactive(newData);
    }
  }

  function cleanUpData() {
    propData.value = reactive({} as TData);
  }

  function setPropData(data: TData) {
    propData.value = reactive(data);
  }

  /**
   * Closes the dialog.
   */
  function close(cleanUpData = false) {
    isOpen.value = false;
    if(cleanUpData){
      propData.value = reactive({} as TData);
    }
  }

  return {
    withoutComponent(): UseDialogReturn<TData> {
      return proxyRefs({
        isOpen,
        propData,
        open,
        close
      });
    },
    withComponent<T extends Component = { new(): ComponentPublicInstance; }>(
      loader: AsyncComponentLoader<T> | AsyncComponentOptions<T>
    ): UseDialogReturnWithComponent<TData, T> {
      const component: Ref<T> = shallowRef(defineAsyncComponent(loader));

      return proxyRefs({
        isOpen,
        propData,
        open,
        close,
        cleanUpData,
        setPropData,
        Component: component
      });
    }
  };
};
