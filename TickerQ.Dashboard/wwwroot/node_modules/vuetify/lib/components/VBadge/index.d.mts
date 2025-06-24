import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType, ComponentPublicInstance, FunctionalComponent } from 'vue';

type ClassValue = any;

declare const block: readonly ["top", "bottom"];
declare const inline: readonly ["start", "end", "left", "right"];
type Tblock = typeof block[number];
type Tinline = typeof inline[number];
type Anchor = Tblock | Tinline | 'center' | 'center center' | `${Tblock} ${Tinline | 'center'}` | `${Tinline} ${Tblock | 'center'}`;

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

declare const VBadge: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        inline: boolean;
        location: Anchor | null;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        label: string;
        style: vue.StyleValue;
        tag: string;
        dot: boolean;
        modelValue: boolean;
        tile: boolean;
        floating: boolean;
        bordered: boolean;
    } & {
        max?: string | number | undefined;
        color?: string | undefined;
        content?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
        icon?: IconValue | undefined;
        rounded?: string | number | boolean | undefined;
        offsetX?: string | number | undefined;
        offsetY?: string | number | undefined;
        textColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            badge?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            badge?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:badge"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        inline: boolean;
        location: Anchor | null;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        label: string;
        style: vue.StyleValue;
        tag: string;
        dot: boolean;
        modelValue: boolean;
        tile: boolean;
        floating: boolean;
        bordered: boolean;
    } & {
        max?: string | number | undefined;
        color?: string | undefined;
        content?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
        icon?: IconValue | undefined;
        rounded?: string | number | boolean | undefined;
        offsetX?: string | number | undefined;
        offsetY?: string | number | undefined;
        textColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            badge?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            badge?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:badge"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        inline: boolean;
        location: Anchor | null;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        label: string;
        style: vue.StyleValue;
        tag: string;
        dot: boolean;
        modelValue: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        floating: boolean;
        bordered: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        badge: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        inline: boolean;
        location: Anchor | null;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        label: string;
        style: vue.StyleValue;
        tag: string;
        dot: boolean;
        modelValue: boolean;
        tile: boolean;
        floating: boolean;
        bordered: boolean;
    } & {
        max?: string | number | undefined;
        color?: string | undefined;
        content?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
        icon?: IconValue | undefined;
        rounded?: string | number | boolean | undefined;
        offsetX?: string | number | undefined;
        offsetY?: string | number | undefined;
        textColor?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            badge?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            badge?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:badge"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        inline: boolean;
        location: Anchor | null;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        label: string;
        style: vue.StyleValue;
        tag: string;
        dot: boolean;
        modelValue: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        floating: boolean;
        bordered: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    inline: boolean;
    location: Anchor | null;
    transition: string | boolean | (vue.TransitionProps & {
        component?: vue.Component;
    });
    label: string;
    style: vue.StyleValue;
    tag: string;
    dot: boolean;
    modelValue: boolean;
    tile: boolean;
    floating: boolean;
    bordered: boolean;
} & {
    max?: string | number | undefined;
    color?: string | undefined;
    content?: string | number | undefined;
    class?: any;
    theme?: string | undefined;
    icon?: IconValue | undefined;
    rounded?: string | number | boolean | undefined;
    offsetX?: string | number | undefined;
    offsetY?: string | number | undefined;
    textColor?: string | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        badge?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        badge?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:badge"?: false | (() => vue.VNodeChild) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    inline: boolean;
    location: Anchor | null;
    transition: string | boolean | (vue.TransitionProps & {
        component?: vue.Component;
    });
    label: string;
    style: vue.StyleValue;
    tag: string;
    dot: boolean;
    modelValue: boolean;
    rounded: string | number | boolean;
    tile: boolean;
    floating: boolean;
    bordered: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    badge: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    transition: Omit<{
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
    };
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    location: {
        type: vue.PropType<Anchor | null>;
        default: NonNullable<Anchor | null>;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    bordered: BooleanConstructor;
    color: StringConstructor;
    content: (StringConstructor | NumberConstructor)[];
    dot: BooleanConstructor;
    floating: BooleanConstructor;
    icon: vue.PropType<IconValue>;
    inline: BooleanConstructor;
    label: {
        type: StringConstructor;
        default: string;
    };
    max: (StringConstructor | NumberConstructor)[];
    modelValue: {
        type: BooleanConstructor;
        default: boolean;
    };
    offsetX: (StringConstructor | NumberConstructor)[];
    offsetY: (StringConstructor | NumberConstructor)[];
    textColor: StringConstructor;
}, vue.ExtractPropTypes<{
    transition: Omit<{
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
    };
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    location: {
        type: vue.PropType<Anchor | null>;
        default: NonNullable<Anchor | null>;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    bordered: BooleanConstructor;
    color: StringConstructor;
    content: (StringConstructor | NumberConstructor)[];
    dot: BooleanConstructor;
    floating: BooleanConstructor;
    icon: vue.PropType<IconValue>;
    inline: BooleanConstructor;
    label: {
        type: StringConstructor;
        default: string;
    };
    max: (StringConstructor | NumberConstructor)[];
    modelValue: {
        type: BooleanConstructor;
        default: boolean;
    };
    offsetX: (StringConstructor | NumberConstructor)[];
    offsetY: (StringConstructor | NumberConstructor)[];
    textColor: StringConstructor;
}>>;
type VBadge = InstanceType<typeof VBadge>;

export { VBadge };
