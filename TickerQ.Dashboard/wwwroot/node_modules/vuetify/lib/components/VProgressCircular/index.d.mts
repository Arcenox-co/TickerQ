import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

declare const VProgressCircular: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        width: string | number;
        rotate: string | number;
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        modelValue: string | number;
    } & {
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        indeterminate?: boolean | "disable-shrink" | undefined;
        bgColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                value: number;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            value: number;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                value: number;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            value: number;
        }) => vue.VNodeChild) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        width: string | number;
        rotate: string | number;
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        modelValue: string | number;
    } & {
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        indeterminate?: boolean | "disable-shrink" | undefined;
        bgColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                value: number;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            value: number;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                value: number;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            value: number;
        }) => vue.VNodeChild) | undefined;
    }, {
        width: string | number;
        rotate: string | number;
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        modelValue: string | number;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: {
            value: number;
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        width: string | number;
        rotate: string | number;
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        modelValue: string | number;
    } & {
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        indeterminate?: boolean | "disable-shrink" | undefined;
        bgColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                value: number;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            value: number;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                value: number;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            value: number;
        }) => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        width: string | number;
        rotate: string | number;
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        modelValue: string | number;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    width: string | number;
    rotate: string | number;
    style: vue.StyleValue;
    size: string | number;
    tag: string;
    modelValue: string | number;
} & {
    color?: string | undefined;
    class?: any;
    theme?: string | undefined;
    indeterminate?: boolean | "disable-shrink" | undefined;
    bgColor?: string | undefined;
} & {
    $children?: vue.VNodeChild | {
        default?: ((arg: {
            value: number;
        }) => vue.VNodeChild) | undefined;
    } | ((arg: {
        value: number;
    }) => vue.VNodeChild);
    'v-slots'?: {
        default?: false | ((arg: {
            value: number;
        }) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | ((arg: {
        value: number;
    }) => vue.VNodeChild) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    width: string | number;
    rotate: string | number;
    style: vue.StyleValue;
    size: string | number;
    tag: string;
    modelValue: string | number;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: {
        value: number;
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    theme: StringConstructor;
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: PropType<string>;
        default: string;
    };
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    bgColor: StringConstructor;
    color: StringConstructor;
    indeterminate: PropType<boolean | "disable-shrink">;
    modelValue: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    rotate: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    width: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
}, vue.ExtractPropTypes<{
    theme: StringConstructor;
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: PropType<string>;
        default: string;
    };
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    bgColor: StringConstructor;
    color: StringConstructor;
    indeterminate: PropType<boolean | "disable-shrink">;
    modelValue: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    rotate: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    width: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
}>>;
type VProgressCircular = InstanceType<typeof VProgressCircular>;

export { VProgressCircular };
