import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, ComponentPublicInstance, Ref, EffectScope } from 'vue';

type ClassValue = any;

declare const block: readonly ["top", "bottom"];
declare const inline: readonly ["start", "end", "left", "right"];
type Tblock = typeof block[number];
type Tinline = typeof inline[number];
type Anchor = Tblock | Tinline | 'center' | 'center center' | `${Tblock} ${Tinline | 'center'}` | `${Tinline} ${Tblock | 'center'}`;

declare class Box {
    x: number;
    y: number;
    width: number;
    height: number;
    constructor({ x, y, width, height }: {
        x: number;
        y: number;
        width: number;
        height: number;
    });
    get top(): number;
    get bottom(): number;
    get left(): number;
    get right(): number;
}

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type TemplateRef = {
    (target: Element | ComponentPublicInstance | null): void;
    value: HTMLElement | ComponentPublicInstance | null | undefined;
    readonly el: HTMLElement | undefined;
};

interface ScrollStrategyData {
    root: Ref<HTMLElement | undefined>;
    contentEl: Ref<HTMLElement | undefined>;
    targetEl: Ref<HTMLElement | undefined>;
    isActive: Ref<boolean>;
    updateLocation: Ref<((e: Event) => void) | undefined>;
}
type ScrollStrategyFn = (data: ScrollStrategyData, props: StrategyProps$1, scope: EffectScope) => void;
declare const scrollStrategies: {
    none: null;
    close: typeof closeScrollStrategy;
    block: typeof blockScrollStrategy;
    reposition: typeof repositionScrollStrategy;
};
interface StrategyProps$1 {
    scrollStrategy: keyof typeof scrollStrategies | ScrollStrategyFn;
    contained: boolean | undefined;
}
declare function closeScrollStrategy(data: ScrollStrategyData): void;
declare function blockScrollStrategy(data: ScrollStrategyData, props: StrategyProps$1): void;
declare function repositionScrollStrategy(data: ScrollStrategyData, props: StrategyProps$1, scope: EffectScope): void;

interface LocationStrategyData {
    contentEl: Ref<HTMLElement | undefined>;
    target: Ref<HTMLElement | [x: number, y: number] | undefined>;
    isActive: Ref<boolean>;
    isRtl: Ref<boolean>;
}
type LocationStrategyFn = (data: LocationStrategyData, props: StrategyProps, contentStyles: Ref<Record<string, string>>) => undefined | {
    updateLocation: (e?: Event) => void;
};
declare const locationStrategies: {
    static: typeof staticLocationStrategy;
    connected: typeof connectedLocationStrategy;
};
interface StrategyProps {
    locationStrategy: keyof typeof locationStrategies | LocationStrategyFn;
    location: Anchor;
    origin: Anchor | 'auto' | 'overlap';
    offset?: number | string | number[];
    maxHeight?: number | string;
    maxWidth?: number | string;
    minHeight?: number | string;
    minWidth?: number | string;
}
declare function staticLocationStrategy(): void;
declare function connectedLocationStrategy(data: LocationStrategyData, props: StrategyProps, contentStyles: Ref<Record<string, string>>): {
    updateLocation: () => {
        available: {
            x: number;
            y: number;
        };
        contentBox: Box;
    } | undefined;
};

