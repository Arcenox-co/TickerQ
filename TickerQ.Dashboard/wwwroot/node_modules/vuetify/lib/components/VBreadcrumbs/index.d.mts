import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, VNodeChild, VNode, PropType, ComponentPublicInstance, FunctionalComponent } from 'vue';
// @ts-ignore
import * as vue_router from 'vue-router';
// @ts-ignore
import { RouteLocationRaw } from 'vue-router';

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

type Density = null | 'default' | 'comfortable' | 'compact';

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

interface LinkProps {
    href: string | undefined;
    replace: boolean | undefined;
    to: RouteLocationRaw | undefined;
    exact: boolean | undefined;
}

type InternalBreadcrumbItem = Partial<LinkProps> & {
    title: string;
    disabled?: boolean;
};
type BreadcrumbItem = string | InternalBreadcrumbItem;
declare const VBreadcrumbs: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        divider: string;
        tile: boolean;
        density: Density;
    } & {
        color?: string | undefined;
        class?: any;
        icon?: IconValue | undefined;
        rounded?: string | number | boolean | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<Record<string, any>, "$children" | "v-slots" | "v-slot:default" | "items" | "v-slot:prepend" | "v-slot:title" | "v-slot:item" | "v-slot:divider">, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        divider: string;
        tile: boolean;
        density: Density;
    } & {
        color?: string | undefined;
        class?: any;
        icon?: IconValue | undefined;
        rounded?: string | number | boolean | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
    }, {
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        divider: string;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
    }, true, {}, vue.SlotsType<Partial<{
        prepend: () => vue.VNode[];
        title: (arg: {
            item: InternalBreadcrumbItem;
            index: number;
        }) => vue.VNode[];
        divider: (arg: {
            item: BreadcrumbItem;
            index: number;
        }) => vue.VNode[];
        item: (arg: {
            item: InternalBreadcrumbItem;
            index: number;
        }) => vue.VNode[];
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
        disabled: boolean;
        tag: string;
        divider: string;
        tile: boolean;
        density: Density;
    } & {
        color?: string | undefined;
        class?: any;
        icon?: IconValue | undefined;
        rounded?: string | number | boolean | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
    }, {}, {}, {}, {}, {
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        divider: string;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    divider: string;
    tile: boolean;
    density: Density;
} & {
    color?: string | undefined;
    class?: any;
    icon?: IconValue | undefined;
    rounded?: string | number | boolean | undefined;
    bgColor?: string | undefined;
    activeColor?: string | undefined;
    activeClass?: string | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<Record<string, any>, "$children" | "v-slots" | "v-slot:default" | "items" | "v-slot:prepend" | "v-slot:title" | "v-slot:item" | "v-slot:divider">, string, {
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    divider: string;
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
}, {}, string, vue.SlotsType<Partial<{
    prepend: () => vue.VNode[];
    title: (arg: {
        item: InternalBreadcrumbItem;
        index: number;
    }) => vue.VNode[];
    divider: (arg: {
        item: BreadcrumbItem;
        index: number;
    }) => vue.VNode[];
    item: (arg: {
        item: InternalBreadcrumbItem;
        index: number;
    }) => vue.VNode[];
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & (new <T extends BreadcrumbItem>(props: {
    items?: T[];
}, slots: {
    prepend: never;
    title: {
        item: InternalBreadcrumbItem;
        index: number;
    };
    divider: {
        item: T;
        index: number;
    };
    item: {
        item: InternalBreadcrumbItem;
        index: number;
    };
    default: never;
}) => GenericProps<typeof props, typeof slots>) & FilterPropsOptions<{
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: PropType<string>;
        default: string;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
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
    activeClass: StringConstructor;
    activeColor: StringConstructor;
    bgColor: StringConstructor;
    color: StringConstructor;
    disabled: BooleanConstructor;
    divider: {
        type: StringConstructor;
        default: string;
    };
    icon: PropType<IconValue>;
    items: {
        type: PropType<readonly BreadcrumbItem[]>;
        default: () => never[];
    };
}, vue.ExtractPropTypes<{
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: PropType<string>;
        default: string;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
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
    activeClass: StringConstructor;
    activeColor: StringConstructor;
    bgColor: StringConstructor;
    color: StringConstructor;
    disabled: BooleanConstructor;
    divider: {
        type: StringConstructor;
        default: string;
    };
    icon: PropType<IconValue>;
    items: {
        type: PropType<readonly BreadcrumbItem[]>;
        default: () => never[];
    };
}>>;
type VBreadcrumbs = InstanceType<typeof VBreadcrumbs>;

declare const VBreadcrumbsItem: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        replace: boolean;
        exact: boolean;
        active: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
    } & {
        color?: string | undefined;
        title?: string | undefined;
        class?: any;
        to?: vue_router.RouteLocationRaw | undefined;
        href?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
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
        replace: boolean;
        exact: boolean;
        active: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
    } & {
        color?: string | undefined;
        title?: string | undefined;
        class?: any;
        to?: vue_router.RouteLocationRaw | undefined;
        href?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
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
        replace: boolean;
        exact: boolean;
        active: boolean;
        style: vue.StyleValue;
        disabled: boolean;
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
        replace: boolean;
        exact: boolean;
        active: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
    } & {
        color?: string | undefined;
        title?: string | undefined;
        class?: any;
        to?: vue_router.RouteLocationRaw | undefined;
        href?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
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
        replace: boolean;
        exact: boolean;
        active: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    replace: boolean;
    exact: boolean;
    active: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
} & {
    color?: string | undefined;
    title?: string | undefined;
    class?: any;
    to?: vue_router.RouteLocationRaw | undefined;
    href?: string | undefined;
    activeColor?: string | undefined;
    activeClass?: string | undefined;
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
    replace: boolean;
    exact: boolean;
    active: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<string>;
        default: string;
    };
    href: StringConstructor;
    replace: BooleanConstructor;
    to: vue.PropType<vue_router.RouteLocationRaw>;
    exact: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    active: BooleanConstructor;
    activeClass: StringConstructor;
    activeColor: StringConstructor;
    color: StringConstructor;
    disabled: BooleanConstructor;
    title: StringConstructor;
}, vue.ExtractPropTypes<{
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<string>;
        default: string;
    };
    href: StringConstructor;
    replace: BooleanConstructor;
    to: vue.PropType<vue_router.RouteLocationRaw>;
    exact: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    active: BooleanConstructor;
    activeClass: StringConstructor;
    activeColor: StringConstructor;
    color: StringConstructor;
    disabled: BooleanConstructor;
    title: StringConstructor;
}>>;
type VBreadcrumbsItem = InstanceType<typeof VBreadcrumbsItem>;

declare const VBreadcrumbsDivider: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
    } & {
        class?: any;
        divider?: string | number | undefined;
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
    } & {
        class?: any;
        divider?: string | number | undefined;
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
    } & {
        class?: any;
        divider?: string | number | undefined;
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
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
} & {
    class?: any;
    divider?: string | number | undefined;
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
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    divider: (StringConstructor | NumberConstructor)[];
}, vue.ExtractPropTypes<{
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    divider: (StringConstructor | NumberConstructor)[];
}>>;
type VBreadcrumbsDivider = InstanceType<typeof VBreadcrumbsDivider>;

export { VBreadcrumbs, VBreadcrumbsDivider, VBreadcrumbsItem };
