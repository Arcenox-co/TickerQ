import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType, ComponentPublicInstance, FunctionalComponent } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;
declare const VComponentIcon: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        tag: string;
    } & {
        icon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, () => JSX.Element, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        tag: string;
    } & {
        icon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        tag: string;
    } & {
        icon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, () => JSX.Element, {}, {}, {}, {}>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    tag: string;
} & {
    icon?: IconValue | undefined;
} & {
    $children?: vue.VNodeChild | {
        default?: (() => vue.VNodeChild) | undefined;
    } | (() => vue.VNodeChild);
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
}, () => JSX.Element, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    icon: {
        type: PropType<IconValue>;
    };
    tag: {
        type: StringConstructor;
        required: true;
    };
}, vue.ExtractPropTypes<{
    icon: {
        type: PropType<IconValue>;
    };
    tag: {
        type: StringConstructor;
        required: true;
    };
}>>;
type VComponentIcon = InstanceType<typeof VComponentIcon>;
declare const VSvgIcon: {
    new (...args: any[]): vue.CreateComponentPublicInstance<Readonly<vue.ExtractPropTypes<{
        icon: {
            type: PropType<IconValue>;
        };
        tag: {
            type: StringConstructor;
            required: true;
        };
    }>>, () => JSX.Element, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {}, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & Readonly<vue.ExtractPropTypes<{
        icon: {
            type: PropType<IconValue>;
        };
        tag: {
            type: StringConstructor;
            required: true;
        };
    }>>, {}, true, {}, {}, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, Readonly<vue.ExtractPropTypes<{
        icon: {
            type: PropType<IconValue>;
        };
        tag: {
            type: StringConstructor;
            required: true;
        };
    }>>, () => JSX.Element, {}, {}, {}, {}>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<Readonly<vue.ExtractPropTypes<{
    icon: {
        type: PropType<IconValue>;
    };
    tag: {
        type: StringConstructor;
        required: true;
    };
}>>, () => JSX.Element, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {}, string, {}, {}, string, {}> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    icon: {
        type: PropType<IconValue>;
    };
    tag: {
        type: StringConstructor;
        required: true;
    };
}, vue.ExtractPropTypes<{
    icon: {
        type: PropType<IconValue>;
    };
    tag: {
        type: StringConstructor;
        required: true;
    };
}>>;
type VSvgIcon = InstanceType<typeof VSvgIcon>;
declare const VLigatureIcon: {
    new (...args: any[]): vue.CreateComponentPublicInstance<Readonly<vue.ExtractPropTypes<{
        icon: {
            type: PropType<IconValue>;
        };
        tag: {
            type: StringConstructor;
            required: true;
        };
    }>>, () => JSX.Element, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {}, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & Readonly<vue.ExtractPropTypes<{
        icon: {
            type: PropType<IconValue>;
        };
        tag: {
            type: StringConstructor;
            required: true;
        };
    }>>, {}, true, {}, {}, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, Readonly<vue.ExtractPropTypes<{
        icon: {
            type: PropType<IconValue>;
        };
        tag: {
            type: StringConstructor;
            required: true;
        };
    }>>, () => JSX.Element, {}, {}, {}, {}>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<Readonly<vue.ExtractPropTypes<{
    icon: {
        type: PropType<IconValue>;
    };
    tag: {
        type: StringConstructor;
        required: true;
    };
}>>, () => JSX.Element, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {}, string, {}, {}, string, {}> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    icon: {
        type: PropType<IconValue>;
    };
    tag: {
        type: StringConstructor;
        required: true;
    };
}, vue.ExtractPropTypes<{
    icon: {
        type: PropType<IconValue>;
    };
    tag: {
        type: StringConstructor;
        required: true;
    };
}>>;
type VLigatureIcon = InstanceType<typeof VLigatureIcon>;
declare const VClassIcon: {
    new (...args: any[]): vue.CreateComponentPublicInstance<Readonly<vue.ExtractPropTypes<{
        icon: {
            type: PropType<IconValue>;
        };
        tag: {
            type: StringConstructor;
            required: true;
        };
    }>>, () => JSX.Element, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {}, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & Readonly<vue.ExtractPropTypes<{
        icon: {
            type: PropType<IconValue>;
        };
        tag: {
            type: StringConstructor;
            required: true;
        };
    }>>, {}, true, {}, {}, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, Readonly<vue.ExtractPropTypes<{
        icon: {
            type: PropType<IconValue>;
        };
        tag: {
            type: StringConstructor;
            required: true;
        };
    }>>, () => JSX.Element, {}, {}, {}, {}>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<Readonly<vue.ExtractPropTypes<{
    icon: {
        type: PropType<IconValue>;
    };
    tag: {
        type: StringConstructor;
        required: true;
    };
}>>, () => JSX.Element, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {}, string, {}, {}, string, {}> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    icon: {
        type: PropType<IconValue>;
    };
    tag: {
        type: StringConstructor;
        required: true;
    };
}, vue.ExtractPropTypes<{
    icon: {
        type: PropType<IconValue>;
    };
    tag: {
        type: StringConstructor;
        required: true;
    };
}>>;
type VClassIcon = InstanceType<typeof VClassIcon>;

declare const VIcon: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        end: boolean;
        start: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        tag: string;
    } & {
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        icon?: IconValue | undefined;
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
        disabled: boolean;
        size: string | number;
        tag: string;
    } & {
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        icon?: IconValue | undefined;
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
        disabled: boolean;
        size: string | number;
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
        disabled: boolean;
        size: string | number;
        tag: string;
    } & {
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        icon?: IconValue | undefined;
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
        disabled: boolean;
        size: string | number;
        tag: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    end: boolean;
    start: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    size: string | number;
    tag: string;
} & {
    color?: string | undefined;
    class?: any;
    theme?: string | undefined;
    icon?: IconValue | undefined;
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
    disabled: boolean;
    size: string | number;
    tag: string;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    theme: StringConstructor;
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<string>;
        default: string;
    };
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    color: StringConstructor;
    disabled: BooleanConstructor;
    start: BooleanConstructor;
    end: BooleanConstructor;
    icon: vue.PropType<IconValue>;
}, vue.ExtractPropTypes<{
    theme: StringConstructor;
    tag: Omit<{
        type: StringConstructor;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<string>;
        default: string;
    };
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    color: StringConstructor;
    disabled: BooleanConstructor;
    start: BooleanConstructor;
    end: BooleanConstructor;
    icon: vue.PropType<IconValue>;
}>>;
type VIcon = InstanceType<typeof VIcon>;

export { VClassIcon, VComponentIcon, VIcon, VLigatureIcon, VSvgIcon };
