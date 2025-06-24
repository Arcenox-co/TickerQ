import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, VNodeChild, VNode, PropType, ComponentPublicInstance, FunctionalComponent, ComponentInternalInstance, Ref, ComputedRef } from 'vue';

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

declare function deepEqual(a: any, b: any): boolean;

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

interface GroupItem {
    id: number;
    value: Ref<unknown>;
    disabled: Ref<boolean | undefined>;
    useIndexAsValue?: boolean;
}
interface GroupProvide {
    register: (item: GroupItem, cmp: ComponentInternalInstance) => void;
    unregister: (id: number) => void;
    select: (id: number, value: boolean) => void;
    selected: Ref<Readonly<number[]>>;
    isSelected: (id: number) => boolean;
    prev: () => void;
    next: () => void;
    selectedClass: Ref<string | undefined>;
    items: ComputedRef<{
        id: number;
        value: unknown;
        disabled: boolean | undefined;
    }[]>;
    disabled: Ref<boolean | undefined>;
    getItemIndex: (value: unknown) => number;
}

declare const breakpoints: readonly ["sm", "md", "lg", "xl", "xxl"];
type Breakpoint = typeof breakpoints[number];
type DisplayBreakpoint = 'xs' | Breakpoint;

declare const allowedVariants: readonly ["elevated", "flat", "tonal", "outlined", "text", "plain"];
type Variant = typeof allowedVariants[number];

type VChipGroupSlots = {
    default: {
        isSelected: (id: number) => boolean;
        select: (id: number, value: boolean) => void;
        next: () => void;
        prev: () => void;
        selected: readonly number[];
    };
};
declare const VChipGroup: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        symbol: any;
        filter: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        mobile: boolean | null;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        column: boolean;
        selectedClass: string;
        valueComparator: typeof deepEqual;
        centerActive: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
    } & {
        max?: number | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        mandatory?: boolean | "force" | undefined;
        mobileBreakpoint?: number | DisplayBreakpoint | undefined;
        showArrows?: string | boolean | undefined;
    } & {}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
        'update:modelValue': (value: any) => true;
    }, "$children" | "v-slots" | "v-slot:default" | "modelValue" | "update:modelValue">, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        symbol: any;
        filter: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        mobile: boolean | null;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        column: boolean;
        selectedClass: string;
        valueComparator: typeof deepEqual;
        centerActive: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
    } & {
        max?: number | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        mandatory?: boolean | "force" | undefined;
        mobileBreakpoint?: number | DisplayBreakpoint | undefined;
        showArrows?: string | boolean | undefined;
    } & {}, {
        symbol: any;
        filter: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        mobile: boolean | null;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        column: boolean;
        selectedClass: string;
        valueComparator: typeof deepEqual;
        centerActive: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: {
            isSelected: (id: number) => boolean;
            select: (id: number, value: boolean) => void;
            next: () => void;
            prev: () => void;
            selected: readonly number[];
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        symbol: any;
        filter: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        mobile: boolean | null;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        column: boolean;
        selectedClass: string;
        valueComparator: typeof deepEqual;
        centerActive: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
    } & {
        max?: number | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        mandatory?: boolean | "force" | undefined;
        mobileBreakpoint?: number | DisplayBreakpoint | undefined;
        showArrows?: string | boolean | undefined;
    } & {}, {}, {}, {}, {}, {
        symbol: any;
        filter: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        mobile: boolean | null;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        column: boolean;
        selectedClass: string;
        valueComparator: typeof deepEqual;
        centerActive: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    symbol: any;
    filter: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    direction: "horizontal" | "vertical";
    style: vue.StyleValue;
    mobile: boolean | null;
    disabled: boolean;
    multiple: boolean;
    tag: string;
    column: boolean;
    selectedClass: string;
    valueComparator: typeof deepEqual;
    centerActive: boolean;
    nextIcon: IconValue;
    prevIcon: IconValue;
} & {
    max?: number | undefined;
    color?: string | undefined;
    class?: any;
    theme?: string | undefined;
    mandatory?: boolean | "force" | undefined;
    mobileBreakpoint?: number | DisplayBreakpoint | undefined;
    showArrows?: string | boolean | undefined;
} & {}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
    'update:modelValue': (value: any) => true;
}, "$children" | "v-slots" | "v-slot:default" | "modelValue" | "update:modelValue">, string, {
    symbol: any;
    filter: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    direction: "horizontal" | "vertical";
    style: vue.StyleValue;
    mobile: boolean | null;
    disabled: boolean;
    multiple: boolean;
    tag: string;
    column: boolean;
    selectedClass: string;
    valueComparator: typeof deepEqual;
    centerActive: boolean;
    nextIcon: IconValue;
    prevIcon: IconValue;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: {
        isSelected: (id: number) => boolean;
        select: (id: number, value: boolean) => void;
        next: () => void;
        prev: () => void;
        selected: readonly number[];
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & (new <T>(props: {
    modelValue?: T;
    "onUpdate:modelValue"?: (value: T) => void;
}, slots: VChipGroupSlots) => GenericProps<typeof props, typeof slots>) & FilterPropsOptions<{
    color: StringConstructor;
    variant: Omit<{
        type: PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    modelValue: {
        type: null;
        default: undefined;
    };
    multiple: BooleanConstructor;
    mandatory: PropType<boolean | "force">;
    max: NumberConstructor;
    selectedClass: {
        type: PropType<string>;
        default: string;
    };
    disabled: BooleanConstructor;
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    mobile: Omit<{
        type: PropType<boolean | null>;
        default: boolean;
    }, "type" | "default"> & {
        type: PropType<boolean | null>;
        default: NonNullable<boolean | null> | null;
    };
    mobileBreakpoint: PropType<number | DisplayBreakpoint>;
    centerActive: BooleanConstructor;
    direction: {
        type: PropType<"horizontal" | "vertical">;
        default: string;
    };
    symbol: {
        type: null;
        default: vue.InjectionKey<GroupProvide>;
    };
    nextIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    prevIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    showArrows: {
        type: (StringConstructor | BooleanConstructor)[];
        validator: (v: any) => boolean;
    };
    column: BooleanConstructor;
    filter: BooleanConstructor;
    valueComparator: {
        type: PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
}, vue.ExtractPropTypes<{
    color: StringConstructor;
    variant: Omit<{
        type: PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    modelValue: {
        type: null;
        default: undefined;
    };
    multiple: BooleanConstructor;
    mandatory: PropType<boolean | "force">;
    max: NumberConstructor;
    selectedClass: {
        type: PropType<string>;
        default: string;
    };
    disabled: BooleanConstructor;
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    mobile: Omit<{
        type: PropType<boolean | null>;
        default: boolean;
    }, "type" | "default"> & {
        type: PropType<boolean | null>;
        default: NonNullable<boolean | null> | null;
    };
    mobileBreakpoint: PropType<number | DisplayBreakpoint>;
    centerActive: BooleanConstructor;
    direction: {
        type: PropType<"horizontal" | "vertical">;
        default: string;
    };
    symbol: {
        type: null;
        default: vue.InjectionKey<GroupProvide>;
    };
    nextIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    prevIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    showArrows: {
        type: (StringConstructor | BooleanConstructor)[];
        validator: (v: any) => boolean;
    };
    column: BooleanConstructor;
    filter: BooleanConstructor;
    valueComparator: {
        type: PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
}>>;
type VChipGroup = InstanceType<typeof VChipGroup>;

export { VChipGroup };
