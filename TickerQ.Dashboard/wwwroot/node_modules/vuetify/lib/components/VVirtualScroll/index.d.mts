import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, VNodeChild, VNode, Ref, PropType } from 'vue';

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

type ClassValue = any;

interface VVirtualScrollSlot<T> {
    item: T;
    index: number;
}
declare const VVirtualScroll: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        itemHeight: string | number;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
    }, {
        calculateVisibleItems: () => void;
        scrollToIndex: (index: number) => void;
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<Record<string, any>, "$children" | "v-slots" | "v-slot:default" | "items" | "renderless">, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        itemHeight: string | number;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
    }, {
        style: vue.StyleValue;
        itemHeight: string | number;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: VVirtualScrollSlot<unknown> | (VVirtualScrollSlot<unknown> & {
            itemRef: Ref<HTMLElement | undefined>;
        })) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        style: vue.StyleValue;
        itemHeight: string | number;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
    }, {
        calculateVisibleItems: () => void;
        scrollToIndex: (index: number) => void;
    }, {}, {}, {}, {
        style: vue.StyleValue;
        itemHeight: string | number;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    itemHeight: string | number;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    class?: any;
}, {
    calculateVisibleItems: () => void;
    scrollToIndex: (index: number) => void;
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<Record<string, any>, "$children" | "v-slots" | "v-slot:default" | "items" | "renderless">, string, {
    style: vue.StyleValue;
    itemHeight: string | number;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: VVirtualScrollSlot<unknown> | (VVirtualScrollSlot<unknown> & {
        itemRef: Ref<HTMLElement | undefined>;
    })) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & (new <T, Renderless extends boolean = false>(props: {
    items?: readonly T[];
    renderless?: Renderless;
}, slots: {
    default: VVirtualScrollSlot<T> & (Renderless extends true ? {
        itemRef: Ref<HTMLElement | undefined>;
    } : {});
}) => GenericProps<typeof props, typeof slots>) & FilterPropsOptions<{
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    itemHeight: {
        type: (StringConstructor | NumberConstructor)[];
        default: null;
    };
    items: {
        type: PropType<readonly unknown[]>;
        default: () => never[];
    };
    renderless: BooleanConstructor;
}, vue.ExtractPropTypes<{
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    itemHeight: {
        type: (StringConstructor | NumberConstructor)[];
        default: null;
    };
    items: {
        type: PropType<readonly unknown[]>;
        default: () => never[];
    };
    renderless: BooleanConstructor;
}>>;
type VVirtualScroll = InstanceType<typeof VVirtualScroll>;

export { VVirtualScroll };