declare const VSpeedDial: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        offset: string | number | number[] | undefined;
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
        minWidth: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        }) | {
            component: vue.Component;
        };
        zIndex: string | number;
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        persistent: boolean;
        modelValue: boolean;
        locationStrategy: "connected" | "static" | ((data: LocationStrategyData, props: StrategyProps, contentStyles: vue.Ref<Record<string, string>>) => undefined | {
            updateLocation: (e?: Event) => void;
        });
        scrollStrategy: "none" | "block" | "close" | ((data: ScrollStrategyData, props: StrategyProps$1, scope: vue.EffectScope) => void) | "reposition";
        closeDelay: string | number;
        openDelay: string | number;
        activatorProps: Record<string, any>;
        openOnHover: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        submenu: boolean;
    } & {
        id?: string | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        opacity?: string | number | undefined;
        target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
        class?: any;
        theme?: string | undefined;
        contentClass?: any;
        activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
        openOnClick?: boolean | undefined;
        openOnFocus?: boolean | undefined;
        contentProps?: any;
        attach?: string | boolean | Element | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNodeChild) | undefined;
            activator?: ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNodeChild) | undefined;
            activator?: false | ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNodeChild) | undefined;
        "v-slot:activator"?: false | ((arg: {
            isActive: boolean;
            props: Record<string, any>;
            targetRef: TemplateRef;
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (value: boolean) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        offset: string | number | number[] | undefined;
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
        minWidth: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        }) | {
            component: vue.Component;
        };
        zIndex: string | number;
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        persistent: boolean;
        modelValue: boolean;
        locationStrategy: "connected" | "static" | ((data: LocationStrategyData, props: StrategyProps, contentStyles: vue.Ref<Record<string, string>>) => undefined | {
            updateLocation: (e?: Event) => void;
        });
        scrollStrategy: "none" | "block" | "close" | ((data: ScrollStrategyData, props: StrategyProps$1, scope: vue.EffectScope) => void) | "reposition";
        closeDelay: string | number;
        openDelay: string | number;
        activatorProps: Record<string, any>;
        openOnHover: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        submenu: boolean;
    } & {
        id?: string | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        opacity?: string | number | undefined;
        target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
        class?: any;
        theme?: string | undefined;
        contentClass?: any;
        activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
        openOnClick?: boolean | undefined;
        openOnFocus?: boolean | undefined;
        contentProps?: any;
        attach?: string | boolean | Element | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNodeChild) | undefined;
            activator?: ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNodeChild) | undefined;
            activator?: false | ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNodeChild) | undefined;
        "v-slot:activator"?: false | ((arg: {
            isActive: boolean;
            props: Record<string, any>;
            targetRef: TemplateRef;
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
    }, {
        offset: string | number | number[] | undefined;
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
        minWidth: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        }) | {
            component: vue.Component;
        };
        zIndex: string | number;
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        persistent: boolean;
        modelValue: boolean;
        locationStrategy: "connected" | "static" | ((data: LocationStrategyData, props: StrategyProps, contentStyles: vue.Ref<Record<string, string>>) => undefined | {
            updateLocation: (e?: Event) => void;
        });
        scrollStrategy: "none" | "block" | "close" | ((data: ScrollStrategyData, props: StrategyProps$1, scope: vue.EffectScope) => void) | "reposition";
        closeDelay: string | number;
        openDelay: string | number;
        activatorProps: Record<string, any>;
        openOnClick: boolean;
        openOnHover: boolean;
        openOnFocus: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        submenu: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNode[];
        activator: (arg: {
            isActive: boolean;
            props: Record<string, any>;
            targetRef: TemplateRef;
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        offset: string | number | number[] | undefined;
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
        minWidth: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        }) | {
            component: vue.Component;
        };
        zIndex: string | number;
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        persistent: boolean;
        modelValue: boolean;
        locationStrategy: "connected" | "static" | ((data: LocationStrategyData, props: StrategyProps, contentStyles: vue.Ref<Record<string, string>>) => undefined | {
            updateLocation: (e?: Event) => void;
        });
        scrollStrategy: "none" | "block" | "close" | ((data: ScrollStrategyData, props: StrategyProps$1, scope: vue.EffectScope) => void) | "reposition";
        closeDelay: string | number;
        openDelay: string | number;
        activatorProps: Record<string, any>;
        openOnHover: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        submenu: boolean;
    } & {
        id?: string | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        opacity?: string | number | undefined;
        target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
        class?: any;
        theme?: string | undefined;
        contentClass?: any;
        activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
        openOnClick?: boolean | undefined;
        openOnFocus?: boolean | undefined;
        contentProps?: any;
        attach?: string | boolean | Element | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNodeChild) | undefined;
            activator?: ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNodeChild) | undefined;
            activator?: false | ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNodeChild) | undefined;
        "v-slot:activator"?: false | ((arg: {
            isActive: boolean;
            props: Record<string, any>;
            targetRef: TemplateRef;
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
    }, {}, {}, {}, {}, {
        offset: string | number | number[] | undefined;
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
        minWidth: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        }) | {
            component: vue.Component;
        };
        zIndex: string | number;
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        persistent: boolean;
        modelValue: boolean;
        locationStrategy: "connected" | "static" | ((data: LocationStrategyData, props: StrategyProps, contentStyles: vue.Ref<Record<string, string>>) => undefined | {
            updateLocation: (e?: Event) => void;
        });
        scrollStrategy: "none" | "block" | "close" | ((data: ScrollStrategyData, props: StrategyProps$1, scope: vue.EffectScope) => void) | "reposition";
        closeDelay: string | number;
        openDelay: string | number;
        activatorProps: Record<string, any>;
        openOnClick: boolean;
        openOnHover: boolean;
        openOnFocus: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        submenu: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    offset: string | number | number[] | undefined;
    location: Anchor | undefined;
    origin: "auto" | Anchor | "overlap";
    minWidth: string | number;
    transition: string | boolean | (vue.TransitionProps & {
        component?: vue.Component;
    }) | {
        component: vue.Component;
    };
    zIndex: string | number;
    style: vue.StyleValue;
    eager: boolean;
    disabled: boolean;
    persistent: boolean;
    modelValue: boolean;
    locationStrategy: "connected" | "static" | ((data: LocationStrategyData, props: StrategyProps, contentStyles: vue.Ref<Record<string, string>>) => undefined | {
        updateLocation: (e?: Event) => void;
    });
    scrollStrategy: "none" | "block" | "close" | ((data: ScrollStrategyData, props: StrategyProps$1, scope: vue.EffectScope) => void) | "reposition";
    closeDelay: string | number;
    openDelay: string | number;
    activatorProps: Record<string, any>;
    openOnHover: boolean;
    closeOnContentClick: boolean;
    closeOnBack: boolean;
    contained: boolean;
    noClickAnimation: boolean;
    scrim: string | boolean;
    submenu: boolean;
} & {
    id?: string | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    opacity?: string | number | undefined;
    target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
    class?: any;
    theme?: string | undefined;
    contentClass?: any;
    activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
    openOnClick?: boolean | undefined;
    openOnFocus?: boolean | undefined;
    contentProps?: any;
    attach?: string | boolean | Element | undefined;
} & {
    $children?: vue.VNodeChild | {
        default?: ((arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNodeChild) | undefined;
        activator?: ((arg: {
            isActive: boolean;
            props: Record<string, any>;
            targetRef: TemplateRef;
        }) => vue.VNodeChild) | undefined;
    } | ((arg: {
        isActive: vue.Ref<boolean>;
    }) => vue.VNodeChild);
    'v-slots'?: {
        default?: false | ((arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNodeChild) | undefined;
        activator?: false | ((arg: {
            isActive: boolean;
            props: Record<string, any>;
            targetRef: TemplateRef;
        }) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | ((arg: {
        isActive: vue.Ref<boolean>;
    }) => vue.VNodeChild) | undefined;
    "v-slot:activator"?: false | ((arg: {
        isActive: boolean;
        props: Record<string, any>;
        targetRef: TemplateRef;
    }) => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (value: boolean) => true;
}, string, {
    offset: string | number | number[] | undefined;
    location: Anchor | undefined;
    origin: "auto" | Anchor | "overlap";
    minWidth: string | number;
    transition: string | boolean | (vue.TransitionProps & {
        component?: vue.Component;
    }) | {
        component: vue.Component;
    };
    zIndex: string | number;
    style: vue.StyleValue;
    eager: boolean;
    disabled: boolean;
    persistent: boolean;
    modelValue: boolean;
    locationStrategy: "connected" | "static" | ((data: LocationStrategyData, props: StrategyProps, contentStyles: vue.Ref<Record<string, string>>) => undefined | {
        updateLocation: (e?: Event) => void;
    });
    scrollStrategy: "none" | "block" | "close" | ((data: ScrollStrategyData, props: StrategyProps$1, scope: vue.EffectScope) => void) | "reposition";
    closeDelay: string | number;
    openDelay: string | number;
    activatorProps: Record<string, any>;
    openOnClick: boolean;
    openOnHover: boolean;
    openOnFocus: boolean;
    closeOnContentClick: boolean;
    closeOnBack: boolean;
    contained: boolean;
    noClickAnimation: boolean;
    scrim: string | boolean;
    submenu: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: {
        isActive: vue.Ref<boolean>;
    }) => vue.VNode[];
    activator: (arg: {
        isActive: boolean;
        props: Record<string, any>;
        targetRef: TemplateRef;
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    offset: {
        type: vue.PropType<string | number | number[] | undefined>;
        default: NonNullable<string | number | number[] | undefined>;
    };
    location: Omit<Omit<{
        type: vue.PropType<StrategyProps["location"]>;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<Anchor | undefined>;
        default: NonNullable<Anchor> | undefined;
    }, "type" | "default"> & {
        type: vue.PropType<Anchor | undefined>;
        default: NonNullable<Anchor | undefined>;
    };
    origin: {
        type: vue.PropType<StrategyProps["origin"]>;
        default: string;
    };
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    opacity: (StringConstructor | NumberConstructor)[];
    transition: Omit<Omit<{
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        }) | {
            component: vue.Component;
        }>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })> | {
            component: vue.Component;
        };
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        }) | {
            component: vue.Component;
        }>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        }) | {
            component: vue.Component;
        }>;
    };
    zIndex: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    target: vue.PropType<Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined>;
    eager: BooleanConstructor;
    disabled: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    theme: StringConstructor;
    persistent: BooleanConstructor;
    modelValue: BooleanConstructor;
    contentClass: null;
    activator: vue.PropType<Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined>;
    locationStrategy: Omit<{
        type: vue.PropType<StrategyProps["locationStrategy"]>;
        default: string;
        validator: (val: any) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<"connected" | "static" | ((data: LocationStrategyData, props: StrategyProps, contentStyles: vue.Ref<Record<string, string>>) => undefined | {
            updateLocation: (e?: Event) => void;
        })>;
        default: NonNullable<"connected" | "static" | ((data: LocationStrategyData, props: StrategyProps, contentStyles: vue.Ref<Record<string, string>>) => undefined | {
            updateLocation: (e?: Event) => void;
        })>;
    };
    scrollStrategy: Omit<{
        type: vue.PropType<StrategyProps$1["scrollStrategy"]>;
        default: string;
        validator: (val: any) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<"none" | "block" | "close" | ((data: ScrollStrategyData, props: StrategyProps$1, scope: vue.EffectScope) => void) | "reposition">;
        default: NonNullable<"none" | "block" | "close" | ((data: ScrollStrategyData, props: StrategyProps$1, scope: vue.EffectScope) => void) | "reposition">;
    };
    closeDelay: Omit<{
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    }, "type" | "default"> & {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    openDelay: Omit<{
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    }, "type" | "default"> & {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    activatorProps: {
        type: vue.PropType<Record<string, any>>;
        default: () => {};
    };
    openOnClick: {
        type: BooleanConstructor;
        default: undefined;
    };
    openOnHover: BooleanConstructor;
    openOnFocus: {
        type: BooleanConstructor;
        default: undefined;
    };
    closeOnContentClick: {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    closeOnBack: {
        type: BooleanConstructor;
        default: boolean;
    };
    contained: BooleanConstructor;
    contentProps: null;
    noClickAnimation: BooleanConstructor;
    scrim: Omit<{
        type: (StringConstructor | BooleanConstructor)[];
        default: boolean;
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean>;
        default: NonNullable<string | boolean>;
    };
    attach: vue.PropType<boolean | string | Element>;
    id: StringConstructor;
    submenu: BooleanConstructor;
}, vue.ExtractPropTypes<{
    offset: {
        type: vue.PropType<string | number | number[] | undefined>;
        default: NonNullable<string | number | number[] | undefined>;
    };
    location: Omit<Omit<{
        type: vue.PropType<StrategyProps["location"]>;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<Anchor | undefined>;
        default: NonNullable<Anchor> | undefined;
    }, "type" | "default"> & {
        type: vue.PropType<Anchor | undefined>;
        default: NonNullable<Anchor | undefined>;
    };
    origin: {
        type: vue.PropType<StrategyProps["origin"]>;
        default: string;
    };
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    opacity: (StringConstructor | NumberConstructor)[];
    transition: Omit<Omit<{
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        }) | {
            component: vue.Component;
        }>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })> | {
            component: vue.Component;
        };
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        }) | {
            component: vue.Component;
        }>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        }) | {
            component: vue.Component;
        }>;
    };
    zIndex: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    target: vue.PropType<Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined>;
    eager: BooleanConstructor;
    disabled: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    theme: StringConstructor;
    persistent: BooleanConstructor;
    modelValue: BooleanConstructor;
    contentClass: null;
    activator: vue.PropType<Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined>;
    locationStrategy: Omit<{
        type: vue.PropType<StrategyProps["locationStrategy"]>;
        default: string;
        validator: (val: any) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<"connected" | "static" | ((data: LocationStrategyData, props: StrategyProps, contentStyles: vue.Ref<Record<string, string>>) => undefined | {
            updateLocation: (e?: Event) => void;
        })>;
        default: NonNullable<"connected" | "static" | ((data: LocationStrategyData, props: StrategyProps, contentStyles: vue.Ref<Record<string, string>>) => undefined | {
            updateLocation: (e?: Event) => void;
        })>;
    };
    scrollStrategy: Omit<{
        type: vue.PropType<StrategyProps$1["scrollStrategy"]>;
        default: string;
        validator: (val: any) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<"none" | "block" | "close" | ((data: ScrollStrategyData, props: StrategyProps$1, scope: vue.EffectScope) => void) | "reposition">;
        default: NonNullable<"none" | "block" | "close" | ((data: ScrollStrategyData, props: StrategyProps$1, scope: vue.EffectScope) => void) | "reposition">;
    };
    closeDelay: Omit<{
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    }, "type" | "default"> & {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    openDelay: Omit<{
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    }, "type" | "default"> & {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    activatorProps: {
        type: vue.PropType<Record<string, any>>;
        default: () => {};
    };
    openOnClick: {
        type: BooleanConstructor;
        default: undefined;
    };
    openOnHover: BooleanConstructor;
    openOnFocus: {
        type: BooleanConstructor;
        default: undefined;
    };
    closeOnContentClick: {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    closeOnBack: {
        type: BooleanConstructor;
        default: boolean;
    };
    contained: BooleanConstructor;
    contentProps: null;
    noClickAnimation: BooleanConstructor;
    scrim: Omit<{
        type: (StringConstructor | BooleanConstructor)[];
        default: boolean;
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean>;
        default: NonNullable<string | boolean>;
    };
    attach: vue.PropType<boolean | string | Element>;
    id: StringConstructor;
    submenu: BooleanConstructor;
}>>;
type VSpeedDial = InstanceType<typeof VSpeedDial>;

export { VSpeedDial };
