import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, Component, PropType } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

type VMessageSlot = {
    message: string;
};
declare const VMessages: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        active: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            leaveAbsolute: boolean;
            group: boolean;
        };
        style: vue.StyleValue;
        messages: string | readonly string[];
    } & {
        color?: string | undefined;
        class?: any;
    } & {
        $children?: {} | vue.VNodeChild | {
            message?: ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            message?: false | ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:message"?: false | ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        active: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            leaveAbsolute: boolean;
            group: boolean;
        };
        style: vue.StyleValue;
        messages: string | readonly string[];
    } & {
        color?: string | undefined;
        class?: any;
    } & {
        $children?: {} | vue.VNodeChild | {
            message?: ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            message?: false | ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:message"?: false | ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
    }, {
        active: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            leaveAbsolute: boolean;
            group: boolean;
        };
        style: vue.StyleValue;
        messages: string | readonly string[];
    }, true, {}, vue.SlotsType<Partial<{
        message: (arg: VMessageSlot) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        active: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            leaveAbsolute: boolean;
            group: boolean;
        };
        style: vue.StyleValue;
        messages: string | readonly string[];
    } & {
        color?: string | undefined;
        class?: any;
    } & {
        $children?: {} | vue.VNodeChild | {
            message?: ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            message?: false | ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:message"?: false | ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        active: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            leaveAbsolute: boolean;
            group: boolean;
        };
        style: vue.StyleValue;
        messages: string | readonly string[];
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    active: boolean;
    transition: string | boolean | (vue.TransitionProps & {
        component?: Component;
    }) | {
        component: Component;
        leaveAbsolute: boolean;
        group: boolean;
    };
    style: vue.StyleValue;
    messages: string | readonly string[];
} & {
    color?: string | undefined;
    class?: any;
} & {
    $children?: {} | vue.VNodeChild | {
        message?: ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        message?: false | ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:message"?: false | ((arg: VMessageSlot) => vue.VNodeChild) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    active: boolean;
    transition: string | boolean | (vue.TransitionProps & {
        component?: Component;
    }) | {
        component: Component;
        leaveAbsolute: boolean;
        group: boolean;
    };
    style: vue.StyleValue;
    messages: string | readonly string[];
}, {}, string, vue.SlotsType<Partial<{
    message: (arg: VMessageSlot) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    transition: Omit<{
        type: PropType<string | boolean | (vue.TransitionProps & {
            component?: Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: PropType<string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            leaveAbsolute: boolean;
            group: boolean;
        }>;
        default: {
            component: Component;
            leaveAbsolute: boolean;
            group: boolean;
        } | NonNullable<string | boolean | (vue.TransitionProps & {
            component?: Component;
        })>;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    active: BooleanConstructor;
    color: StringConstructor;
    messages: {
        type: PropType<string | readonly string[]>;
        default: () => never[];
    };
}, vue.ExtractPropTypes<{
    transition: Omit<{
        type: PropType<string | boolean | (vue.TransitionProps & {
            component?: Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: PropType<string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            leaveAbsolute: boolean;
            group: boolean;
        }>;
        default: {
            component: Component;
            leaveAbsolute: boolean;
            group: boolean;
        } | NonNullable<string | boolean | (vue.TransitionProps & {
            component?: Component;
        })>;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    active: BooleanConstructor;
    color: StringConstructor;
    messages: {
        type: PropType<string | readonly string[]>;
        default: () => never[];
    };
}>>;
type VMessages = InstanceType<typeof VMessages>;

export { VMessages };
