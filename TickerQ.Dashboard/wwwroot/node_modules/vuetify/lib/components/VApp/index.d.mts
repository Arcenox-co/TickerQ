import * as vue from 'vue';
import { Ref, DeepReadonly, ComponentPropsOptions, ExtractPropTypes } from 'vue';

interface InternalThemeDefinition {
    dark: boolean;
    colors: Colors;
    variables: Record<string, string | number>;
}
interface Colors extends BaseColors, OnColors {
    [key: string]: string;
}
interface BaseColors {
    background: string;
    surface: string;
    primary: string;
    secondary: string;
    success: string;
    warning: string;
    error: string;
    info: string;
}
interface OnColors {
    'on-background': string;
    'on-surface': string;
    'on-primary': string;
    'on-secondary': string;
    'on-success': string;
    'on-warning': string;
    'on-error': string;
    'on-info': string;
}
interface ThemeInstance {
    readonly isDisabled: boolean;
    readonly themes: Ref<Record<string, InternalThemeDefinition>>;
    readonly name: Readonly<Ref<string>>;
    readonly current: DeepReadonly<Ref<InternalThemeDefinition>>;
    readonly computedThemes: DeepReadonly<Ref<Record<string, InternalThemeDefinition>>>;
    readonly themeClasses: Readonly<Ref<string | undefined>>;
    readonly styles: Readonly<Ref<string>>;
    readonly global: {
        readonly name: Ref<string>;
        readonly current: DeepReadonly<Ref<InternalThemeDefinition>>;
    };
}

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

type Position = 'top' | 'left' | 'right' | 'bottom';

declare const VApp: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        fullHeight: boolean;
    } & {
        class?: any;
        theme?: string | undefined;
        overlaps?: string[] | undefined;
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
        getLayoutItem: (id: string) => {
            size: number;
            position: Position;
            top: number;
            bottom: number;
            left: number;
            right: number;
            id: string;
        } | undefined;
        items: vue.ComputedRef<{
            size: number;
            position: Position;
            top: number;
            bottom: number;
            left: number;
            right: number;
            id: string;
        }[]>;
        theme: ThemeInstance;
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        fullHeight: boolean;
    } & {
        class?: any;
        theme?: string | undefined;
        overlaps?: string[] | undefined;
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
        fullHeight: boolean;
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
        fullHeight: boolean;
    } & {
        class?: any;
        theme?: string | undefined;
        overlaps?: string[] | undefined;
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
        getLayoutItem: (id: string) => {
            size: number;
            position: Position;
            top: number;
            bottom: number;
            left: number;
            right: number;
            id: string;
        } | undefined;
        items: vue.ComputedRef<{
            size: number;
            position: Position;
            top: number;
            bottom: number;
            left: number;
            right: number;
            id: string;
        }[]>;
        theme: ThemeInstance;
    }, {}, {}, {}, {
        style: vue.StyleValue;
        fullHeight: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    fullHeight: boolean;
} & {
    class?: any;
    theme?: string | undefined;
    overlaps?: string[] | undefined;
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
    getLayoutItem: (id: string) => {
        size: number;
        position: Position;
        top: number;
        bottom: number;
        left: number;
        right: number;
        id: string;
    } | undefined;
    items: vue.ComputedRef<{
        size: number;
        position: Position;
        top: number;
        bottom: number;
        left: number;
        right: number;
        id: string;
    }[]>;
    theme: ThemeInstance;
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    style: vue.StyleValue;
    fullHeight: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    theme: StringConstructor;
    overlaps: vue.Prop<string[]>;
    fullHeight: {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
}, vue.ExtractPropTypes<{
    theme: StringConstructor;
    overlaps: vue.Prop<string[]>;
    fullHeight: {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
}>>;
type VApp = InstanceType<typeof VApp>;

export { VApp };
