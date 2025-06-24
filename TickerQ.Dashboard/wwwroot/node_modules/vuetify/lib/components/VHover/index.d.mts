import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

declare const VHover: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        disabled: boolean;
        modelValue: boolean;
    } & {
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                isHovering: boolean | null;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            isHovering: boolean | null;
            props: Record<string, unknown>;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                isHovering: boolean | null;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            isHovering: boolean | null;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>[] | undefined, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (value: boolean) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        disabled: boolean;
        modelValue: boolean;
    } & {
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                isHovering: boolean | null;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            isHovering: boolean | null;
            props: Record<string, unknown>;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                isHovering: boolean | null;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            isHovering: boolean | null;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
    }, {
        disabled: boolean;
        modelValue: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: {
            isHovering: boolean | null;
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
        disabled: boolean;
        modelValue: boolean;
    } & {
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                isHovering: boolean | null;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            isHovering: boolean | null;
            props: Record<string, unknown>;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                isHovering: boolean | null;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            isHovering: boolean | null;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>[] | undefined, {}, {}, {}, {
        disabled: boolean;
        modelValue: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    disabled: boolean;
    modelValue: boolean;
} & {
    closeDelay?: string | number | undefined;
    openDelay?: string | number | undefined;
} & {
    $children?: vue.VNodeChild | {
        default?: ((arg: {
            isHovering: boolean | null;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    } | ((arg: {
        isHovering: boolean | null;
        props: Record<string, unknown>;
    }) => vue.VNodeChild);
    'v-slots'?: {
        default?: false | ((arg: {
            isHovering: boolean | null;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | ((arg: {
        isHovering: boolean | null;
        props: Record<string, unknown>;
    }) => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
}, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
    [key: string]: any;
}>[] | undefined, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (value: boolean) => true;
}, string, {
    disabled: boolean;
    modelValue: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: {
        isHovering: boolean | null;
        props: Record<string, unknown>;
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    closeDelay: (StringConstructor | NumberConstructor)[];
    openDelay: (StringConstructor | NumberConstructor)[];
    disabled: BooleanConstructor;
    modelValue: {
        type: BooleanConstructor;
        default: null;
    };
}, vue.ExtractPropTypes<{
    closeDelay: (StringConstructor | NumberConstructor)[];
    openDelay: (StringConstructor | NumberConstructor)[];
    disabled: BooleanConstructor;
    modelValue: {
        type: BooleanConstructor;
        default: null;
    };
}>>;
type VHover = InstanceType<typeof VHover>;

export { VHover };
