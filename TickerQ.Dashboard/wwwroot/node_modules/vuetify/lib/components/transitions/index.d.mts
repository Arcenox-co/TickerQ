import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

declare const VDialogTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{} & {
        target?: HTMLElement | [x: number, y: number] | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, () => JSX.Element, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {} & {
        target?: HTMLElement | [x: number, y: number] | undefined;
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
    }, {} & {
        target?: HTMLElement | [x: number, y: number] | undefined;
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
} & vue.ComponentOptionsBase<{} & {
    target?: HTMLElement | [x: number, y: number] | undefined;
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
    target: PropType<HTMLElement | [x: number, y: number]>;
}, vue.ExtractPropTypes<{
    target: PropType<HTMLElement | [x: number, y: number]>;
}>>;
type VDialogTransition = InstanceType<typeof VDialogTransition>;

declare const VFabTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VFabTransition = InstanceType<typeof VFabTransition>;
declare const VDialogBottomTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VDialogBottomTransition = InstanceType<typeof VDialogBottomTransition>;
declare const VDialogTopTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VDialogTopTransition = InstanceType<typeof VDialogTopTransition>;
declare const VFadeTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VFadeTransition = InstanceType<typeof VFadeTransition>;
declare const VScaleTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VScaleTransition = InstanceType<typeof VScaleTransition>;
declare const VScrollXTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VScrollXTransition = InstanceType<typeof VScrollXTransition>;
declare const VScrollXReverseTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VScrollXReverseTransition = InstanceType<typeof VScrollXReverseTransition>;
declare const VScrollYTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VScrollYTransition = InstanceType<typeof VScrollYTransition>;
declare const VScrollYReverseTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VScrollYReverseTransition = InstanceType<typeof VScrollYReverseTransition>;
declare const VSlideXTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VSlideXTransition = InstanceType<typeof VSlideXTransition>;
declare const VSlideXReverseTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VSlideXReverseTransition = InstanceType<typeof VSlideXReverseTransition>;
declare const VSlideYTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VSlideYTransition = InstanceType<typeof VSlideYTransition>;
declare const VSlideYReverseTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
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
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    } & {} & {
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
    }>, {}, {}, {}, {
        origin: string | undefined;
        disabled: boolean;
        group: boolean;
        mode: string | undefined;
        hideOnLeave: boolean;
        leaveAbsolute: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    origin: string | undefined;
    disabled: boolean;
    group: boolean;
    mode: string | undefined;
    hideOnLeave: boolean;
    leaveAbsolute: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    group: BooleanConstructor;
    hideOnLeave: BooleanConstructor;
    leaveAbsolute: BooleanConstructor;
    mode: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
    origin: {
        type: vue.PropType<string | undefined>;
        default: string | undefined;
    };
}>>;
type VSlideYReverseTransition = InstanceType<typeof VSlideYReverseTransition>;
declare const VExpandTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        disabled: boolean;
        group: boolean;
        mode: "default" | "in-out" | "out-in";
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        disabled: boolean;
        group: boolean;
        mode: "default" | "in-out" | "out-in";
    } & {} & {
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
        group: boolean;
        mode: "default" | "in-out" | "out-in";
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
        group: boolean;
        mode: "default" | "in-out" | "out-in";
    } & {} & {
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
    }>, {}, {}, {}, {
        disabled: boolean;
        group: boolean;
        mode: "default" | "in-out" | "out-in";
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    disabled: boolean;
    group: boolean;
    mode: "default" | "in-out" | "out-in";
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    disabled: boolean;
    group: boolean;
    mode: "default" | "in-out" | "out-in";
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    mode: {
        type: vue.PropType<"in-out" | "out-in" | "default">;
        default: string;
    };
    disabled: BooleanConstructor;
    group: BooleanConstructor;
}, vue.ExtractPropTypes<{
    mode: {
        type: vue.PropType<"in-out" | "out-in" | "default">;
        default: string;
    };
    disabled: BooleanConstructor;
    group: BooleanConstructor;
}>>;
type VExpandTransition = InstanceType<typeof VExpandTransition>;
declare const VExpandXTransition: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        disabled: boolean;
        group: boolean;
        mode: "default" | "in-out" | "out-in";
    } & {} & {
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
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        disabled: boolean;
        group: boolean;
        mode: "default" | "in-out" | "out-in";
    } & {} & {
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
        group: boolean;
        mode: "default" | "in-out" | "out-in";
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
        group: boolean;
        mode: "default" | "in-out" | "out-in";
    } & {} & {
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
    }>, {}, {}, {}, {
        disabled: boolean;
        group: boolean;
        mode: "default" | "in-out" | "out-in";
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    disabled: boolean;
    group: boolean;
    mode: "default" | "in-out" | "out-in";
} & {} & {
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
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    disabled: boolean;
    group: boolean;
    mode: "default" | "in-out" | "out-in";
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    mode: {
        type: vue.PropType<"in-out" | "out-in" | "default">;
        default: string;
    };
    disabled: BooleanConstructor;
    group: BooleanConstructor;
}, vue.ExtractPropTypes<{
    mode: {
        type: vue.PropType<"in-out" | "out-in" | "default">;
        default: string;
    };
    disabled: BooleanConstructor;
    group: BooleanConstructor;
}>>;
type VExpandXTransition = InstanceType<typeof VExpandXTransition>;

export { VDialogBottomTransition, VDialogTopTransition, VDialogTransition, VExpandTransition, VExpandXTransition, VFabTransition, VFadeTransition, VScaleTransition, VScrollXReverseTransition, VScrollXTransition, VScrollYReverseTransition, VScrollYTransition, VSlideXReverseTransition, VSlideXTransition, VSlideYReverseTransition, VSlideYTransition };
