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

declare const VEmptyState: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        size: string | number | undefined;
        justify: "center" | "end" | "start";
        textWidth: string | number;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        title?: string | undefined;
        image?: string | undefined;
        text?: string | undefined;
        class?: any;
        theme?: string | undefined;
        to?: string | undefined;
        icon?: IconValue | undefined;
        href?: string | undefined;
        bgColor?: string | undefined;
        actionText?: string | undefined;
        headline?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            actions?: ((arg: {
                props: {
                    onClick: (e: Event) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            headline?: (() => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            media?: (() => vue.VNodeChild) | undefined;
            text?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            actions?: false | ((arg: {
                props: {
                    onClick: (e: Event) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            headline?: false | (() => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            media?: false | (() => vue.VNodeChild) | undefined;
            text?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:actions"?: false | ((arg: {
            props: {
                onClick: (e: Event) => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:headline"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:media"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:text"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onClick:action"?: ((e: Event) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'click:action': (e: Event) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        size: string | number | undefined;
        justify: "center" | "end" | "start";
        textWidth: string | number;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        title?: string | undefined;
        image?: string | undefined;
        text?: string | undefined;
        class?: any;
        theme?: string | undefined;
        to?: string | undefined;
        icon?: IconValue | undefined;
        href?: string | undefined;
        bgColor?: string | undefined;
        actionText?: string | undefined;
        headline?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            actions?: ((arg: {
                props: {
                    onClick: (e: Event) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            headline?: (() => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            media?: (() => vue.VNodeChild) | undefined;
            text?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            actions?: false | ((arg: {
                props: {
                    onClick: (e: Event) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            headline?: false | (() => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            media?: false | (() => vue.VNodeChild) | undefined;
            text?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:actions"?: false | ((arg: {
            props: {
                onClick: (e: Event) => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:headline"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:media"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:text"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onClick:action"?: ((e: Event) => any) | undefined;
    }, {
        style: vue.StyleValue;
        size: string | number | undefined;
        justify: "center" | "end" | "start";
        textWidth: string | number;
    }, true, {}, vue.SlotsType<Partial<{
        actions: (arg: {
            props: {
                onClick: (e: Event) => void;
            };
        }) => vue.VNode[];
        default: () => vue.VNode[];
        headline: () => vue.VNode[];
        title: () => vue.VNode[];
        media: () => vue.VNode[];
        text: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        style: vue.StyleValue;
        size: string | number | undefined;
        justify: "center" | "end" | "start";
        textWidth: string | number;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        title?: string | undefined;
        image?: string | undefined;
        text?: string | undefined;
        class?: any;
        theme?: string | undefined;
        to?: string | undefined;
        icon?: IconValue | undefined;
        href?: string | undefined;
        bgColor?: string | undefined;
        actionText?: string | undefined;
        headline?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            actions?: ((arg: {
                props: {
                    onClick: (e: Event) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            headline?: (() => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            media?: (() => vue.VNodeChild) | undefined;
            text?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            actions?: false | ((arg: {
                props: {
                    onClick: (e: Event) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            headline?: false | (() => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            media?: false | (() => vue.VNodeChild) | undefined;
            text?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:actions"?: false | ((arg: {
            props: {
                onClick: (e: Event) => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:headline"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:media"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:text"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onClick:action"?: ((e: Event) => any) | undefined;
    }, {}, {}, {}, {}, {
        style: vue.StyleValue;
        size: string | number | undefined;
        justify: "center" | "end" | "start";
        textWidth: string | number;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    size: string | number | undefined;
    justify: "center" | "end" | "start";
    textWidth: string | number;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    title?: string | undefined;
    image?: string | undefined;
    text?: string | undefined;
    class?: any;
    theme?: string | undefined;
    to?: string | undefined;
    icon?: IconValue | undefined;
    href?: string | undefined;
    bgColor?: string | undefined;
    actionText?: string | undefined;
    headline?: string | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        actions?: ((arg: {
            props: {
                onClick: (e: Event) => void;
            };
        }) => vue.VNodeChild) | undefined;
        default?: (() => vue.VNodeChild) | undefined;
        headline?: (() => vue.VNodeChild) | undefined;
        title?: (() => vue.VNodeChild) | undefined;
        media?: (() => vue.VNodeChild) | undefined;
        text?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        actions?: false | ((arg: {
            props: {
                onClick: (e: Event) => void;
            };
        }) => vue.VNodeChild) | undefined;
        default?: false | (() => vue.VNodeChild) | undefined;
        headline?: false | (() => vue.VNodeChild) | undefined;
        title?: false | (() => vue.VNodeChild) | undefined;
        media?: false | (() => vue.VNodeChild) | undefined;
        text?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:actions"?: false | ((arg: {
        props: {
            onClick: (e: Event) => void;
        };
    }) => vue.VNodeChild) | undefined;
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:headline"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:media"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:text"?: false | (() => vue.VNodeChild) | undefined;
} & {
    "onClick:action"?: ((e: Event) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'click:action': (e: Event) => true;
}, string, {
    style: vue.StyleValue;
    size: string | number | undefined;
    justify: "center" | "end" | "start";
    textWidth: string | number;
}, {}, string, vue.SlotsType<Partial<{
    actions: (arg: {
        props: {
            onClick: (e: Event) => void;
        };
    }) => vue.VNode[];
    default: () => vue.VNode[];
    headline: () => vue.VNode[];
    title: () => vue.VNode[];
    media: () => vue.VNode[];
    text: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    theme: StringConstructor;
    size: Omit<{
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    }, "type" | "default"> & {
        type: PropType<string | number | undefined>;
        default: NonNullable<string | number> | undefined;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    actionText: StringConstructor;
    bgColor: StringConstructor;
    color: StringConstructor;
    icon: PropType<IconValue>;
    image: StringConstructor;
    justify: {
        type: PropType<"start" | "center" | "end">;
        default: string;
    };
    headline: StringConstructor;
    title: StringConstructor;
    text: StringConstructor;
    textWidth: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    href: StringConstructor;
    to: StringConstructor;
}, vue.ExtractPropTypes<{
    theme: StringConstructor;
    size: Omit<{
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    }, "type" | "default"> & {
        type: PropType<string | number | undefined>;
        default: NonNullable<string | number> | undefined;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    actionText: StringConstructor;
    bgColor: StringConstructor;
    color: StringConstructor;
    icon: PropType<IconValue>;
    image: StringConstructor;
    justify: {
        type: PropType<"start" | "center" | "end">;
        default: string;
    };
    headline: StringConstructor;
    title: StringConstructor;
    text: StringConstructor;
    textWidth: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    href: StringConstructor;
    to: StringConstructor;
}>>;
type VEmptyState = InstanceType<typeof VEmptyState>;

export { VEmptyState };
