import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, ComponentInternalInstance, Ref, ComputedRef, DirectiveBinding, PropType, ComponentPublicInstance, FunctionalComponent } from 'vue';
// @ts-ignore
import * as vue_router from 'vue-router';

type ClassValue = any;

declare const block: readonly ["top", "bottom"];
declare const inline: readonly ["start", "end", "left", "right"];
type Tblock = typeof block[number];
type Tinline = typeof inline[number];
type Anchor = Tblock | Tinline | 'center' | 'center center' | `${Tblock} ${Tinline | 'center'}` | `${Tinline} ${Tblock | 'center'}`;

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

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

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

declare const allowedVariants: readonly ["elevated", "flat", "tonal", "outlined", "text", "plain"];
type Variant = typeof allowedVariants[number];

declare const VTab: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        replace: boolean;
        fixed: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        selectedClass: string;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        hideSlider: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
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
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        sliderColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            prepend?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
            loader?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            prepend?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
            loader?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
    }, Omit<Omit<{
        $: vue.ComponentInternalInstance;
        $data: {};
        $props: Partial<{
            symbol: any;
            replace: boolean;
            flat: boolean;
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            exact: boolean;
            block: boolean;
            active: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            size: string | number;
            readonly: boolean;
            tag: string;
            rounded: string | number | boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            stacked: boolean;
            ripple: boolean | {
                class: string;
            } | undefined;
        }> & Omit<{
            symbol: any;
            replace: boolean;
            flat: boolean;
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            exact: boolean;
            block: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            size: string | number;
            readonly: boolean;
            tag: string;
            tile: boolean;
            density: Density;
            slim: boolean;
            stacked: boolean;
            ripple: boolean | {
                class: string;
            } | undefined;
            location?: Anchor | null | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            active?: boolean | undefined;
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
            $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
                default?: (() => vue.VNodeChild) | undefined;
                prepend?: (() => vue.VNodeChild) | undefined;
                append?: (() => vue.VNodeChild) | undefined;
                loader?: (() => vue.VNodeChild) | undefined;
            };
            'v-slots'?: {
                default?: false | (() => vue.VNodeChild) | undefined;
                prepend?: false | (() => vue.VNodeChild) | undefined;
                append?: false | (() => vue.VNodeChild) | undefined;
                loader?: false | (() => vue.VNodeChild) | undefined;
            } | undefined;
            "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
            "onGroup:selected"?: ((val: {
                value: boolean;
            }) => any) | undefined;
        } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
            symbol: any;
            replace: boolean;
            flat: boolean;
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            exact: boolean;
            block: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            size: string | number;
            readonly: boolean;
            tag: string;
            tile: boolean;
            density: Density;
            slim: boolean;
            stacked: boolean;
            ripple: boolean | {
                class: string;
            } | undefined;
        } & {
            location?: Anchor | null | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            active?: boolean | undefined;
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
            $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
                default?: (() => vue.VNodeChild) | undefined;
                prepend?: (() => vue.VNodeChild) | undefined;
                append?: (() => vue.VNodeChild) | undefined;
                loader?: (() => vue.VNodeChild) | undefined;
            };
            'v-slots'?: {
                default?: false | (() => vue.VNodeChild) | undefined;
                prepend?: false | (() => vue.VNodeChild) | undefined;
                append?: false | (() => vue.VNodeChild) | undefined;
                loader?: false | (() => vue.VNodeChild) | undefined;
            } | undefined;
        } & {
            "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
        } & {
            "onGroup:selected"?: ((val: {
                value: boolean;
            }) => any) | undefined;
        }, "symbol" | "replace" | "flat" | "variant" | "exact" | "block" | "active" | "style" | "disabled" | "size" | "readonly" | "tag" | "rounded" | "tile" | "density" | "slim" | "stacked" | "ripple">;
        $attrs: {
            [x: string]: unknown;
        };
        $refs: {
            [x: string]: unknown;
        };
        $slots: Readonly<{
            default?: (() => vue.VNode[]) | undefined;
            prepend?: (() => vue.VNode[]) | undefined;
            append?: (() => vue.VNode[]) | undefined;
            loader?: (() => vue.VNode[]) | undefined;
        }>;
        $root: vue.ComponentPublicInstance | null;
        $parent: vue.ComponentPublicInstance | null;
        $emit: (event: "group:selected", val: {
            value: boolean;
        }) => void;
        $el: any;
        $options: vue.ComponentOptionsBase<{
            symbol: any;
            replace: boolean;
            flat: boolean;
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            exact: boolean;
            block: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            size: string | number;
            readonly: boolean;
            tag: string;
            tile: boolean;
            density: Density;
            slim: boolean;
            stacked: boolean;
            ripple: boolean | {
                class: string;
            } | undefined;
        } & {
            location?: Anchor | null | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            active?: boolean | undefined;
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
            $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
                default?: (() => vue.VNodeChild) | undefined;
                prepend?: (() => vue.VNodeChild) | undefined;
                append?: (() => vue.VNodeChild) | undefined;
                loader?: (() => vue.VNodeChild) | undefined;
            };
            'v-slots'?: {
                default?: false | (() => vue.VNodeChild) | undefined;
                prepend?: false | (() => vue.VNodeChild) | undefined;
                append?: false | (() => vue.VNodeChild) | undefined;
                loader?: false | (() => vue.VNodeChild) | undefined;
            } | undefined;
        } & {
            "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
        } & {
            "onGroup:selected"?: ((val: {
                value: boolean;
            }) => any) | undefined;
        }, {
            group: GroupItemProvide | null;
        }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
            'group:selected': (val: {
                value: boolean;
            }) => true;
        }, string, {
            symbol: any;
            replace: boolean;
            flat: boolean;
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            exact: boolean;
            block: boolean;
            active: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            size: string | number;
            readonly: boolean;
            tag: string;
            rounded: string | number | boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            stacked: boolean;
            ripple: boolean | {
                class: string;
            } | undefined;
        }, {}, string, vue.SlotsType<Partial<{
            default: () => vue.VNode[];
            prepend: () => vue.VNode[];
            append: () => vue.VNode[];
            loader: () => vue.VNode[];
        }>>> & {
            beforeCreate?: (() => void) | (() => void)[];
            created?: (() => void) | (() => void)[];
            beforeMount?: (() => void) | (() => void)[];
            mounted?: (() => void) | (() => void)[];
            beforeUpdate?: (() => void) | (() => void)[];
            updated?: (() => void) | (() => void)[];
            activated?: (() => void) | (() => void)[];
            deactivated?: (() => void) | (() => void)[];
            beforeDestroy?: (() => void) | (() => void)[];
            beforeUnmount?: (() => void) | (() => void)[];
            destroyed?: (() => void) | (() => void)[];
            unmounted?: (() => void) | (() => void)[];
            renderTracked?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
            renderTriggered?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
            errorCaptured?: ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void) | ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void)[];
        };
        $forceUpdate: () => void;
        $nextTick: typeof vue.nextTick;
        $watch<T extends string | ((...args: any) => any)>(source: T, cb: T extends (...args: any) => infer R ? (...args: [R, R]) => any : (...args: any) => any, options?: vue.WatchOptions): vue.WatchStopHandle;
    } & Omit<{
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        block: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
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
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            prepend?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
            loader?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            prepend?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
            loader?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onGroup:selected"?: ((val: {
            value: boolean;
        }) => any) | undefined;
    }, "group"> & vue.ShallowUnwrapRef<{
        group: GroupItemProvide | null;
    }> & {} & vue.ComponentCustomProperties & {}, "key" | "location" | "height" | "width" | "border" | "color" | "maxHeight" | "maxWidth" | "minHeight" | "minWidth" | "position" | "value" | "loading" | "text" | "class" | "theme" | "ref" | "to" | "icon" | "$children" | "href" | "elevation" | "v-slots" | "v-slot:default" | "ref_for" | "ref_key" | "onVnodeBeforeMount" | "onVnodeMounted" | "onVnodeBeforeUpdate" | "onVnodeUpdated" | "onVnodeBeforeUnmount" | "onVnodeUnmounted" | "v-slot:prepend" | "v-slot:append" | "baseColor" | "selectedClass" | "onGroup:selected" | "activeColor" | "prependIcon" | "appendIcon" | ("symbol" | "replace" | "flat" | "variant" | "exact" | "block" | "active" | "style" | "disabled" | "size" | "readonly" | "tag" | "rounded" | "tile" | "density" | "slim" | "stacked" | "ripple") | "v-slot:loader">, `$${any}`> & {
        _allExposed: {
            group: GroupItemProvide | null;
        } | {};
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        replace: boolean;
        fixed: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        selectedClass: string;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        hideSlider: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
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
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        sliderColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            prepend?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
            loader?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            prepend?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
            loader?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        replace: boolean;
        fixed: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        selectedClass: string;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        hideSlider: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        prepend: () => vue.VNode[];
        append: () => vue.VNode[];
        loader: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        replace: boolean;
        fixed: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        selectedClass: string;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        hideSlider: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
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
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        sliderColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            prepend?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
            loader?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            prepend?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
            loader?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
    }, Omit<Omit<{
        $: vue.ComponentInternalInstance;
        $data: {};
        $props: Partial<{
            symbol: any;
            replace: boolean;
            flat: boolean;
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            exact: boolean;
            block: boolean;
            active: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            size: string | number;
            readonly: boolean;
            tag: string;
            rounded: string | number | boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            stacked: boolean;
            ripple: boolean | {
                class: string;
            } | undefined;
        }> & Omit<{
            symbol: any;
            replace: boolean;
            flat: boolean;
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            exact: boolean;
            block: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            size: string | number;
            readonly: boolean;
            tag: string;
            tile: boolean;
            density: Density;
            slim: boolean;
            stacked: boolean;
            ripple: boolean | {
                class: string;
            } | undefined;
            location?: Anchor | null | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            active?: boolean | undefined;
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
            $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
                default?: (() => vue.VNodeChild) | undefined;
                prepend?: (() => vue.VNodeChild) | undefined;
                append?: (() => vue.VNodeChild) | undefined;
                loader?: (() => vue.VNodeChild) | undefined;
            };
            'v-slots'?: {
                default?: false | (() => vue.VNodeChild) | undefined;
                prepend?: false | (() => vue.VNodeChild) | undefined;
                append?: false | (() => vue.VNodeChild) | undefined;
                loader?: false | (() => vue.VNodeChild) | undefined;
            } | undefined;
            "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
            "onGroup:selected"?: ((val: {
                value: boolean;
            }) => any) | undefined;
        } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
            symbol: any;
            replace: boolean;
            flat: boolean;
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            exact: boolean;
            block: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            size: string | number;
            readonly: boolean;
            tag: string;
            tile: boolean;
            density: Density;
            slim: boolean;
            stacked: boolean;
            ripple: boolean | {
                class: string;
            } | undefined;
        } & {
            location?: Anchor | null | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            active?: boolean | undefined;
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
            $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
                default?: (() => vue.VNodeChild) | undefined;
                prepend?: (() => vue.VNodeChild) | undefined;
                append?: (() => vue.VNodeChild) | undefined;
                loader?: (() => vue.VNodeChild) | undefined;
            };
            'v-slots'?: {
                default?: false | (() => vue.VNodeChild) | undefined;
                prepend?: false | (() => vue.VNodeChild) | undefined;
                append?: false | (() => vue.VNodeChild) | undefined;
                loader?: false | (() => vue.VNodeChild) | undefined;
            } | undefined;
        } & {
            "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
        } & {
            "onGroup:selected"?: ((val: {
                value: boolean;
            }) => any) | undefined;
        }, "symbol" | "replace" | "flat" | "variant" | "exact" | "block" | "active" | "style" | "disabled" | "size" | "readonly" | "tag" | "rounded" | "tile" | "density" | "slim" | "stacked" | "ripple">;
        $attrs: {
            [x: string]: unknown;
        };
        $refs: {
            [x: string]: unknown;
        };
        $slots: Readonly<{
            default?: (() => vue.VNode[]) | undefined;
            prepend?: (() => vue.VNode[]) | undefined;
            append?: (() => vue.VNode[]) | undefined;
            loader?: (() => vue.VNode[]) | undefined;
        }>;
        $root: vue.ComponentPublicInstance | null;
        $parent: vue.ComponentPublicInstance | null;
        $emit: (event: "group:selected", val: {
            value: boolean;
        }) => void;
        $el: any;
        $options: vue.ComponentOptionsBase<{
            symbol: any;
            replace: boolean;
            flat: boolean;
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            exact: boolean;
            block: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            size: string | number;
            readonly: boolean;
            tag: string;
            tile: boolean;
            density: Density;
            slim: boolean;
            stacked: boolean;
            ripple: boolean | {
                class: string;
            } | undefined;
        } & {
            location?: Anchor | null | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            active?: boolean | undefined;
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
            $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
                default?: (() => vue.VNodeChild) | undefined;
                prepend?: (() => vue.VNodeChild) | undefined;
                append?: (() => vue.VNodeChild) | undefined;
                loader?: (() => vue.VNodeChild) | undefined;
            };
            'v-slots'?: {
                default?: false | (() => vue.VNodeChild) | undefined;
                prepend?: false | (() => vue.VNodeChild) | undefined;
                append?: false | (() => vue.VNodeChild) | undefined;
                loader?: false | (() => vue.VNodeChild) | undefined;
            } | undefined;
        } & {
            "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
        } & {
            "onGroup:selected"?: ((val: {
                value: boolean;
            }) => any) | undefined;
        }, {
            group: GroupItemProvide | null;
        }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
            'group:selected': (val: {
                value: boolean;
            }) => true;
        }, string, {
            symbol: any;
            replace: boolean;
            flat: boolean;
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            exact: boolean;
            block: boolean;
            active: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            size: string | number;
            readonly: boolean;
            tag: string;
            rounded: string | number | boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            stacked: boolean;
            ripple: boolean | {
                class: string;
            } | undefined;
        }, {}, string, vue.SlotsType<Partial<{
            default: () => vue.VNode[];
            prepend: () => vue.VNode[];
            append: () => vue.VNode[];
            loader: () => vue.VNode[];
        }>>> & {
            beforeCreate?: (() => void) | (() => void)[];
            created?: (() => void) | (() => void)[];
            beforeMount?: (() => void) | (() => void)[];
            mounted?: (() => void) | (() => void)[];
            beforeUpdate?: (() => void) | (() => void)[];
            updated?: (() => void) | (() => void)[];
            activated?: (() => void) | (() => void)[];
            deactivated?: (() => void) | (() => void)[];
            beforeDestroy?: (() => void) | (() => void)[];
            beforeUnmount?: (() => void) | (() => void)[];
            destroyed?: (() => void) | (() => void)[];
            unmounted?: (() => void) | (() => void)[];
            renderTracked?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
            renderTriggered?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
            errorCaptured?: ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void) | ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void)[];
        };
        $forceUpdate: () => void;
        $nextTick: typeof vue.nextTick;
        $watch<T extends string | ((...args: any) => any)>(source: T, cb: T extends (...args: any) => infer R ? (...args: [R, R]) => any : (...args: any) => any, options?: vue.WatchOptions): vue.WatchStopHandle;
    } & Omit<{
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        block: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
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
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            prepend?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
            loader?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            prepend?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
            loader?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onGroup:selected"?: ((val: {
            value: boolean;
        }) => any) | undefined;
    }, "group"> & vue.ShallowUnwrapRef<{
        group: GroupItemProvide | null;
    }> & {} & vue.ComponentCustomProperties & {}, "key" | "location" | "height" | "width" | "border" | "color" | "maxHeight" | "maxWidth" | "minHeight" | "minWidth" | "position" | "value" | "loading" | "text" | "class" | "theme" | "ref" | "to" | "icon" | "$children" | "href" | "elevation" | "v-slots" | "v-slot:default" | "ref_for" | "ref_key" | "onVnodeBeforeMount" | "onVnodeMounted" | "onVnodeBeforeUpdate" | "onVnodeUpdated" | "onVnodeBeforeUnmount" | "onVnodeUnmounted" | "v-slot:prepend" | "v-slot:append" | "baseColor" | "selectedClass" | "onGroup:selected" | "activeColor" | "prependIcon" | "appendIcon" | ("symbol" | "replace" | "flat" | "variant" | "exact" | "block" | "active" | "style" | "disabled" | "size" | "readonly" | "tag" | "rounded" | "tile" | "density" | "slim" | "stacked" | "ripple") | "v-slot:loader">, `$${any}`> & {
        _allExposed: {
            group: GroupItemProvide | null;
        } | {};
    }, {}, {}, {}, {
        replace: boolean;
        fixed: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        selectedClass: string;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        hideSlider: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    replace: boolean;
    fixed: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    exact: boolean;
    direction: "horizontal" | "vertical";
    style: vue.StyleValue;
    disabled: boolean;
    size: string | number;
    readonly: boolean;
    tag: string;
    tile: boolean;
    density: Density;
    selectedClass: string;
    slim: boolean;
    stacked: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
    hideSlider: boolean;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    border?: string | number | boolean | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
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
    activeColor?: string | undefined;
    prependIcon?: IconValue | undefined;
    appendIcon?: IconValue | undefined;
    sliderColor?: string | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        prepend?: (() => vue.VNodeChild) | undefined;
        append?: (() => vue.VNodeChild) | undefined;
        loader?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        prepend?: false | (() => vue.VNodeChild) | undefined;
        append?: false | (() => vue.VNodeChild) | undefined;
        loader?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
}, Omit<Omit<{
    $: vue.ComponentInternalInstance;
    $data: {};
    $props: Partial<{
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        block: boolean;
        active: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    }> & Omit<{
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        block: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
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
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            prepend?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
            loader?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            prepend?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
            loader?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
        "onGroup:selected"?: ((val: {
            value: boolean;
        }) => any) | undefined;
    } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        block: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
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
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            prepend?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
            loader?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            prepend?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
            loader?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onGroup:selected"?: ((val: {
            value: boolean;
        }) => any) | undefined;
    }, "symbol" | "replace" | "flat" | "variant" | "exact" | "block" | "active" | "style" | "disabled" | "size" | "readonly" | "tag" | "rounded" | "tile" | "density" | "slim" | "stacked" | "ripple">;
    $attrs: {
        [x: string]: unknown;
    };
    $refs: {
        [x: string]: unknown;
    };
    $slots: Readonly<{
        default?: (() => vue.VNode[]) | undefined;
        prepend?: (() => vue.VNode[]) | undefined;
        append?: (() => vue.VNode[]) | undefined;
        loader?: (() => vue.VNode[]) | undefined;
    }>;
    $root: vue.ComponentPublicInstance | null;
    $parent: vue.ComponentPublicInstance | null;
    $emit: (event: "group:selected", val: {
        value: boolean;
    }) => void;
    $el: any;
    $options: vue.ComponentOptionsBase<{
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        block: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
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
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            prepend?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
            loader?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            prepend?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
            loader?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onGroup:selected"?: ((val: {
            value: boolean;
        }) => any) | undefined;
    }, {
        group: GroupItemProvide | null;
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'group:selected': (val: {
            value: boolean;
        }) => true;
    }, string, {
        symbol: any;
        replace: boolean;
        flat: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        block: boolean;
        active: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        stacked: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    }, {}, string, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        prepend: () => vue.VNode[];
        append: () => vue.VNode[];
        loader: () => vue.VNode[];
    }>>> & {
        beforeCreate?: (() => void) | (() => void)[];
        created?: (() => void) | (() => void)[];
        beforeMount?: (() => void) | (() => void)[];
        mounted?: (() => void) | (() => void)[];
        beforeUpdate?: (() => void) | (() => void)[];
        updated?: (() => void) | (() => void)[];
        activated?: (() => void) | (() => void)[];
        deactivated?: (() => void) | (() => void)[];
        beforeDestroy?: (() => void) | (() => void)[];
        beforeUnmount?: (() => void) | (() => void)[];
        destroyed?: (() => void) | (() => void)[];
        unmounted?: (() => void) | (() => void)[];
        renderTracked?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
        renderTriggered?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
        errorCaptured?: ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void) | ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void)[];
    };
    $forceUpdate: () => void;
    $nextTick: typeof vue.nextTick;
    $watch<T extends string | ((...args: any) => any)>(source: T, cb: T extends (...args: any) => infer R ? (...args: [R, R]) => any : (...args: any) => any, options?: vue.WatchOptions): vue.WatchStopHandle;
} & Omit<{
    symbol: any;
    replace: boolean;
    flat: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    exact: boolean;
    block: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    size: string | number;
    readonly: boolean;
    tag: string;
    tile: boolean;
    density: Density;
    slim: boolean;
    stacked: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
} & {
    location?: Anchor | null | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    active?: boolean | undefined;
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
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        prepend?: (() => vue.VNodeChild) | undefined;
        append?: (() => vue.VNodeChild) | undefined;
        loader?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        prepend?: false | (() => vue.VNodeChild) | undefined;
        append?: false | (() => vue.VNodeChild) | undefined;
        loader?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:loader"?: false | (() => vue.VNodeChild) | undefined;
} & {
    "onGroup:selected"?: ((val: {
        value: boolean;
    }) => any) | undefined;
}, "group"> & vue.ShallowUnwrapRef<{
    group: GroupItemProvide | null;
}> & {} & vue.ComponentCustomProperties & {}, "key" | "location" | "height" | "width" | "border" | "color" | "maxHeight" | "maxWidth" | "minHeight" | "minWidth" | "position" | "value" | "loading" | "text" | "class" | "theme" | "ref" | "to" | "icon" | "$children" | "href" | "elevation" | "v-slots" | "v-slot:default" | "ref_for" | "ref_key" | "onVnodeBeforeMount" | "onVnodeMounted" | "onVnodeBeforeUpdate" | "onVnodeUpdated" | "onVnodeBeforeUnmount" | "onVnodeUnmounted" | "v-slot:prepend" | "v-slot:append" | "baseColor" | "selectedClass" | "onGroup:selected" | "activeColor" | "prependIcon" | "appendIcon" | ("symbol" | "replace" | "flat" | "variant" | "exact" | "block" | "active" | "style" | "disabled" | "size" | "readonly" | "tag" | "rounded" | "tile" | "density" | "slim" | "stacked" | "ripple") | "v-slot:loader">, `$${any}`> & {
    _allExposed: {
        group: GroupItemProvide | null;
    } | {};
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    replace: boolean;
    fixed: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    exact: boolean;
    direction: "horizontal" | "vertical";
    style: vue.StyleValue;
    disabled: boolean;
    size: string | number;
    readonly: boolean;
    tag: string;
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    selectedClass: string;
    slim: boolean;
    stacked: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
    hideSlider: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    prepend: () => vue.VNode[];
    append: () => vue.VNode[];
    loader: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    replace: BooleanConstructor;
    variant: Omit<Omit<{
        type: PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    }, "type" | "default"> & {
        type: PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    exact: BooleanConstructor;
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    value: null;
    loading: (StringConstructor | BooleanConstructor)[];
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    text: StringConstructor;
    disabled: BooleanConstructor;
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
    readonly: BooleanConstructor;
    class: PropType<ClassValue>;
    theme: StringConstructor;
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: PropType<string>;
        default: string;
    };
    to: PropType<vue_router.RouteLocationRaw>;
    icon: PropType<boolean | IconValue>;
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
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    baseColor: StringConstructor;
    selectedClass: {
        type: PropType<string>;
        default: string;
    };
    activeColor: StringConstructor;
    prependIcon: PropType<IconValue>;
    appendIcon: PropType<IconValue>;
    slim: BooleanConstructor;
    stacked: BooleanConstructor;
    ripple: {
        type: PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    fixed: BooleanConstructor;
    sliderColor: StringConstructor;
    hideSlider: BooleanConstructor;
    direction: {
        type: PropType<"horizontal" | "vertical">;
        default: string;
    };
}, vue.ExtractPropTypes<{
    replace: BooleanConstructor;
    variant: Omit<Omit<{
        type: PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    }, "type" | "default"> & {
        type: PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    exact: BooleanConstructor;
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    value: null;
    loading: (StringConstructor | BooleanConstructor)[];
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    text: StringConstructor;
    disabled: BooleanConstructor;
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
    readonly: BooleanConstructor;
    class: PropType<ClassValue>;
    theme: StringConstructor;
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: PropType<string>;
        default: string;
    };
    to: PropType<vue_router.RouteLocationRaw>;
    icon: PropType<boolean | IconValue>;
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
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    baseColor: StringConstructor;
    selectedClass: {
        type: PropType<string>;
        default: string;
    };
    activeColor: StringConstructor;
    prependIcon: PropType<IconValue>;
    appendIcon: PropType<IconValue>;
    slim: BooleanConstructor;
    stacked: BooleanConstructor;
    ripple: {
        type: PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    fixed: BooleanConstructor;
    sliderColor: StringConstructor;
    hideSlider: BooleanConstructor;
    direction: {
        type: PropType<"horizontal" | "vertical">;
        default: string;
    };
}>>;
type VTab = InstanceType<typeof VTab>;

declare const breakpoints: readonly ["sm", "md", "lg", "xl", "xxl"];
type Breakpoint = typeof breakpoints[number];
type DisplayBreakpoint = 'xs' | Breakpoint;

type TabItem = string | number | Record<string, any>;
type VTabsSlot = {
    item: TabItem;
};
declare const VTabs: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        symbol: any;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        mobile: boolean | null;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        mandatory: boolean | "force";
        items: readonly TabItem[];
        density: Density;
        selectedClass: string;
        stacked: boolean;
        centerActive: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        grow: boolean;
        hideSlider: boolean;
        alignTabs: "center" | "end" | "start" | "title";
        fixedTabs: boolean;
    } & {
        max?: number | undefined;
        height?: string | number | undefined;
        color?: string | undefined;
        class?: any;
        mobileBreakpoint?: number | DisplayBreakpoint | undefined;
        modelValue?: any;
        bgColor?: string | undefined;
        showArrows?: string | boolean | undefined;
        sliderColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            [x: `tab.${string}`]: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            [x: `item.${string}`]: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            tab?: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            item?: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            window?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            [x: `tab.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            [x: `item.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            tab?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            item?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            window?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        [x: `v-slot:tab.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        [x: `v-slot:item.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:tab"?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        "v-slot:item"?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        "v-slot:window"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((v: unknown) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (v: unknown) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        symbol: any;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        mobile: boolean | null;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        mandatory: boolean | "force";
        items: readonly TabItem[];
        density: Density;
        selectedClass: string;
        stacked: boolean;
        centerActive: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        grow: boolean;
        hideSlider: boolean;
        alignTabs: "center" | "end" | "start" | "title";
        fixedTabs: boolean;
    } & {
        max?: number | undefined;
        height?: string | number | undefined;
        color?: string | undefined;
        class?: any;
        mobileBreakpoint?: number | DisplayBreakpoint | undefined;
        modelValue?: any;
        bgColor?: string | undefined;
        showArrows?: string | boolean | undefined;
        sliderColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            [x: `tab.${string}`]: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            [x: `item.${string}`]: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            tab?: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            item?: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            window?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            [x: `tab.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            [x: `item.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            tab?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            item?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            window?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        [x: `v-slot:tab.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        [x: `v-slot:item.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:tab"?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        "v-slot:item"?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        "v-slot:window"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((v: unknown) => any) | undefined;
    }, {
        symbol: any;
        height: string | number;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        mobile: boolean | null;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        mandatory: boolean | "force";
        modelValue: any;
        items: readonly TabItem[];
        density: Density;
        selectedClass: string;
        stacked: boolean;
        centerActive: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        grow: boolean;
        hideSlider: boolean;
        alignTabs: "center" | "end" | "start" | "title";
        fixedTabs: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        [x: `tab.${string}`]: (arg: VTabsSlot) => vue.VNode[];
        [x: `item.${string}`]: (arg: VTabsSlot) => vue.VNode[];
        default: () => vue.VNode[];
        tab: (arg: VTabsSlot) => vue.VNode[];
        item: (arg: VTabsSlot) => vue.VNode[];
        window: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        symbol: any;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        mobile: boolean | null;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        mandatory: boolean | "force";
        items: readonly TabItem[];
        density: Density;
        selectedClass: string;
        stacked: boolean;
        centerActive: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        grow: boolean;
        hideSlider: boolean;
        alignTabs: "center" | "end" | "start" | "title";
        fixedTabs: boolean;
    } & {
        max?: number | undefined;
        height?: string | number | undefined;
        color?: string | undefined;
        class?: any;
        mobileBreakpoint?: number | DisplayBreakpoint | undefined;
        modelValue?: any;
        bgColor?: string | undefined;
        showArrows?: string | boolean | undefined;
        sliderColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            [x: `tab.${string}`]: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            [x: `item.${string}`]: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            tab?: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            item?: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            window?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            [x: `tab.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            [x: `item.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            tab?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            item?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
            window?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        [x: `v-slot:tab.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        [x: `v-slot:item.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:tab"?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        "v-slot:item"?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        "v-slot:window"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((v: unknown) => any) | undefined;
    }, {}, {}, {}, {}, {
        symbol: any;
        height: string | number;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        mobile: boolean | null;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        mandatory: boolean | "force";
        modelValue: any;
        items: readonly TabItem[];
        density: Density;
        selectedClass: string;
        stacked: boolean;
        centerActive: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        grow: boolean;
        hideSlider: boolean;
        alignTabs: "center" | "end" | "start" | "title";
        fixedTabs: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    symbol: any;
    direction: "horizontal" | "vertical";
    style: vue.StyleValue;
    mobile: boolean | null;
    disabled: boolean;
    multiple: boolean;
    tag: string;
    mandatory: boolean | "force";
    items: readonly TabItem[];
    density: Density;
    selectedClass: string;
    stacked: boolean;
    centerActive: boolean;
    nextIcon: IconValue;
    prevIcon: IconValue;
    grow: boolean;
    hideSlider: boolean;
    alignTabs: "center" | "end" | "start" | "title";
    fixedTabs: boolean;
} & {
    max?: number | undefined;
    height?: string | number | undefined;
    color?: string | undefined;
    class?: any;
    mobileBreakpoint?: number | DisplayBreakpoint | undefined;
    modelValue?: any;
    bgColor?: string | undefined;
    showArrows?: string | boolean | undefined;
    sliderColor?: string | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        [x: `tab.${string}`]: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        [x: `item.${string}`]: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        default?: (() => vue.VNodeChild) | undefined;
        tab?: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        item?: ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        window?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        [x: `tab.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        [x: `item.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        default?: false | (() => vue.VNodeChild) | undefined;
        tab?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        item?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
        window?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    [x: `v-slot:tab.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
    [x: `v-slot:item.${string}`]: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:tab"?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
    "v-slot:item"?: false | ((arg: VTabsSlot) => vue.VNodeChild) | undefined;
    "v-slot:window"?: false | (() => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((v: unknown) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (v: unknown) => true;
}, string, {
    symbol: any;
    height: string | number;
    direction: "horizontal" | "vertical";
    style: vue.StyleValue;
    mobile: boolean | null;
    disabled: boolean;
    multiple: boolean;
    tag: string;
    mandatory: boolean | "force";
    modelValue: any;
    items: readonly TabItem[];
    density: Density;
    selectedClass: string;
    stacked: boolean;
    centerActive: boolean;
    nextIcon: IconValue;
    prevIcon: IconValue;
    grow: boolean;
    hideSlider: boolean;
    alignTabs: "center" | "end" | "start" | "title";
    fixedTabs: boolean;
}, {}, string, vue.SlotsType<Partial<{
    [x: `tab.${string}`]: (arg: VTabsSlot) => vue.VNode[];
    [x: `item.${string}`]: (arg: VTabsSlot) => vue.VNode[];
    default: () => vue.VNode[];
    tab: (arg: VTabsSlot) => vue.VNode[];
    item: (arg: VTabsSlot) => vue.VNode[];
    window: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    modelValue: {
        type: null;
        default: undefined;
    };
    multiple: BooleanConstructor;
    mandatory: {
        type: PropType<boolean | "force">;
        default: NonNullable<boolean | "force">;
    };
    max: NumberConstructor;
    selectedClass: Omit<{
        type: PropType<string>;
        default: string;
    }, "type" | "default"> & {
        type: PropType<string>;
        default: string;
    };
    disabled: BooleanConstructor;
    mobile: Omit<{
        type: PropType<boolean | null>;
        default: boolean;
    }, "type" | "default"> & {
        type: PropType<boolean | null>;
        default: NonNullable<boolean | null> | null;
    };
    mobileBreakpoint: PropType<number | DisplayBreakpoint>;
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
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
    alignTabs: {
        type: PropType<"start" | "title" | "center" | "end">;
        default: string;
    };
    color: StringConstructor;
    fixedTabs: BooleanConstructor;
    items: {
        type: PropType<readonly TabItem[]>;
        default: () => never[];
    };
    stacked: BooleanConstructor;
    bgColor: StringConstructor;
    grow: BooleanConstructor;
    height: {
        type: (StringConstructor | NumberConstructor)[];
        default: undefined;
    };
    hideSlider: BooleanConstructor;
    sliderColor: StringConstructor;
}, vue.ExtractPropTypes<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    modelValue: {
        type: null;
        default: undefined;
    };
    multiple: BooleanConstructor;
    mandatory: {
        type: PropType<boolean | "force">;
        default: NonNullable<boolean | "force">;
    };
    max: NumberConstructor;
    selectedClass: Omit<{
        type: PropType<string>;
        default: string;
    }, "type" | "default"> & {
        type: PropType<string>;
        default: string;
    };
    disabled: BooleanConstructor;
    mobile: Omit<{
        type: PropType<boolean | null>;
        default: boolean;
    }, "type" | "default"> & {
        type: PropType<boolean | null>;
        default: NonNullable<boolean | null> | null;
    };
    mobileBreakpoint: PropType<number | DisplayBreakpoint>;
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
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
    alignTabs: {
        type: PropType<"start" | "title" | "center" | "end">;
        default: string;
    };
    color: StringConstructor;
    fixedTabs: BooleanConstructor;
    items: {
        type: PropType<readonly TabItem[]>;
        default: () => never[];
    };
    stacked: BooleanConstructor;
    bgColor: StringConstructor;
    grow: BooleanConstructor;
    height: {
        type: (StringConstructor | NumberConstructor)[];
        default: undefined;
    };
    hideSlider: BooleanConstructor;
    sliderColor: StringConstructor;
}>>;
type VTabs = InstanceType<typeof VTabs>;

declare const VTabsWindow: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        reverse: boolean;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        selectedClass: string;
    } & {
        class?: any;
        theme?: string | undefined;
        modelValue?: any;
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
        "onUpdate:modelValue"?: ((v: unknown) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (v: unknown) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        reverse: boolean;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        selectedClass: string;
    } & {
        class?: any;
        theme?: string | undefined;
        modelValue?: any;
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
        "onUpdate:modelValue"?: ((v: unknown) => any) | undefined;
    }, {
        reverse: boolean;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        selectedClass: string;
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
        reverse: boolean;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        selectedClass: string;
    } & {
        class?: any;
        theme?: string | undefined;
        modelValue?: any;
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
        "onUpdate:modelValue"?: ((v: unknown) => any) | undefined;
    }, {}, {}, {}, {}, {
        reverse: boolean;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        selectedClass: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    reverse: boolean;
    direction: "horizontal" | "vertical";
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    selectedClass: string;
} & {
    class?: any;
    theme?: string | undefined;
    modelValue?: any;
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
    "onUpdate:modelValue"?: ((v: unknown) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (v: unknown) => true;
}, string, {
    reverse: boolean;
    direction: "horizontal" | "vertical";
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    selectedClass: string;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    reverse: BooleanConstructor;
    direction: {
        type: vue.PropType<"horizontal" | "vertical">;
        default: string;
    };
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    disabled: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    modelValue: null;
    selectedClass: {
        type: StringConstructor;
        default: string;
    };
}, vue.ExtractPropTypes<{
    reverse: BooleanConstructor;
    direction: {
        type: vue.PropType<"horizontal" | "vertical">;
        default: string;
    };
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    disabled: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    modelValue: null;
    selectedClass: {
        type: StringConstructor;
        default: string;
    };
}>>;
type VTabsWindow = InstanceType<typeof VTabsWindow>;

declare const VTabsWindowItem: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
    } & {
        transition?: string | boolean | undefined;
        value?: any;
        class?: any;
        selectedClass?: string | undefined;
        reverseTransition?: string | boolean | undefined;
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
        disabled: boolean;
    } & {
        transition?: string | boolean | undefined;
        value?: any;
        class?: any;
        selectedClass?: string | undefined;
        reverseTransition?: string | boolean | undefined;
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
        transition: string | boolean;
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        reverseTransition: string | boolean;
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
        disabled: boolean;
    } & {
        transition?: string | boolean | undefined;
        value?: any;
        class?: any;
        selectedClass?: string | undefined;
        reverseTransition?: string | boolean | undefined;
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
        transition: string | boolean;
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        reverseTransition: string | boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    eager: boolean;
    disabled: boolean;
} & {
    transition?: string | boolean | undefined;
    value?: any;
    class?: any;
    selectedClass?: string | undefined;
    reverseTransition?: string | boolean | undefined;
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
    transition: string | boolean;
    style: vue.StyleValue;
    eager: boolean;
    disabled: boolean;
    reverseTransition: string | boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    eager: BooleanConstructor;
    value: null;
    disabled: BooleanConstructor;
    selectedClass: StringConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    reverseTransition: {
        type: (StringConstructor | BooleanConstructor)[];
        default: undefined;
    };
    transition: {
        type: (StringConstructor | BooleanConstructor)[];
        default: undefined;
    };
}, vue.ExtractPropTypes<{
    eager: BooleanConstructor;
    value: null;
    disabled: BooleanConstructor;
    selectedClass: StringConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    reverseTransition: {
        type: (StringConstructor | BooleanConstructor)[];
        default: undefined;
    };
    transition: {
        type: (StringConstructor | BooleanConstructor)[];
        default: undefined;
    };
}>>;
type VTabsWindowItem = InstanceType<typeof VTabsWindowItem>;

export { VTab, VTabs, VTabsWindow, VTabsWindowItem };
