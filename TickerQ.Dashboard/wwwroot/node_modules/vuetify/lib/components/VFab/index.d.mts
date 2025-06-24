import * as vue from 'vue';
import { DirectiveBinding, ComponentPropsOptions, ExtractPropTypes, PropType, ComponentPublicInstance, FunctionalComponent, ComponentInternalInstance, Ref, ComputedRef } from 'vue';
// @ts-ignore
import * as vue_router from 'vue-router';

type ClassValue = any;

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

type Density = null | 'default' | 'comfortable' | 'compact';

declare const block: readonly ["top", "bottom"];
declare const inline: readonly ["start", "end", "left", "right"];
type Tblock = typeof block[number];
type Tinline = typeof inline[number];
type Anchor = Tblock | Tinline | 'center' | 'center center' | `${Tblock} ${Tinline | 'center'}` | `${Tinline} ${Tblock | 'center'}`;

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

declare const allowedVariants: readonly ["elevated", "flat", "tonal", "outlined", "text", "plain"];
type Variant = typeof allowedVariants[number];

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

declare const VFab: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        offset: boolean;
        exact: boolean;
        absolute: boolean;
        block: boolean;
        active: boolean;
        order: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        layout: boolean;
        appear: boolean;
        app: boolean;
        modelValue: boolean;
        tile: boolean;
        density: Density;
        extended: boolean;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        name?: string | undefined;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        value?: any;
        loading?: string | boolean | undefined;
        text?: string | undefined;
        class?: any;
        theme?: string | undefined;
        to?: vue_router.RouteLocationRaw | undefined;
        icon?: boolean | IconValue | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        selectedClass?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (value: boolean) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        offset: boolean;
        exact: boolean;
        absolute: boolean;
        block: boolean;
        active: boolean;
        order: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        layout: boolean;
        appear: boolean;
        app: boolean;
        modelValue: boolean;
        tile: boolean;
        density: Density;
        extended: boolean;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        name?: string | undefined;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        value?: any;
        loading?: string | boolean | undefined;
        text?: string | undefined;
        class?: any;
        theme?: string | undefined;
        to?: vue_router.RouteLocationRaw | undefined;
        icon?: boolean | IconValue | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        selectedClass?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
    }, {
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        offset: boolean;
        exact: boolean;
        absolute: boolean;
        block: boolean;
        active: boolean;
        order: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        layout: boolean;
        appear: boolean;
        app: boolean;
        modelValue: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        extended: boolean;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
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
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        offset: boolean;
        exact: boolean;
        absolute: boolean;
        block: boolean;
        active: boolean;
        order: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        layout: boolean;
        appear: boolean;
        app: boolean;
        modelValue: boolean;
        tile: boolean;
        density: Density;
        extended: boolean;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        name?: string | undefined;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        value?: any;
        loading?: string | boolean | undefined;
        text?: string | undefined;
        class?: any;
        theme?: string | undefined;
        to?: vue_router.RouteLocationRaw | undefined;
        icon?: boolean | IconValue | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        selectedClass?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
    }, {}, {}, {}, {}, {
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        offset: boolean;
        exact: boolean;
        absolute: boolean;
        block: boolean;
        active: boolean;
        order: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        layout: boolean;
        appear: boolean;
        app: boolean;
        modelValue: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        extended: boolean;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    symbol: any;
    replace: boolean;
    flat: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    offset: boolean;
    exact: boolean;
    absolute: boolean;
    block: boolean;
    active: boolean;
    order: string | number;
    transition: string | boolean | (vue.TransitionProps & {
        component?: vue.Component;
    });
    style: vue.StyleValue;
    disabled: boolean;
    size: string | number;
    readonly: boolean;
    tag: string;
    layout: boolean;
    appear: boolean;
    app: boolean;
    modelValue: boolean;
    tile: boolean;
    density: Density;
    extended: boolean;
    slim: boolean;
    stacked: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
} & {
    name?: string | undefined;
    location?: Anchor | null | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    border?: string | number | boolean | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
    value?: any;
    loading?: string | boolean | undefined;
    text?: string | undefined;
    class?: any;
    theme?: string | undefined;
    to?: vue_router.RouteLocationRaw | undefined;
    icon?: boolean | IconValue | undefined;
    href?: string | undefined;
    elevation?: string | number | undefined;
    rounded?: string | number | boolean | undefined;
    baseColor?: string | undefined;
    selectedClass?: string | undefined;
    activeColor?: string | undefined;
    prependIcon?: IconValue | undefined;
    appendIcon?: IconValue | undefined;
} & {
    $children?: vue.VNodeChild | {
        default?: (() => vue.VNodeChild) | undefined;
    } | (() => vue.VNodeChild);
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (value: boolean) => true;
}, string, {
    symbol: any;
    replace: boolean;
    flat: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    offset: boolean;
    exact: boolean;
    absolute: boolean;
    block: boolean;
    active: boolean;
    order: string | number;
    transition: string | boolean | (vue.TransitionProps & {
        component?: vue.Component;
    });
    style: vue.StyleValue;
    disabled: boolean;
    size: string | number;
    readonly: boolean;
    tag: string;
    layout: boolean;
    appear: boolean;
    app: boolean;
    modelValue: boolean;
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    extended: boolean;
    slim: boolean;
    stacked: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    transition: Omit<{
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
    };
    location: vue.PropType<Anchor | null>;
    name: {
        type: StringConstructor;
    };
    order: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    absolute: BooleanConstructor;
    symbol: {
        type: null;
        default: vue.InjectionKey<GroupProvide>;
    };
    replace: BooleanConstructor;
    flat: BooleanConstructor;
    variant: Omit<{
        type: vue.PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    exact: BooleanConstructor;
    block: BooleanConstructor;
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    active: Omit<{
        type: BooleanConstructor;
        default: undefined;
    }, "type" | "default"> & {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    position: {
        type: vue.PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    value: null;
    loading: (StringConstructor | BooleanConstructor)[];
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    text: StringConstructor;
    disabled: BooleanConstructor;
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
    readonly: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    theme: StringConstructor;
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<string>;
        default: string;
    };
    to: vue.PropType<vue_router.RouteLocationRaw>;
    icon: vue.PropType<boolean | IconValue>;
    href: StringConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    density: {
        type: vue.PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    baseColor: StringConstructor;
    selectedClass: StringConstructor;
    activeColor: StringConstructor;
    prependIcon: vue.PropType<IconValue>;
    appendIcon: vue.PropType<IconValue>;
    slim: BooleanConstructor;
    stacked: BooleanConstructor;
    ripple: {
        type: vue.PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    app: BooleanConstructor;
    appear: BooleanConstructor;
    extended: BooleanConstructor;
    layout: BooleanConstructor;
    offset: BooleanConstructor;
    modelValue: {
        type: BooleanConstructor;
        default: boolean;
    };
}, vue.ExtractPropTypes<{
    transition: Omit<{
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
    };
    location: vue.PropType<Anchor | null>;
    name: {
        type: StringConstructor;
    };
    order: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    absolute: BooleanConstructor;
    symbol: {
        type: null;
        default: vue.InjectionKey<GroupProvide>;
    };
    replace: BooleanConstructor;
    flat: BooleanConstructor;
    variant: Omit<{
        type: vue.PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    exact: BooleanConstructor;
    block: BooleanConstructor;
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    active: Omit<{
        type: BooleanConstructor;
        default: undefined;
    }, "type" | "default"> & {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    position: {
        type: vue.PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    value: null;
    loading: (StringConstructor | BooleanConstructor)[];
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    text: StringConstructor;
    disabled: BooleanConstructor;
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
    readonly: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    theme: StringConstructor;
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<string>;
        default: string;
    };
    to: vue.PropType<vue_router.RouteLocationRaw>;
    icon: vue.PropType<boolean | IconValue>;
    href: StringConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    density: {
        type: vue.PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    baseColor: StringConstructor;
    selectedClass: StringConstructor;
    activeColor: StringConstructor;
    prependIcon: vue.PropType<IconValue>;
    appendIcon: vue.PropType<IconValue>;
    slim: BooleanConstructor;
    stacked: BooleanConstructor;
    ripple: {
        type: vue.PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    app: BooleanConstructor;
    appear: BooleanConstructor;
    extended: BooleanConstructor;
    layout: BooleanConstructor;
    offset: BooleanConstructor;
    modelValue: {
        type: BooleanConstructor;
        default: boolean;
    };
}>>;
type VFab = InstanceType<typeof VFab>;

export { VFab };
