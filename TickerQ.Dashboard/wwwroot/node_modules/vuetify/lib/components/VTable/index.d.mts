import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

type Density = null | 'default' | 'comfortable' | 'compact';

declare const VTable: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        tag: string;
        density: Density;
        hover: boolean;
        fixedHeader: boolean;
        fixedFooter: boolean;
    } & {
        height?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            top?: (() => vue.VNodeChild) | undefined;
            bottom?: (() => vue.VNodeChild) | undefined;
            wrapper?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            top?: false | (() => vue.VNodeChild) | undefined;
            bottom?: false | (() => vue.VNodeChild) | undefined;
            wrapper?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:top"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:bottom"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:wrapper"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        tag: string;
        density: Density;
        hover: boolean;
        fixedHeader: boolean;
        fixedFooter: boolean;
    } & {
        height?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            top?: (() => vue.VNodeChild) | undefined;
            bottom?: (() => vue.VNodeChild) | undefined;
            wrapper?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            top?: false | (() => vue.VNodeChild) | undefined;
            bottom?: false | (() => vue.VNodeChild) | undefined;
            wrapper?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:top"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:bottom"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:wrapper"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        style: vue.StyleValue;
        tag: string;
        density: Density;
        hover: boolean;
        fixedHeader: boolean;
        fixedFooter: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        top: () => vue.VNode[];
        bottom: () => vue.VNode[];
        wrapper: () => vue.VNode[];
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
        density: Density;
        hover: boolean;
        fixedHeader: boolean;
        fixedFooter: boolean;
    } & {
        height?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            top?: (() => vue.VNodeChild) | undefined;
            bottom?: (() => vue.VNodeChild) | undefined;
            wrapper?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            top?: false | (() => vue.VNodeChild) | undefined;
            bottom?: false | (() => vue.VNodeChild) | undefined;
            wrapper?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:top"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:bottom"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:wrapper"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        style: vue.StyleValue;
        tag: string;
        density: Density;
        hover: boolean;
        fixedHeader: boolean;
        fixedFooter: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    tag: string;
    density: Density;
    hover: boolean;
    fixedHeader: boolean;
    fixedFooter: boolean;
} & {
    height?: string | number | undefined;
    class?: any;
    theme?: string | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        top?: (() => vue.VNodeChild) | undefined;
        bottom?: (() => vue.VNodeChild) | undefined;
        wrapper?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        top?: false | (() => vue.VNodeChild) | undefined;
        bottom?: false | (() => vue.VNodeChild) | undefined;
        wrapper?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:top"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:bottom"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:wrapper"?: false | (() => vue.VNodeChild) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    style: vue.StyleValue;
    tag: string;
    density: Density;
    hover: boolean;
    fixedHeader: boolean;
    fixedFooter: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    top: () => vue.VNode[];
    bottom: () => vue.VNode[];
    wrapper: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    density: {
        type: vue.PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    fixedHeader: BooleanConstructor;
    fixedFooter: BooleanConstructor;
    height: (StringConstructor | NumberConstructor)[];
    hover: BooleanConstructor;
}, vue.ExtractPropTypes<{
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    density: {
        type: vue.PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    fixedHeader: BooleanConstructor;
    fixedFooter: BooleanConstructor;
    height: (StringConstructor | NumberConstructor)[];
    hover: BooleanConstructor;
}>>;
type VTable = InstanceType<typeof VTable>;

export { VTable };
