import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, VNodeChild, VNode, Ref } from 'vue';

type SlotsToProps<U extends RawSlots, T = MakeInternalSlots<U>> = {
    $children?: (VNodeChild | (T extends {
        default: infer V;
    } ? V : {}) | {
        [K in keyof T]?: T[K];
    });
    'v-slots'?: {
        [K in keyof T]?: T[K] | false;
    };
} & {
    [K in keyof T as `v-slot:${K & string}`]?: T[K] | false;
};
type RawSlots = Record<string, unknown>;
type Slot<T> = [T] extends [never] ? () => VNodeChild : (arg: T) => VNodeChild;
type VueSlot<T> = [T] extends [never] ? () => VNode[] : (arg: T) => VNode[];
type MakeInternalSlots<T extends RawSlots> = {
    [K in keyof T]: Slot<T[K]>;
};
type MakeSlots<T extends RawSlots> = {
    [K in keyof T]: VueSlot<T[K]>;
};
type GenericProps<Props, Slots extends Record<string, unknown>> = {
    $props: Props & SlotsToProps<Slots>;
    $slots: MakeSlots<Slots>;
};
interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type VConfirmEditSlots<T> = {
    default: {
        model: Ref<T>;
        save: () => void;
        cancel: () => void;
        isPristine: boolean;
        get actions(): (props?: {}) => VNode;
    };
};
declare const VConfirmEdit: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        cancelText: string;
        okText: string;
    } & {
        color?: string | undefined;
    } & {
        onCancel?: (() => any) | undefined;
    }, {
        save: () => void;
        cancel: () => void;
        isPristine: vue.ComputedRef<boolean>;
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
        cancel: () => true;
        save: (value: any) => true;
        'update:modelValue': (value: any) => true;
    }, "$children" | "v-slots" | "v-slot:default" | "modelValue" | "update:modelValue" | "save">, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        cancelText: string;
        okText: string;
    } & {
        color?: string | undefined;
    } & {
        onCancel?: (() => any) | undefined;
    }, {
        cancelText: string;
        okText: string;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: {
            model: Ref<unknown>;
            save: () => void;
            cancel: () => void;
            isPristine: boolean;
            readonly actions: (props?: {}) => VNode;
        }) => VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        cancelText: string;
        okText: string;
    } & {
        color?: string | undefined;
    } & {
        onCancel?: (() => any) | undefined;
    }, {
        save: () => void;
        cancel: () => void;
        isPristine: vue.ComputedRef<boolean>;
    }, {}, {}, {}, {
        cancelText: string;
        okText: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    cancelText: string;
    okText: string;
} & {
    color?: string | undefined;
} & {
    onCancel?: (() => any) | undefined;
}, {
    save: () => void;
    cancel: () => void;
    isPristine: vue.ComputedRef<boolean>;
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
    cancel: () => true;
    save: (value: any) => true;
    'update:modelValue': (value: any) => true;
}, "$children" | "v-slots" | "v-slot:default" | "modelValue" | "update:modelValue" | "save">, string, {
    cancelText: string;
    okText: string;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: {
        model: Ref<unknown>;
        save: () => void;
        cancel: () => void;
        isPristine: boolean;
        readonly actions: (props?: {}) => VNode;
    }) => VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & (new <T>(props: {
    modelValue?: T;
    "onUpdate:modelValue"?: (value: T) => void;
    "onSave"?: (value: T) => void;
}, slots: VConfirmEditSlots<T>) => GenericProps<typeof props, typeof slots>) & FilterPropsOptions<{
    modelValue: null;
    color: StringConstructor;
    cancelText: {
        type: StringConstructor;
        default: string;
    };
    okText: {
        type: StringConstructor;
        default: string;
    };
}, vue.ExtractPropTypes<{
    modelValue: null;
    color: StringConstructor;
    cancelText: {
        type: StringConstructor;
        default: string;
    };
    okText: {
        type: StringConstructor;
        default: string;
    };
}>>;
type VConfirmEdit = InstanceType<typeof VConfirmEdit>;

export { VConfirmEdit };
