import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, VNodeChild, VNode, PropType, ComponentPublicInstance, FunctionalComponent, DirectiveBinding } from 'vue';
// @ts-ignore
import * as vue_router from 'vue-router';

type ClassValue = any;

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

declare function deepEqual(a: any, b: any): boolean;
type SelectItemKey<T = Record<string, any>> = boolean | null | undefined | string | readonly (string | number)[] | ((item: T, fallback?: any) => any);

type Density = null | 'default' | 'comfortable' | 'compact';

declare const allowedVariants: readonly ["elevated", "flat", "tonal", "outlined", "text", "plain"];
type Variant = typeof allowedVariants[number];

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

type ListItemSlot = {
    isActive: boolean;
    isOpen: boolean;
    isSelected: boolean;
    isIndeterminate: boolean;
    select: (value: boolean) => void;
};
type ListItemTitleSlot = {
    title?: string | number;
};
type ListItemSubtitleSlot = {
    subtitle?: string | number;
};
type VListItemSlots = {
    prepend: ListItemSlot;
    append: ListItemSlot;
    default: ListItemSlot;
    title: ListItemTitleSlot;
    subtitle: ListItemSubtitleSlot;
};
declare const VListItem: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        replace: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        link?: boolean | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        value?: any;
        title?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
        to?: vue_router.RouteLocationRaw | undefined;
        lines?: false | "one" | "two" | "three" | undefined;
        onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
        onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        appendAvatar?: string | undefined;
        prependAvatar?: string | undefined;
        subtitle?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | {
            prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | ((arg: ListItemSlot) => vue.VNodeChild);
        'v-slots'?: {
            prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    } & {
        onClick?: ((e: MouseEvent | KeyboardEvent) => any) | undefined;
    }, {
        activate: (activated: boolean, e?: Event) => void;
        isActivated: vue.ComputedRef<boolean>;
        isGroupActivator: boolean | undefined;
        isSelected: vue.ComputedRef<boolean>;
        list: {
            hasPrepend: vue.Ref<boolean>;
            updateHasPrepend: (value: boolean) => void;
        } | null;
        select: (selected: boolean, e?: Event) => void;
        root: {
            children: vue.Ref<Map<unknown, unknown[]>>;
            parents: vue.Ref<Map<unknown, unknown>>;
            activatable: vue.Ref<boolean>;
            selectable: vue.Ref<boolean>;
            opened: vue.Ref<Set<unknown>>;
            activated: vue.Ref<Set<unknown>>;
            selected: vue.Ref<Map<unknown, "on" | "off" | "indeterminate">>;
            selectedValues: vue.Ref<unknown[]>;
            register: (id: unknown, parentId: unknown, isGroup?: boolean) => void;
            unregister: (id: unknown) => void;
            open: (id: unknown, value: boolean, event?: Event) => void;
            activate: (id: unknown, value: boolean, event?: Event) => void;
            select: (id: unknown, value: boolean, event?: Event) => void;
            openOnSelect: (id: unknown, value: boolean, event?: Event) => void;
            getPath: (id: unknown) => unknown[];
        };
        id: vue.ComputedRef<{} | null>;
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        click: (e: MouseEvent | KeyboardEvent) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        replace: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        link?: boolean | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        value?: any;
        title?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
        to?: vue_router.RouteLocationRaw | undefined;
        lines?: false | "one" | "two" | "three" | undefined;
        onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
        onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        appendAvatar?: string | undefined;
        prependAvatar?: string | undefined;
        subtitle?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | {
            prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | ((arg: ListItemSlot) => vue.VNodeChild);
        'v-slots'?: {
            prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    } & {
        onClick?: ((e: MouseEvent | KeyboardEvent) => any) | undefined;
    }, {
        replace: boolean;
        link: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        active: boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    }, true, {}, vue.SlotsType<Partial<{
        prepend: (arg: ListItemSlot) => vue.VNode[];
        append: (arg: ListItemSlot) => vue.VNode[];
        default: (arg: ListItemSlot) => vue.VNode[];
        title: (arg: ListItemTitleSlot) => vue.VNode[];
        subtitle: (arg: ListItemSubtitleSlot) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        replace: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        link?: boolean | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        value?: any;
        title?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
        to?: vue_router.RouteLocationRaw | undefined;
        lines?: false | "one" | "two" | "three" | undefined;
        onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
        onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        appendAvatar?: string | undefined;
        prependAvatar?: string | undefined;
        subtitle?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | {
            prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | ((arg: ListItemSlot) => vue.VNodeChild);
        'v-slots'?: {
            prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    } & {
        onClick?: ((e: MouseEvent | KeyboardEvent) => any) | undefined;
    }, {
        activate: (activated: boolean, e?: Event) => void;
        isActivated: vue.ComputedRef<boolean>;
        isGroupActivator: boolean | undefined;
        isSelected: vue.ComputedRef<boolean>;
        list: {
            hasPrepend: vue.Ref<boolean>;
            updateHasPrepend: (value: boolean) => void;
        } | null;
        select: (selected: boolean, e?: Event) => void;
        root: {
            children: vue.Ref<Map<unknown, unknown[]>>;
            parents: vue.Ref<Map<unknown, unknown>>;
            activatable: vue.Ref<boolean>;
            selectable: vue.Ref<boolean>;
            opened: vue.Ref<Set<unknown>>;
            activated: vue.Ref<Set<unknown>>;
            selected: vue.Ref<Map<unknown, "on" | "off" | "indeterminate">>;
            selectedValues: vue.Ref<unknown[]>;
            register: (id: unknown, parentId: unknown, isGroup?: boolean) => void;
            unregister: (id: unknown) => void;
            open: (id: unknown, value: boolean, event?: Event) => void;
            activate: (id: unknown, value: boolean, event?: Event) => void;
            select: (id: unknown, value: boolean, event?: Event) => void;
            openOnSelect: (id: unknown, value: boolean, event?: Event) => void;
            getPath: (id: unknown) => unknown[];
        };
        id: vue.ComputedRef<{} | null>;
    }, {}, {}, {}, {
        replace: boolean;
        link: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        active: boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    replace: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    exact: boolean;
    nav: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    tile: boolean;
    density: Density;
    slim: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
} & {
    link?: boolean | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    active?: boolean | undefined;
    border?: string | number | boolean | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    value?: any;
    title?: string | number | undefined;
    class?: any;
    theme?: string | undefined;
    to?: vue_router.RouteLocationRaw | undefined;
    lines?: false | "one" | "two" | "three" | undefined;
    onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
    onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
    href?: string | undefined;
    elevation?: string | number | undefined;
    rounded?: string | number | boolean | undefined;
    baseColor?: string | undefined;
    activeColor?: string | undefined;
    prependIcon?: IconValue | undefined;
    appendIcon?: IconValue | undefined;
    activeClass?: string | undefined;
    appendAvatar?: string | undefined;
    prependAvatar?: string | undefined;
    subtitle?: string | number | undefined;
} & {
    $children?: vue.VNodeChild | {
        prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    } | ((arg: ListItemSlot) => vue.VNodeChild);
    'v-slots'?: {
        prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
    "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
    "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
    "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
    "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
} & {
    onClick?: ((e: MouseEvent | KeyboardEvent) => any) | undefined;
}, {
    activate: (activated: boolean, e?: Event) => void;
    isActivated: vue.ComputedRef<boolean>;
    isGroupActivator: boolean | undefined;
    isSelected: vue.ComputedRef<boolean>;
    list: {
        hasPrepend: vue.Ref<boolean>;
        updateHasPrepend: (value: boolean) => void;
    } | null;
    select: (selected: boolean, e?: Event) => void;
    root: {
        children: vue.Ref<Map<unknown, unknown[]>>;
        parents: vue.Ref<Map<unknown, unknown>>;
        activatable: vue.Ref<boolean>;
        selectable: vue.Ref<boolean>;
        opened: vue.Ref<Set<unknown>>;
        activated: vue.Ref<Set<unknown>>;
        selected: vue.Ref<Map<unknown, "on" | "off" | "indeterminate">>;
        selectedValues: vue.Ref<unknown[]>;
        register: (id: unknown, parentId: unknown, isGroup?: boolean) => void;
        unregister: (id: unknown) => void;
        open: (id: unknown, value: boolean, event?: Event) => void;
        activate: (id: unknown, value: boolean, event?: Event) => void;
        select: (id: unknown, value: boolean, event?: Event) => void;
        openOnSelect: (id: unknown, value: boolean, event?: Event) => void;
        getPath: (id: unknown) => unknown[];
    };
    id: vue.ComputedRef<{} | null>;
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    click: (e: MouseEvent | KeyboardEvent) => true;
}, string, {
    replace: boolean;
    link: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    exact: boolean;
    active: boolean;
    nav: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    slim: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
}, {}, string, vue.SlotsType<Partial<{
    prepend: (arg: ListItemSlot) => vue.VNode[];
    append: (arg: ListItemSlot) => vue.VNode[];
    default: (arg: ListItemSlot) => vue.VNode[];
    title: (arg: ListItemTitleSlot) => vue.VNode[];
    subtitle: (arg: ListItemSubtitleSlot) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
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
    href: StringConstructor;
    replace: BooleanConstructor;
    to: PropType<vue_router.RouteLocationRaw>;
    exact: BooleanConstructor;
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    active: {
        type: BooleanConstructor;
        default: undefined;
    };
    activeClass: StringConstructor;
    activeColor: StringConstructor;
    appendAvatar: StringConstructor;
    appendIcon: PropType<IconValue>;
    baseColor: StringConstructor;
    disabled: BooleanConstructor;
    lines: PropType<"one" | "two" | "three" | false>;
    link: {
        type: BooleanConstructor;
        default: undefined;
    };
    nav: BooleanConstructor;
    prependAvatar: StringConstructor;
    prependIcon: PropType<IconValue>;
    ripple: {
        type: PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    slim: BooleanConstructor;
    subtitle: (StringConstructor | NumberConstructor)[];
    title: (StringConstructor | NumberConstructor)[];
    value: null;
    onClick: PropType<(args_0: MouseEvent | KeyboardEvent) => void>;
    onClickOnce: PropType<(args_0: MouseEvent) => void>;
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
    href: StringConstructor;
    replace: BooleanConstructor;
    to: PropType<vue_router.RouteLocationRaw>;
    exact: BooleanConstructor;
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    active: {
        type: BooleanConstructor;
        default: undefined;
    };
    activeClass: StringConstructor;
    activeColor: StringConstructor;
    appendAvatar: StringConstructor;
    appendIcon: PropType<IconValue>;
    baseColor: StringConstructor;
    disabled: BooleanConstructor;
    lines: PropType<"one" | "two" | "three" | false>;
    link: {
        type: BooleanConstructor;
        default: undefined;
    };
    nav: BooleanConstructor;
    prependAvatar: StringConstructor;
    prependIcon: PropType<IconValue>;
    ripple: {
        type: PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    slim: BooleanConstructor;
    subtitle: (StringConstructor | NumberConstructor)[];
    title: (StringConstructor | NumberConstructor)[];
    value: null;
    onClick: PropType<(args_0: MouseEvent | KeyboardEvent) => void>;
    onClickOnce: PropType<(args_0: MouseEvent) => void>;
}>>;
type VListItem = InstanceType<typeof VListItem>;

type ActiveStrategyFn = (data: {
    id: unknown;
    value: boolean;
    activated: Set<unknown>;
    children: Map<unknown, unknown[]>;
    parents: Map<unknown, unknown>;
    event?: Event;
}) => Set<unknown>;
type ActiveStrategyTransformInFn = (v: unknown | undefined, children: Map<unknown, unknown[]>, parents: Map<unknown, unknown>) => Set<unknown>;
type ActiveStrategyTransformOutFn = (v: Set<unknown>, children: Map<unknown, unknown[]>, parents: Map<unknown, unknown>) => unknown;
type ActiveStrategy = {
    activate: ActiveStrategyFn;
    in: ActiveStrategyTransformInFn;
    out: ActiveStrategyTransformOutFn;
};

type OpenStrategyFn = (data: {
    id: unknown;
    value: boolean;
    opened: Set<unknown>;
    children: Map<unknown, unknown[]>;
    parents: Map<unknown, unknown>;
    event?: Event;
}) => Set<unknown>;
type OpenSelectStrategyFn = (data: {
    id: unknown;
    value: boolean;
    opened: Set<unknown>;
    selected: Map<unknown, 'on' | 'off' | 'indeterminate'>;
    children: Map<unknown, unknown[]>;
    parents: Map<unknown, unknown>;
    event?: Event;
}) => Set<unknown> | null;
type OpenStrategy = {
    open: OpenStrategyFn;
    select: OpenSelectStrategyFn;
};

type SelectStrategyFn = (data: {
    id: unknown;
    value: boolean;
    selected: Map<unknown, 'on' | 'off' | 'indeterminate'>;
    children: Map<unknown, unknown[]>;
    parents: Map<unknown, unknown>;
    event?: Event;
}) => Map<unknown, 'on' | 'off' | 'indeterminate'>;
type SelectStrategyTransformInFn = (v: readonly unknown[] | undefined, children: Map<unknown, unknown[]>, parents: Map<unknown, unknown>) => Map<unknown, 'on' | 'off' | 'indeterminate'>;
type SelectStrategyTransformOutFn = (v: Map<unknown, 'on' | 'off' | 'indeterminate'>, children: Map<unknown, unknown[]>, parents: Map<unknown, unknown>) => unknown[];
type SelectStrategy = {
    select: SelectStrategyFn;
    in: SelectStrategyTransformInFn;
    out: SelectStrategyTransformOutFn;
};

type ActiveStrategyProp = 'single-leaf' | 'leaf' | 'independent' | 'single-independent' | ActiveStrategy | ((mandatory: boolean) => ActiveStrategy);
type SelectStrategyProp = 'single-leaf' | 'leaf' | 'independent' | 'single-independent' | 'classic' | SelectStrategy | ((mandatory: boolean) => SelectStrategy);
type OpenStrategyProp = 'single' | 'multiple' | 'list' | OpenStrategy;

type VListChildrenSlots<T> = {
    [K in keyof Omit<VListItemSlots, 'default'>]: VListItemSlots[K] & {
        item: T;
    };
} & {
    default: never;
    item: {
        props: InternalListItem['props'];
    };
    divider: {
        props: InternalListItem['props'];
    };
    subheader: {
        props: InternalListItem['props'];
    };
    header: {
        props: InternalListItem['props'];
    };
};

interface InternalItem<T = any> {
    value: any;
    raw: T;
}

interface ListItem<T = any> extends InternalItem<T> {
    title: string;
    props: {
        [key: string]: any;
        title: string;
        value: any;
    };
    children?: ListItem<T>[];
}
interface ItemProps {
    items: any[];
    itemTitle: SelectItemKey;
    itemValue: SelectItemKey;
    itemChildren: SelectItemKey;
    itemProps: SelectItemKey;
    returnObject: boolean;
    valueComparator: typeof deepEqual;
}

interface InternalListItem<T = any> extends ListItem<T> {
    type?: 'item' | 'subheader' | 'divider';
}
type ItemType<T> = T extends readonly (infer U)[] ? U : never;
declare const VList: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        activated?: any;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
        activeStrategy?: ActiveStrategyProp | undefined;
        collapseIcon?: IconValue | undefined;
        expandIcon?: IconValue | undefined;
    } & {
        "onUpdate:activated"?: ((value: unknown) => any) | undefined;
        "onClick:activate"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
    }, {
        open: (id: unknown, value: boolean, event?: Event) => void;
        select: (id: unknown, value: boolean, event?: Event) => void;
        focus: (location?: "next" | "prev" | "first" | "last") => void;
        children: vue.Ref<Map<unknown, unknown[]>>;
        parents: vue.Ref<Map<unknown, unknown>>;
        getPath: (id: unknown) => unknown[];
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
        'update:selected': (value: unknown) => true;
        'update:activated': (value: unknown) => true;
        'update:opened': (value: unknown) => true;
        'click:open': (value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => true;
        'click:activate': (value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => true;
        'click:select': (value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => true;
    }, "$children" | "selected" | "v-slots" | "v-slot:default" | "items" | "v-slot:prepend" | "v-slot:append" | "v-slot:title" | "opened" | "click:open" | "click:select" | "v-slot:subtitle" | "v-slot:item" | "v-slot:header" | "v-slot:divider" | "v-slot:subheader" | "itemTitle" | "itemValue" | "itemChildren" | "itemProps" | "update:selected" | "update:opened">, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        activated?: any;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
        activeStrategy?: ActiveStrategyProp | undefined;
        collapseIcon?: IconValue | undefined;
        expandIcon?: IconValue | undefined;
    } & {
        "onUpdate:activated"?: ((value: unknown) => any) | undefined;
        "onClick:activate"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
    }, {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    }, true, {}, vue.SlotsType<Partial<{
        title: (arg: ListItemTitleSlot & {
            item: any;
        }) => vue.VNode[];
        append: (arg: ListItemSlot & {
            item: any;
        }) => vue.VNode[];
        prepend: (arg: ListItemSlot & {
            item: any;
        }) => vue.VNode[];
        subtitle: (arg: ListItemSubtitleSlot & {
            item: any;
        }) => vue.VNode[];
        default: () => vue.VNode[];
        item: (arg: {
            props: InternalListItem["props"];
        }) => vue.VNode[];
        divider: (arg: {
            props: InternalListItem["props"];
        }) => vue.VNode[];
        subheader: (arg: {
            props: InternalListItem["props"];
        }) => vue.VNode[];
        header: (arg: {
            props: InternalListItem["props"];
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        activated?: any;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
        activeStrategy?: ActiveStrategyProp | undefined;
        collapseIcon?: IconValue | undefined;
        expandIcon?: IconValue | undefined;
    } & {
        "onUpdate:activated"?: ((value: unknown) => any) | undefined;
        "onClick:activate"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
    }, {
        open: (id: unknown, value: boolean, event?: Event) => void;
        select: (id: unknown, value: boolean, event?: Event) => void;
        focus: (location?: "next" | "prev" | "first" | "last") => void;
        children: vue.Ref<Map<unknown, unknown[]>>;
        parents: vue.Ref<Map<unknown, unknown>>;
        getPath: (id: unknown) => unknown[];
    }, {}, {}, {}, {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    nav: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    lines: false | "one" | "two" | "three";
    mandatory: boolean;
    tile: boolean;
    density: Density;
    slim: boolean;
    valueComparator: typeof deepEqual;
    activatable: boolean;
    selectable: boolean;
    selectStrategy: SelectStrategyProp;
    openStrategy: OpenStrategyProp;
    returnObject: boolean;
    itemType: string;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    border?: string | number | boolean | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    activated?: any;
    class?: any;
    theme?: string | undefined;
    elevation?: string | number | undefined;
    rounded?: string | number | boolean | undefined;
    baseColor?: string | undefined;
    bgColor?: string | undefined;
    activeColor?: string | undefined;
    activeClass?: string | undefined;
    activeStrategy?: ActiveStrategyProp | undefined;
    collapseIcon?: IconValue | undefined;
    expandIcon?: IconValue | undefined;
} & {
    "onUpdate:activated"?: ((value: unknown) => any) | undefined;
    "onClick:activate"?: ((value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => any) | undefined;
}, {
    open: (id: unknown, value: boolean, event?: Event) => void;
    select: (id: unknown, value: boolean, event?: Event) => void;
    focus: (location?: "next" | "prev" | "first" | "last") => void;
    children: vue.Ref<Map<unknown, unknown[]>>;
    parents: vue.Ref<Map<unknown, unknown>>;
    getPath: (id: unknown) => unknown[];
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
    'update:selected': (value: unknown) => true;
    'update:activated': (value: unknown) => true;
    'update:opened': (value: unknown) => true;
    'click:open': (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => true;
    'click:activate': (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => true;
    'click:select': (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => true;
}, "$children" | "selected" | "v-slots" | "v-slot:default" | "items" | "v-slot:prepend" | "v-slot:append" | "v-slot:title" | "opened" | "click:open" | "click:select" | "v-slot:subtitle" | "v-slot:item" | "v-slot:header" | "v-slot:divider" | "v-slot:subheader" | "itemTitle" | "itemValue" | "itemChildren" | "itemProps" | "update:selected" | "update:opened">, string, {
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    nav: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    lines: false | "one" | "two" | "three";
    mandatory: boolean;
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    slim: boolean;
    valueComparator: typeof deepEqual;
    activatable: boolean;
    selectable: boolean;
    selectStrategy: SelectStrategyProp;
    openStrategy: OpenStrategyProp;
    returnObject: boolean;
    itemType: string;
}, {}, string, vue.SlotsType<Partial<{
    title: (arg: ListItemTitleSlot & {
        item: any;
    }) => vue.VNode[];
    append: (arg: ListItemSlot & {
        item: any;
    }) => vue.VNode[];
    prepend: (arg: ListItemSlot & {
        item: any;
    }) => vue.VNode[];
    subtitle: (arg: ListItemSubtitleSlot & {
        item: any;
    }) => vue.VNode[];
    default: () => vue.VNode[];
    item: (arg: {
        props: InternalListItem["props"];
    }) => vue.VNode[];
    divider: (arg: {
        props: InternalListItem["props"];
    }) => vue.VNode[];
    subheader: (arg: {
        props: InternalListItem["props"];
    }) => vue.VNode[];
    header: (arg: {
        props: InternalListItem["props"];
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & (new <T extends readonly any[], S = unknown, O = unknown>(props: {
    items?: T;
    itemTitle?: SelectItemKey<ItemType<T>>;
    itemValue?: SelectItemKey<ItemType<T>>;
    itemChildren?: SelectItemKey<ItemType<T>>;
    itemProps?: SelectItemKey<ItemType<T>>;
    selected?: S;
    "onUpdate:selected"?: (value: S) => void;
    "onClick:open"?: (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void;
    "onClick:select"?: (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void;
    opened?: O;
    "onUpdate:opened"?: (value: O) => void;
}, slots: VListChildrenSlots<ItemType<T>>) => GenericProps<typeof props, typeof slots>) & FilterPropsOptions<{
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
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    items: {
        type: PropType<ItemProps["items"]>;
        default: () => never[];
    };
    itemTitle: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemValue: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemChildren: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemProps: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    returnObject: BooleanConstructor;
    valueComparator: {
        type: PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
    itemType: {
        type: StringConstructor;
        default: string;
    };
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    activatable: BooleanConstructor;
    selectable: BooleanConstructor;
    activeStrategy: PropType<ActiveStrategyProp>;
    selectStrategy: {
        type: PropType<SelectStrategyProp>;
        default: NonNullable<SelectStrategyProp>;
    };
    openStrategy: {
        type: PropType<OpenStrategyProp>;
        default: NonNullable<OpenStrategyProp>;
    };
    opened: null;
    activated: null;
    selected: null;
    mandatory: BooleanConstructor;
    baseColor: StringConstructor;
    activeColor: StringConstructor;
    activeClass: StringConstructor;
    bgColor: StringConstructor;
    disabled: BooleanConstructor;
    expandIcon: PropType<IconValue>;
    collapseIcon: PropType<IconValue>;
    lines: {
        type: PropType<"one" | "two" | "three" | false>;
        default: string;
    };
    slim: BooleanConstructor;
    nav: BooleanConstructor;
    'onClick:open': PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    'onClick:select': PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    'onUpdate:opened': PropType<() => void>;
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
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    items: {
        type: PropType<ItemProps["items"]>;
        default: () => never[];
    };
    itemTitle: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemValue: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemChildren: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemProps: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    returnObject: BooleanConstructor;
    valueComparator: {
        type: PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
    itemType: {
        type: StringConstructor;
        default: string;
    };
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    activatable: BooleanConstructor;
    selectable: BooleanConstructor;
    activeStrategy: PropType<ActiveStrategyProp>;
    selectStrategy: {
        type: PropType<SelectStrategyProp>;
        default: NonNullable<SelectStrategyProp>;
    };
    openStrategy: {
        type: PropType<OpenStrategyProp>;
        default: NonNullable<OpenStrategyProp>;
    };
    opened: null;
    activated: null;
    selected: null;
    mandatory: BooleanConstructor;
    baseColor: StringConstructor;
    activeColor: StringConstructor;
    activeClass: StringConstructor;
    bgColor: StringConstructor;
    disabled: BooleanConstructor;
    expandIcon: PropType<IconValue>;
    collapseIcon: PropType<IconValue>;
    lines: {
        type: PropType<"one" | "two" | "three" | false>;
        default: string;
    };
    slim: BooleanConstructor;
    nav: BooleanConstructor;
    'onClick:open': PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    'onClick:select': PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    'onUpdate:opened': PropType<() => void>;
}>>;
type VList = InstanceType<typeof VList>;

declare const VListGroup: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        tag: string;
        subgroup: boolean;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        fluid: boolean;
    } & {
        color?: string | undefined;
        value?: any;
        title?: string | undefined;
        class?: any;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            activator?: ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            activator?: false | ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:activator"?: false | ((arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    }, {
        isOpen: vue.ComputedRef<boolean>;
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        tag: string;
        subgroup: boolean;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        fluid: boolean;
    } & {
        color?: string | undefined;
        value?: any;
        title?: string | undefined;
        class?: any;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            activator?: ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            activator?: false | ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:activator"?: false | ((arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    }, {
        style: vue.StyleValue;
        tag: string;
        subgroup: boolean;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        fluid: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        activator: (arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        style: vue.StyleValue;
        tag: string;
        subgroup: boolean;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        fluid: boolean;
    } & {
        color?: string | undefined;
        value?: any;
        title?: string | undefined;
        class?: any;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            activator?: ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            activator?: false | ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:activator"?: false | ((arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    }, {
        isOpen: vue.ComputedRef<boolean>;
    }, {}, {}, {}, {
        style: vue.StyleValue;
        tag: string;
        subgroup: boolean;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        fluid: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    tag: string;
    subgroup: boolean;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    fluid: boolean;
} & {
    color?: string | undefined;
    value?: any;
    title?: string | undefined;
    class?: any;
    baseColor?: string | undefined;
    activeColor?: string | undefined;
    prependIcon?: IconValue | undefined;
    appendIcon?: IconValue | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        activator?: ((arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        activator?: false | ((arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:activator"?: false | ((arg: {
        isOpen: boolean;
        props: Record<string, unknown>;
    }) => vue.VNodeChild) | undefined;
}, {
    isOpen: vue.ComputedRef<boolean>;
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    style: vue.StyleValue;
    tag: string;
    subgroup: boolean;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    fluid: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    activator: (arg: {
        isOpen: boolean;
        props: Record<string, unknown>;
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    activeColor: StringConstructor;
    baseColor: StringConstructor;
    color: StringConstructor;
    collapseIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    expandIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    prependIcon: vue.PropType<IconValue>;
    appendIcon: vue.PropType<IconValue>;
    fluid: BooleanConstructor;
    subgroup: BooleanConstructor;
    title: StringConstructor;
    value: null;
}, vue.ExtractPropTypes<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    activeColor: StringConstructor;
    baseColor: StringConstructor;
    color: StringConstructor;
    collapseIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    expandIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    prependIcon: vue.PropType<IconValue>;
    appendIcon: vue.PropType<IconValue>;
    fluid: BooleanConstructor;
    subgroup: BooleanConstructor;
    title: StringConstructor;
    value: null;
}>>;
type VListGroup = InstanceType<typeof VListGroup>;

declare const VListImg: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        tag: string;
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
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        tag: string;
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
        tag: string;
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
        tag: string;
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
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>, {}, {}, {}, {
        style: vue.StyleValue;
        tag: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    tag: string;
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
}, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
    [key: string]: any;
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    style: vue.StyleValue;
    tag: string;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    tag: {
        type: StringConstructor;
        default: string;
    };
}, vue.ExtractPropTypes<{
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    tag: {
        type: StringConstructor;
        default: string;
    };
}>>;
type VListImg = InstanceType<typeof VListImg>;

declare const VListItemAction: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        end: boolean;
        start: boolean;
        style: vue.StyleValue;
        tag: string;
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
        end: boolean;
        start: boolean;
        style: vue.StyleValue;
        tag: string;
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
        end: boolean;
        start: boolean;
        style: vue.StyleValue;
        tag: string;
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
        end: boolean;
        start: boolean;
        style: vue.StyleValue;
        tag: string;
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
        end: boolean;
        start: boolean;
        style: vue.StyleValue;
        tag: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    end: boolean;
    start: boolean;
    style: vue.StyleValue;
    tag: string;
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
    end: boolean;
    start: boolean;
    style: vue.StyleValue;
    tag: string;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    start: BooleanConstructor;
    end: BooleanConstructor;
}, vue.ExtractPropTypes<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    start: BooleanConstructor;
    end: BooleanConstructor;
}>>;
type VListItemAction = InstanceType<typeof VListItemAction>;

declare const VListItemMedia: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        end: boolean;
        start: boolean;
        style: vue.StyleValue;
        tag: string;
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
        end: boolean;
        start: boolean;
        style: vue.StyleValue;
        tag: string;
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
        end: boolean;
        start: boolean;
        style: vue.StyleValue;
        tag: string;
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
        end: boolean;
        start: boolean;
        style: vue.StyleValue;
        tag: string;
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
        end: boolean;
        start: boolean;
        style: vue.StyleValue;
        tag: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    end: boolean;
    start: boolean;
    style: vue.StyleValue;
    tag: string;
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
    end: boolean;
    start: boolean;
    style: vue.StyleValue;
    tag: string;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    start: BooleanConstructor;
    end: BooleanConstructor;
}, vue.ExtractPropTypes<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    start: BooleanConstructor;
    end: BooleanConstructor;
}>>;
type VListItemMedia = InstanceType<typeof VListItemMedia>;

declare const VListItemSubtitle: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        tag: string;
    } & {
        opacity?: string | number | undefined;
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
        tag: string;
    } & {
        opacity?: string | number | undefined;
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
        tag: string;
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
        tag: string;
    } & {
        opacity?: string | number | undefined;
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
        tag: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    tag: string;
} & {
    opacity?: string | number | undefined;
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
    tag: string;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    opacity: (StringConstructor | NumberConstructor)[];
}, vue.ExtractPropTypes<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    opacity: (StringConstructor | NumberConstructor)[];
}>>;
type VListItemSubtitle = InstanceType<typeof VListItemSubtitle>;

declare const VListItemTitle: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        tag: string;
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
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        tag: string;
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
        tag: string;
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
        tag: string;
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
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>, {}, {}, {}, {
        style: vue.StyleValue;
        tag: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    tag: string;
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
}, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
    [key: string]: any;
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    style: vue.StyleValue;
    tag: string;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    tag: {
        type: StringConstructor;
        default: string;
    };
}, vue.ExtractPropTypes<{
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    tag: {
        type: StringConstructor;
        default: string;
    };
}>>;
type VListItemTitle = InstanceType<typeof VListItemTitle>;

declare const VListSubheader: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        inset: boolean;
        style: vue.StyleValue;
        tag: string;
        sticky: boolean;
    } & {
        color?: string | undefined;
        title?: string | undefined;
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
        inset: boolean;
        style: vue.StyleValue;
        tag: string;
        sticky: boolean;
    } & {
        color?: string | undefined;
        title?: string | undefined;
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
        inset: boolean;
        style: vue.StyleValue;
        tag: string;
        sticky: boolean;
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
        inset: boolean;
        style: vue.StyleValue;
        tag: string;
        sticky: boolean;
    } & {
        color?: string | undefined;
        title?: string | undefined;
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
        inset: boolean;
        style: vue.StyleValue;
        tag: string;
        sticky: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    inset: boolean;
    style: vue.StyleValue;
    tag: string;
    sticky: boolean;
} & {
    color?: string | undefined;
    title?: string | undefined;
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
    inset: boolean;
    style: vue.StyleValue;
    tag: string;
    sticky: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    color: StringConstructor;
    inset: BooleanConstructor;
    sticky: BooleanConstructor;
    title: StringConstructor;
}, vue.ExtractPropTypes<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    color: StringConstructor;
    inset: BooleanConstructor;
    sticky: BooleanConstructor;
    title: StringConstructor;
}>>;
type VListSubheader = InstanceType<typeof VListSubheader>;

export { VList, VListGroup, VListImg, VListItem, VListItemAction, VListItemMedia, VListItemSubtitle, VListItemTitle, VListSubheader };
