import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

declare const breakpoints: readonly ["sm", "md", "lg", "xl", "xxl"];
type Breakpoint = typeof breakpoints[number];
type DisplayBreakpoint = 'xs' | Breakpoint;

type VNavigationDrawerImageSlot = {
    image: string | undefined;
};
declare const locations: readonly ["start", "end", "left", "right", "top", "bottom"];
declare const VNavigationDrawer: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        absolute: boolean;
        location: "end" | "start" | "left" | "top" | "bottom" | "right";
        width: string | number;
        order: string | number;
        style: vue.StyleValue;
        mobile: boolean | null;
        temporary: boolean;
        persistent: boolean;
        tag: string;
        sticky: boolean;
        modelValue: boolean | null;
        tile: boolean;
        floating: boolean;
        scrim: string | boolean;
        touchless: boolean;
        disableResizeWatcher: boolean;
        disableRouteWatcher: boolean;
        expandOnHover: boolean;
        permanent: boolean;
        rail: boolean | null;
        railWidth: string | number;
    } & {
        name?: string | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        image?: string | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        mobileBreakpoint?: number | DisplayBreakpoint | undefined;
        rounded?: string | number | boolean | undefined;
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            prepend?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
            image?: ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            prepend?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
            image?: false | ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:image"?: false | ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: boolean) => any) | undefined;
        "onUpdate:rail"?: ((val: boolean) => any) | undefined;
    }, {
        isStuck: vue.ShallowRef<boolean | "top" | "bottom">;
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (val: boolean) => true;
        'update:rail': (val: boolean) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        absolute: boolean;
        location: "end" | "start" | "left" | "top" | "bottom" | "right";
        width: string | number;
        order: string | number;
        style: vue.StyleValue;
        mobile: boolean | null;
        temporary: boolean;
        persistent: boolean;
        tag: string;
        sticky: boolean;
        modelValue: boolean | null;
        tile: boolean;
        floating: boolean;
        scrim: string | boolean;
        touchless: boolean;
        disableResizeWatcher: boolean;
        disableRouteWatcher: boolean;
        expandOnHover: boolean;
        permanent: boolean;
        rail: boolean | null;
        railWidth: string | number;
    } & {
        name?: string | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        image?: string | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        mobileBreakpoint?: number | DisplayBreakpoint | undefined;
        rounded?: string | number | boolean | undefined;
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            prepend?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
            image?: ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            prepend?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
            image?: false | ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:image"?: false | ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: boolean) => any) | undefined;
        "onUpdate:rail"?: ((val: boolean) => any) | undefined;
    }, {
        absolute: boolean;
        location: "end" | "start" | "left" | "top" | "bottom" | "right";
        width: string | number;
        order: string | number;
        style: vue.StyleValue;
        mobile: boolean | null;
        temporary: boolean;
        persistent: boolean;
        tag: string;
        sticky: boolean;
        modelValue: boolean | null;
        rounded: string | number | boolean;
        tile: boolean;
        floating: boolean;
        scrim: string | boolean;
        touchless: boolean;
        disableResizeWatcher: boolean;
        disableRouteWatcher: boolean;
        expandOnHover: boolean;
        permanent: boolean;
        rail: boolean | null;
        railWidth: string | number;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        prepend: () => vue.VNode[];
        append: () => vue.VNode[];
        image: (arg: VNavigationDrawerImageSlot) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        absolute: boolean;
        location: "end" | "start" | "left" | "top" | "bottom" | "right";
        width: string | number;
        order: string | number;
        style: vue.StyleValue;
        mobile: boolean | null;
        temporary: boolean;
        persistent: boolean;
        tag: string;
        sticky: boolean;
        modelValue: boolean | null;
        tile: boolean;
        floating: boolean;
        scrim: string | boolean;
        touchless: boolean;
        disableResizeWatcher: boolean;
        disableRouteWatcher: boolean;
        expandOnHover: boolean;
        permanent: boolean;
        rail: boolean | null;
        railWidth: string | number;
    } & {
        name?: string | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        image?: string | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        mobileBreakpoint?: number | DisplayBreakpoint | undefined;
        rounded?: string | number | boolean | undefined;
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            prepend?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
            image?: ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            prepend?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
            image?: false | ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:image"?: false | ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: boolean) => any) | undefined;
        "onUpdate:rail"?: ((val: boolean) => any) | undefined;
    }, {
        isStuck: vue.ShallowRef<boolean | "top" | "bottom">;
    }, {}, {}, {}, {
        absolute: boolean;
        location: "end" | "start" | "left" | "top" | "bottom" | "right";
        width: string | number;
        order: string | number;
        style: vue.StyleValue;
        mobile: boolean | null;
        temporary: boolean;
        persistent: boolean;
        tag: string;
        sticky: boolean;
        modelValue: boolean | null;
        rounded: string | number | boolean;
        tile: boolean;
        floating: boolean;
        scrim: string | boolean;
        touchless: boolean;
        disableResizeWatcher: boolean;
        disableRouteWatcher: boolean;
        expandOnHover: boolean;
        permanent: boolean;
        rail: boolean | null;
        railWidth: string | number;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    absolute: boolean;
    location: "end" | "start" | "left" | "top" | "bottom" | "right";
    width: string | number;
    order: string | number;
    style: vue.StyleValue;
    mobile: boolean | null;
    temporary: boolean;
    persistent: boolean;
    tag: string;
    sticky: boolean;
    modelValue: boolean | null;
    tile: boolean;
    floating: boolean;
    scrim: string | boolean;
    touchless: boolean;
    disableResizeWatcher: boolean;
    disableRouteWatcher: boolean;
    expandOnHover: boolean;
    permanent: boolean;
    rail: boolean | null;
    railWidth: string | number;
} & {
    name?: string | undefined;
    border?: string | number | boolean | undefined;
    color?: string | undefined;
    image?: string | undefined;
    class?: any;
    theme?: string | undefined;
    elevation?: string | number | undefined;
    mobileBreakpoint?: number | DisplayBreakpoint | undefined;
    rounded?: string | number | boolean | undefined;
    closeDelay?: string | number | undefined;
    openDelay?: string | number | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        prepend?: (() => vue.VNodeChild) | undefined;
        append?: (() => vue.VNodeChild) | undefined;
        image?: ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        prepend?: false | (() => vue.VNodeChild) | undefined;
        append?: false | (() => vue.VNodeChild) | undefined;
        image?: false | ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:image"?: false | ((arg: VNavigationDrawerImageSlot) => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((val: boolean) => any) | undefined;
    "onUpdate:rail"?: ((val: boolean) => any) | undefined;
}, {
    isStuck: vue.ShallowRef<boolean | "top" | "bottom">;
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (val: boolean) => true;
    'update:rail': (val: boolean) => true;
}, string, {
    absolute: boolean;
    location: "end" | "start" | "left" | "top" | "bottom" | "right";
    width: string | number;
    order: string | number;
    style: vue.StyleValue;
    mobile: boolean | null;
    temporary: boolean;
    persistent: boolean;
    tag: string;
    sticky: boolean;
    modelValue: boolean | null;
    rounded: string | number | boolean;
    tile: boolean;
    floating: boolean;
    scrim: string | boolean;
    touchless: boolean;
    disableResizeWatcher: boolean;
    disableRouteWatcher: boolean;
    expandOnHover: boolean;
    permanent: boolean;
    rail: boolean | null;
    railWidth: string | number;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    prepend: () => vue.VNode[];
    append: () => vue.VNode[];
    image: (arg: VNavigationDrawerImageSlot) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    theme: StringConstructor;
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
    name: {
        type: StringConstructor;
    };
    order: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    absolute: BooleanConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    mobile: Omit<{
        type: PropType<boolean | null>;
        default: boolean;
    }, "type" | "default"> & {
        type: PropType<boolean | null>;
        default: NonNullable<boolean | null> | null;
    };
    mobileBreakpoint: PropType<number | DisplayBreakpoint>;
    closeDelay: (StringConstructor | NumberConstructor)[];
    openDelay: (StringConstructor | NumberConstructor)[];
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    disableResizeWatcher: BooleanConstructor;
    disableRouteWatcher: BooleanConstructor;
    expandOnHover: BooleanConstructor;
    floating: BooleanConstructor;
    modelValue: {
        type: PropType<boolean | null>;
        default: null;
    };
    permanent: BooleanConstructor;
    rail: {
        type: PropType<boolean | null>;
        default: null;
    };
    railWidth: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    scrim: {
        type: (StringConstructor | BooleanConstructor)[];
        default: boolean;
    };
    image: StringConstructor;
    temporary: BooleanConstructor;
    persistent: BooleanConstructor;
    touchless: BooleanConstructor;
    width: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    location: {
        type: PropType<(typeof locations)[number]>;
        default: string;
        validator: (value: any) => boolean;
    };
    sticky: BooleanConstructor;
}, vue.ExtractPropTypes<{
    theme: StringConstructor;
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
    name: {
        type: StringConstructor;
    };
    order: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    absolute: BooleanConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    mobile: Omit<{
        type: PropType<boolean | null>;
        default: boolean;
    }, "type" | "default"> & {
        type: PropType<boolean | null>;
        default: NonNullable<boolean | null> | null;
    };
    mobileBreakpoint: PropType<number | DisplayBreakpoint>;
    closeDelay: (StringConstructor | NumberConstructor)[];
    openDelay: (StringConstructor | NumberConstructor)[];
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    disableResizeWatcher: BooleanConstructor;
    disableRouteWatcher: BooleanConstructor;
    expandOnHover: BooleanConstructor;
    floating: BooleanConstructor;
    modelValue: {
        type: PropType<boolean | null>;
        default: null;
    };
    permanent: BooleanConstructor;
    rail: {
        type: PropType<boolean | null>;
        default: null;
    };
    railWidth: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    scrim: {
        type: (StringConstructor | BooleanConstructor)[];
        default: boolean;
    };
    image: StringConstructor;
    temporary: BooleanConstructor;
    persistent: BooleanConstructor;
    touchless: BooleanConstructor;
    width: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    location: {
        type: PropType<(typeof locations)[number]>;
        default: string;
        validator: (value: any) => boolean;
    };
    sticky: BooleanConstructor;
}>>;
type VNavigationDrawer = InstanceType<typeof VNavigationDrawer>;

export { VNavigationDrawer };
