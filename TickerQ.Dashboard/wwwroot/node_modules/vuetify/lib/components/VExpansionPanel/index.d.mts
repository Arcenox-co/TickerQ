import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType, ComponentPublicInstance, FunctionalComponent, DirectiveBinding, Ref, ComponentInternalInstance, ComputedRef } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

interface RippleDirectiveBinding extends Omit<DirectiveBinding, 'modifiers' | 'value'> {
    value?: boolean | {
        class: string;
    };
    modifiers: {
        center?: boolean;
        circle?: boolean;
        stop?: boolean;
    };
}

declare const allowedVariants: readonly ["default", "accordion", "inset", "popout"];
type Variant = typeof allowedVariants[number];
type VExpansionPanelSlot = {
    prev: () => void;
    next: () => void;
};
declare const VExpansionPanels: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        flat: boolean;
        variant: "default" | "inset" | "accordion" | "popout";
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        multiple: boolean;
        readonly: boolean;
        tag: string;
        static: boolean;
        focusable: boolean;
        tile: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    } & {
        max?: number | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        mandatory?: boolean | "force" | undefined;
        elevation?: string | number | undefined;
        modelValue?: any;
        rounded?: string | number | boolean | undefined;
        selectedClass?: string | undefined;
        bgColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
        } | ((arg: VExpansionPanelSlot) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: unknown) => any) | undefined;
    }, {
        next: () => void;
        prev: () => void;
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (val: unknown) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        flat: boolean;
        variant: "default" | "inset" | "accordion" | "popout";
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        multiple: boolean;
        readonly: boolean;
        tag: string;
        static: boolean;
        focusable: boolean;
        tile: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    } & {
        max?: number | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        mandatory?: boolean | "force" | undefined;
        elevation?: string | number | undefined;
        modelValue?: any;
        rounded?: string | number | boolean | undefined;
        selectedClass?: string | undefined;
        bgColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
        } | ((arg: VExpansionPanelSlot) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: unknown) => any) | undefined;
    }, {
        flat: boolean;
        variant: "default" | "inset" | "accordion" | "popout";
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        multiple: boolean;
        readonly: boolean;
        tag: string;
        static: boolean;
        focusable: boolean;
        modelValue: any;
        rounded: string | number | boolean;
        tile: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: VExpansionPanelSlot) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        flat: boolean;
        variant: "default" | "inset" | "accordion" | "popout";
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        multiple: boolean;
        readonly: boolean;
        tag: string;
        static: boolean;
        focusable: boolean;
        tile: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    } & {
        max?: number | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        mandatory?: boolean | "force" | undefined;
        elevation?: string | number | undefined;
        modelValue?: any;
        rounded?: string | number | boolean | undefined;
        selectedClass?: string | undefined;
        bgColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
        } | ((arg: VExpansionPanelSlot) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: unknown) => any) | undefined;
    }, {
        next: () => void;
        prev: () => void;
    }, {}, {}, {}, {
        flat: boolean;
        variant: "default" | "inset" | "accordion" | "popout";
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        multiple: boolean;
        readonly: boolean;
        tag: string;
        static: boolean;
        focusable: boolean;
        modelValue: any;
        rounded: string | number | boolean;
        tile: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    flat: boolean;
    variant: "default" | "inset" | "accordion" | "popout";
    style: vue.StyleValue;
    eager: boolean;
    disabled: boolean;
    multiple: boolean;
    readonly: boolean;
    tag: string;
    static: boolean;
    focusable: boolean;
    tile: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    hideActions: boolean;
} & {
    max?: number | undefined;
    color?: string | undefined;
    class?: any;
    theme?: string | undefined;
    mandatory?: boolean | "force" | undefined;
    elevation?: string | number | undefined;
    modelValue?: any;
    rounded?: string | number | boolean | undefined;
    selectedClass?: string | undefined;
    bgColor?: string | undefined;
} & {
    $children?: vue.VNodeChild | {
        default?: ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
    } | ((arg: VExpansionPanelSlot) => vue.VNodeChild);
    'v-slots'?: {
        default?: false | ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | ((arg: VExpansionPanelSlot) => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((val: unknown) => any) | undefined;
}, {
    next: () => void;
    prev: () => void;
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (val: unknown) => true;
}, string, {
    flat: boolean;
    variant: "default" | "inset" | "accordion" | "popout";
    style: vue.StyleValue;
    eager: boolean;
    disabled: boolean;
    multiple: boolean;
    readonly: boolean;
    tag: string;
    static: boolean;
    focusable: boolean;
    modelValue: any;
    rounded: string | number | boolean;
    tile: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    hideActions: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: VExpansionPanelSlot) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    variant: {
        type: PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    };
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    theme: StringConstructor;
    color: StringConstructor;
    eager: BooleanConstructor;
    readonly: BooleanConstructor;
    static: BooleanConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    focusable: BooleanConstructor;
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    bgColor: StringConstructor;
    ripple: {
        type: PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    collapseIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    expandIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    hideActions: BooleanConstructor;
    modelValue: {
        type: null;
        default: undefined;
    };
    multiple: BooleanConstructor;
    mandatory: PropType<boolean | "force">;
    max: NumberConstructor;
    selectedClass: StringConstructor;
    disabled: BooleanConstructor;
    flat: BooleanConstructor;
}, vue.ExtractPropTypes<{
    variant: {
        type: PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    };
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    theme: StringConstructor;
    color: StringConstructor;
    eager: BooleanConstructor;
    readonly: BooleanConstructor;
    static: BooleanConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    focusable: BooleanConstructor;
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    bgColor: StringConstructor;
    ripple: {
        type: PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    collapseIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    expandIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    hideActions: BooleanConstructor;
    modelValue: {
        type: null;
        default: undefined;
    };
    multiple: BooleanConstructor;
    mandatory: PropType<boolean | "force">;
    max: NumberConstructor;
    selectedClass: StringConstructor;
    disabled: BooleanConstructor;
    flat: BooleanConstructor;
}>>;
type VExpansionPanels = InstanceType<typeof VExpansionPanels>;

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
interface GroupItemProvide {
    id: number;
    isSelected: Ref<boolean>;
    isFirst: Ref<boolean>;
    isLast: Ref<boolean>;
    toggle: () => void;
    select: (value: boolean) => void;
    selectedClass: Ref<(string | undefined)[] | false>;
    value: Ref<unknown>;
    disabled: Ref<boolean | undefined>;
    group: GroupProvide;
}

declare const VExpansionPanel: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        readonly: boolean;
        tag: string;
        static: boolean;
        focusable: boolean;
        tile: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        value?: any;
        title?: string | undefined;
        text?: string | undefined;
        class?: any;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        selectedClass?: string | undefined;
        bgColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            text?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            text?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:text"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onGroup:selected"?: ((val: {
            value: boolean;
        }) => any) | undefined;
    }, {
        groupItem: GroupItemProvide;
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'group:selected': (val: {
            value: boolean;
        }) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        readonly: boolean;
        tag: string;
        static: boolean;
        focusable: boolean;
        tile: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        value?: any;
        title?: string | undefined;
        text?: string | undefined;
        class?: any;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        selectedClass?: string | undefined;
        bgColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            text?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            text?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:text"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onGroup:selected"?: ((val: {
            value: boolean;
        }) => any) | undefined;
    }, {
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        readonly: boolean;
        tag: string;
        static: boolean;
        focusable: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        title: () => vue.VNode[];
        text: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        readonly: boolean;
        tag: string;
        static: boolean;
        focusable: boolean;
        tile: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        value?: any;
        title?: string | undefined;
        text?: string | undefined;
        class?: any;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        selectedClass?: string | undefined;
        bgColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            text?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            text?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:text"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onGroup:selected"?: ((val: {
            value: boolean;
        }) => any) | undefined;
    }, {
        groupItem: GroupItemProvide;
    }, {}, {}, {}, {
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        readonly: boolean;
        tag: string;
        static: boolean;
        focusable: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    eager: boolean;
    disabled: boolean;
    readonly: boolean;
    tag: string;
    static: boolean;
    focusable: boolean;
    tile: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    hideActions: boolean;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    value?: any;
    title?: string | undefined;
    text?: string | undefined;
    class?: any;
    elevation?: string | number | undefined;
    rounded?: string | number | boolean | undefined;
    selectedClass?: string | undefined;
    bgColor?: string | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        title?: (() => vue.VNodeChild) | undefined;
        text?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        title?: false | (() => vue.VNodeChild) | undefined;
        text?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:text"?: false | (() => vue.VNodeChild) | undefined;
} & {
    "onGroup:selected"?: ((val: {
        value: boolean;
    }) => any) | undefined;
}, {
    groupItem: GroupItemProvide;
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'group:selected': (val: {
        value: boolean;
    }) => true;
}, string, {
    style: vue.StyleValue;
    eager: boolean;
    disabled: boolean;
    readonly: boolean;
    tag: string;
    static: boolean;
    focusable: boolean;
    rounded: string | number | boolean;
    tile: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    hideActions: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    title: () => vue.VNode[];
    text: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    eager: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    color: StringConstructor;
    expandIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    collapseIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    hideActions: BooleanConstructor;
    focusable: BooleanConstructor;
    static: BooleanConstructor;
    ripple: {
        type: vue.PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    readonly: BooleanConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    value: null;
    disabled: BooleanConstructor;
    selectedClass: StringConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    title: StringConstructor;
    text: StringConstructor;
    bgColor: StringConstructor;
}, vue.ExtractPropTypes<{
    eager: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    color: StringConstructor;
    expandIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    collapseIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    hideActions: BooleanConstructor;
    focusable: BooleanConstructor;
    static: BooleanConstructor;
    ripple: {
        type: vue.PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    readonly: BooleanConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    value: null;
    disabled: BooleanConstructor;
    selectedClass: StringConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    title: StringConstructor;
    text: StringConstructor;
    bgColor: StringConstructor;
}>>;
type VExpansionPanel = InstanceType<typeof VExpansionPanel>;

declare const VExpansionPanelText: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        eager: boolean;
    } & {
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        eager: boolean;
    } & {
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        style: vue.StyleValue;
        eager: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        style: vue.StyleValue;
        eager: boolean;
    } & {
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        style: vue.StyleValue;
        eager: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    eager: boolean;
} & {
    class?: any;
} & {
    $children?: vue.VNodeChild | {
        default?: (() => vue.VNodeChild) | undefined;
    } | (() => vue.VNodeChild);
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    style: vue.StyleValue;
    eager: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    eager: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
}, vue.ExtractPropTypes<{
    eager: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
}>>;
type VExpansionPanelText = InstanceType<typeof VExpansionPanelText>;

interface ExpansionPanelTitleSlot {
    collapseIcon: IconValue;
    disabled: boolean | undefined;
    expanded: boolean;
    expandIcon: IconValue;
    readonly: boolean;
}
declare const VExpansionPanelTitle: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        readonly: boolean;
        static: boolean;
        focusable: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
            actions?: ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
        } | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
            actions?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:actions"?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        readonly: boolean;
        static: boolean;
        focusable: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
            actions?: ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
        } | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
            actions?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:actions"?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
    }, {
        style: vue.StyleValue;
        readonly: boolean;
        static: boolean;
        focusable: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: ExpansionPanelTitleSlot) => vue.VNode[];
        actions: (arg: ExpansionPanelTitleSlot) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        style: vue.StyleValue;
        readonly: boolean;
        static: boolean;
        focusable: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
            actions?: ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
        } | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
            actions?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:actions"?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        style: vue.StyleValue;
        readonly: boolean;
        static: boolean;
        focusable: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        hideActions: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    readonly: boolean;
    static: boolean;
    focusable: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    hideActions: boolean;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    class?: any;
} & {
    $children?: vue.VNodeChild | {
        default?: ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
        actions?: ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
    } | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild);
    'v-slots'?: {
        default?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
        actions?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
    "v-slot:actions"?: false | ((arg: ExpansionPanelTitleSlot) => vue.VNodeChild) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    style: vue.StyleValue;
    readonly: boolean;
    static: boolean;
    focusable: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    hideActions: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: ExpansionPanelTitleSlot) => vue.VNode[];
    actions: (arg: ExpansionPanelTitleSlot) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
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
    color: StringConstructor;
    expandIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    collapseIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    hideActions: BooleanConstructor;
    focusable: BooleanConstructor;
    static: BooleanConstructor;
    ripple: {
        type: PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    readonly: BooleanConstructor;
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
    color: StringConstructor;
    expandIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    collapseIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    hideActions: BooleanConstructor;
    focusable: BooleanConstructor;
    static: BooleanConstructor;
    ripple: {
        type: PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    readonly: BooleanConstructor;
}>>;
type VExpansionPanelTitle = InstanceType<typeof VExpansionPanelTitle>;

export { VExpansionPanel, VExpansionPanelText, VExpansionPanelTitle, VExpansionPanels };
