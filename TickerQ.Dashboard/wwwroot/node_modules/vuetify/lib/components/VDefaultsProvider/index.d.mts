import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type DefaultsInstance = undefined | {
    [key: string]: undefined | Record<string, unknown>;
    global?: Record<string, unknown>;
};
type DefaultsOptions = Partial<DefaultsInstance>;

declare const VDefaultsProvider: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        disabled: boolean;
        scoped: boolean;
    } & {
        reset?: string | number | undefined;
        root?: string | boolean | undefined;
        defaults?: DefaultsOptions;
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
    }>[] | undefined, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        disabled: boolean;
        scoped: boolean;
    } & {
        reset?: string | number | undefined;
        root?: string | boolean | undefined;
        defaults?: DefaultsOptions;
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
        disabled: boolean;
        scoped: boolean;
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
        disabled: boolean;
        scoped: boolean;
    } & {
        reset?: string | number | undefined;
        root?: string | boolean | undefined;
        defaults?: DefaultsOptions;
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
    }>[] | undefined, {}, {}, {}, {
        disabled: boolean;
        scoped: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    disabled: boolean;
    scoped: boolean;
} & {
    reset?: string | number | undefined;
    root?: string | boolean | undefined;
    defaults?: DefaultsOptions;
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
}>[] | undefined, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    disabled: boolean;
    scoped: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    defaults: PropType<DefaultsOptions>;
    disabled: BooleanConstructor;
    reset: (StringConstructor | NumberConstructor)[];
    root: (StringConstructor | BooleanConstructor)[];
    scoped: BooleanConstructor;
}, vue.ExtractPropTypes<{
    defaults: PropType<DefaultsOptions>;
    disabled: BooleanConstructor;
    reset: (StringConstructor | NumberConstructor)[];
    root: (StringConstructor | BooleanConstructor)[];
    scoped: BooleanConstructor;
}>>;
type VDefaultsProvider = InstanceType<typeof VDefaultsProvider>;

export { VDefaultsProvider };
