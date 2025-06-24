import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, Component } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

type VCounterSlot = {
    counter: string;
    max: string | number | undefined;
    value: string | number | undefined;
};
declare const VCounter: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        active: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
        };
        value: string | number;
        style: vue.StyleValue;
        disabled: boolean;
    } & {
        max?: string | number | undefined;
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
        } | ((arg: VCounterSlot) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        active: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
        };
        value: string | number;
        style: vue.StyleValue;
        disabled: boolean;
    } & {
        max?: string | number | undefined;
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
        } | ((arg: VCounterSlot) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
    }, {
        active: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
        };
        value: string | number;
        style: vue.StyleValue;
        disabled: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: VCounterSlot) => vue.VNode[];
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
        };
        value: string | number;
        style: vue.StyleValue;
        disabled: boolean;
    } & {
        max?: string | number | undefined;
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
        } | ((arg: VCounterSlot) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        active: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
        };
        value: string | number;
        style: vue.StyleValue;
        disabled: boolean;
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
    };
    value: string | number;
    style: vue.StyleValue;
    disabled: boolean;
} & {
    max?: string | number | undefined;
    class?: any;
} & {
    $children?: vue.VNodeChild | {
        default?: ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
    } | ((arg: VCounterSlot) => vue.VNodeChild);
    'v-slots'?: {
        default?: false | ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | ((arg: VCounterSlot) => vue.VNodeChild) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    active: boolean;
    transition: string | boolean | (vue.TransitionProps & {
        component?: Component;
    }) | {
        component: Component;
    };
    value: string | number;
    style: vue.StyleValue;
    disabled: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: VCounterSlot) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    transition: Omit<{
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
        }>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: Component;
        })> | {
            component: Component;
        };
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    active: BooleanConstructor;
    disabled: BooleanConstructor;
    max: (StringConstructor | NumberConstructor)[];
    value: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
}, vue.ExtractPropTypes<{
    transition: Omit<{
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
        }>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: Component;
        })> | {
            component: Component;
        };
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    active: BooleanConstructor;
    disabled: BooleanConstructor;
    max: (StringConstructor | NumberConstructor)[];
    value: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
}>>;
type VCounter = InstanceType<typeof VCounter>;

export { VCounter };
