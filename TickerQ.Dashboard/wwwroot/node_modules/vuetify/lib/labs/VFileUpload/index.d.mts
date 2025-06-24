import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType, ComponentPublicInstance, FunctionalComponent, VNode, DirectiveBinding } from 'vue';
// @ts-ignore
import * as vue_router from 'vue-router';

type ClassValue = any;

type Density = null | 'default' | 'comfortable' | 'compact';

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

declare const VFileUpload: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        length: string | number;
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        icon: IconValue;
        modelValue: File | File[];
        tile: boolean;
        density: Density;
        scrim: string | boolean;
        clearable: boolean;
        showSize: boolean;
        browseText: string;
        dividerText: string;
        hideBrowse: boolean;
    } & {
        name?: string | undefined;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        opacity?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        subtitle?: string | undefined;
        thickness?: string | number | undefined;
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            browse?: ((arg: {
                props: {
                    onClick: (e: MouseEvent) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            icon?: (() => vue.VNodeChild) | undefined;
            input?: ((arg: {
                inputNode: VNode;
            }) => vue.VNodeChild) | undefined;
            item?: ((arg: {
                file: File;
                props: {
                    "onClick:remove": () => void;
                };
            }) => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            divider?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            browse?: false | ((arg: {
                props: {
                    onClick: (e: MouseEvent) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            icon?: false | (() => vue.VNodeChild) | undefined;
            input?: false | ((arg: {
                inputNode: VNode;
            }) => vue.VNodeChild) | undefined;
            item?: false | ((arg: {
                file: File;
                props: {
                    "onClick:remove": () => void;
                };
            }) => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            divider?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:browse"?: false | ((arg: {
            props: {
                onClick: (e: MouseEvent) => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:icon"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:input"?: false | ((arg: {
            inputNode: VNode;
        }) => vue.VNodeChild) | undefined;
        "v-slot:item"?: false | ((arg: {
            file: File;
            props: {
                "onClick:remove": () => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:divider"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((files: File[]) => any) | undefined;
    }, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (files: File[]) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        length: string | number;
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        icon: IconValue;
        modelValue: File | File[];
        tile: boolean;
        density: Density;
        scrim: string | boolean;
        clearable: boolean;
        showSize: boolean;
        browseText: string;
        dividerText: string;
        hideBrowse: boolean;
    } & {
        name?: string | undefined;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        opacity?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        subtitle?: string | undefined;
        thickness?: string | number | undefined;
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            browse?: ((arg: {
                props: {
                    onClick: (e: MouseEvent) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            icon?: (() => vue.VNodeChild) | undefined;
            input?: ((arg: {
                inputNode: VNode;
            }) => vue.VNodeChild) | undefined;
            item?: ((arg: {
                file: File;
                props: {
                    "onClick:remove": () => void;
                };
            }) => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            divider?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            browse?: false | ((arg: {
                props: {
                    onClick: (e: MouseEvent) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            icon?: false | (() => vue.VNodeChild) | undefined;
            input?: false | ((arg: {
                inputNode: VNode;
            }) => vue.VNodeChild) | undefined;
            item?: false | ((arg: {
                file: File;
                props: {
                    "onClick:remove": () => void;
                };
            }) => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            divider?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:browse"?: false | ((arg: {
            props: {
                onClick: (e: MouseEvent) => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:icon"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:input"?: false | ((arg: {
            inputNode: VNode;
        }) => vue.VNodeChild) | undefined;
        "v-slot:item"?: false | ((arg: {
            file: File;
            props: {
                "onClick:remove": () => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:divider"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((files: File[]) => any) | undefined;
    }, {
        length: string | number;
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        icon: IconValue;
        modelValue: File | File[];
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        scrim: string | boolean;
        clearable: boolean;
        showSize: boolean;
        browseText: string;
        dividerText: string;
        hideBrowse: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        browse: (arg: {
            props: {
                onClick: (e: MouseEvent) => void;
            };
        }) => VNode[];
        default: () => VNode[];
        icon: () => VNode[];
        input: (arg: {
            inputNode: VNode;
        }) => VNode[];
        item: (arg: {
            file: File;
            props: {
                "onClick:remove": () => void;
            };
        }) => VNode[];
        title: () => VNode[];
        divider: () => VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        length: string | number;
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        icon: IconValue;
        modelValue: File | File[];
        tile: boolean;
        density: Density;
        scrim: string | boolean;
        clearable: boolean;
        showSize: boolean;
        browseText: string;
        dividerText: string;
        hideBrowse: boolean;
    } & {
        name?: string | undefined;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        opacity?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        subtitle?: string | undefined;
        thickness?: string | number | undefined;
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            browse?: ((arg: {
                props: {
                    onClick: (e: MouseEvent) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            icon?: (() => vue.VNodeChild) | undefined;
            input?: ((arg: {
                inputNode: VNode;
            }) => vue.VNodeChild) | undefined;
            item?: ((arg: {
                file: File;
                props: {
                    "onClick:remove": () => void;
                };
            }) => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            divider?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            browse?: false | ((arg: {
                props: {
                    onClick: (e: MouseEvent) => void;
                };
            }) => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            icon?: false | (() => vue.VNodeChild) | undefined;
            input?: false | ((arg: {
                inputNode: VNode;
            }) => vue.VNodeChild) | undefined;
            item?: false | ((arg: {
                file: File;
                props: {
                    "onClick:remove": () => void;
                };
            }) => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            divider?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:browse"?: false | ((arg: {
            props: {
                onClick: (e: MouseEvent) => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:icon"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:input"?: false | ((arg: {
            inputNode: VNode;
        }) => vue.VNodeChild) | undefined;
        "v-slot:item"?: false | ((arg: {
            file: File;
            props: {
                "onClick:remove": () => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:divider"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((files: File[]) => any) | undefined;
    }, {}, {}, {}, {}, {
        length: string | number;
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        multiple: boolean;
        tag: string;
        icon: IconValue;
        modelValue: File | File[];
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        scrim: string | boolean;
        clearable: boolean;
        showSize: boolean;
        browseText: string;
        dividerText: string;
        hideBrowse: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    length: string | number;
    style: vue.StyleValue;
    title: string;
    disabled: boolean;
    multiple: boolean;
    tag: string;
    icon: IconValue;
    modelValue: File | File[];
    tile: boolean;
    density: Density;
    scrim: string | boolean;
    clearable: boolean;
    showSize: boolean;
    browseText: string;
    dividerText: string;
    hideBrowse: boolean;
} & {
    name?: string | undefined;
    location?: Anchor | null | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    border?: string | number | boolean | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    opacity?: string | number | undefined;
    position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
    class?: any;
    theme?: string | undefined;
    elevation?: string | number | undefined;
    rounded?: string | number | boolean | undefined;
    subtitle?: string | undefined;
    thickness?: string | number | undefined;
    closeDelay?: string | number | undefined;
    openDelay?: string | number | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        browse?: ((arg: {
            props: {
                onClick: (e: MouseEvent) => void;
            };
        }) => vue.VNodeChild) | undefined;
        default?: (() => vue.VNodeChild) | undefined;
        icon?: (() => vue.VNodeChild) | undefined;
        input?: ((arg: {
            inputNode: VNode;
        }) => vue.VNodeChild) | undefined;
        item?: ((arg: {
            file: File;
            props: {
                "onClick:remove": () => void;
            };
        }) => vue.VNodeChild) | undefined;
        title?: (() => vue.VNodeChild) | undefined;
        divider?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        browse?: false | ((arg: {
            props: {
                onClick: (e: MouseEvent) => void;
            };
        }) => vue.VNodeChild) | undefined;
        default?: false | (() => vue.VNodeChild) | undefined;
        icon?: false | (() => vue.VNodeChild) | undefined;
        input?: false | ((arg: {
            inputNode: VNode;
        }) => vue.VNodeChild) | undefined;
        item?: false | ((arg: {
            file: File;
            props: {
                "onClick:remove": () => void;
            };
        }) => vue.VNodeChild) | undefined;
        title?: false | (() => vue.VNodeChild) | undefined;
        divider?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:browse"?: false | ((arg: {
        props: {
            onClick: (e: MouseEvent) => void;
        };
    }) => vue.VNodeChild) | undefined;
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:icon"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:input"?: false | ((arg: {
        inputNode: VNode;
    }) => vue.VNodeChild) | undefined;
    "v-slot:item"?: false | ((arg: {
        file: File;
        props: {
            "onClick:remove": () => void;
        };
    }) => vue.VNodeChild) | undefined;
    "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:divider"?: false | (() => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((files: File[]) => any) | undefined;
}, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (files: File[]) => true;
}, string, {
    length: string | number;
    style: vue.StyleValue;
    title: string;
    disabled: boolean;
    multiple: boolean;
    tag: string;
    icon: IconValue;
    modelValue: File | File[];
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    scrim: string | boolean;
    clearable: boolean;
    showSize: boolean;
    browseText: string;
    dividerText: string;
    hideBrowse: boolean;
}, {}, string, vue.SlotsType<Partial<{
    browse: (arg: {
        props: {
            onClick: (e: MouseEvent) => void;
        };
    }) => VNode[];
    default: () => VNode[];
    icon: () => VNode[];
    input: (arg: {
        inputNode: VNode;
    }) => VNode[];
    item: (arg: {
        file: File;
        props: {
            "onClick:remove": () => void;
        };
    }) => VNode[];
    title: () => VNode[];
    divider: () => VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
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
    position: {
        type: PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    location: PropType<Anchor | null>;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
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
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    length: {
        type: PropType<string | number>;
        default: NonNullable<string | number>;
    };
    opacity: (StringConstructor | NumberConstructor)[];
    thickness: (StringConstructor | NumberConstructor)[];
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    closeDelay: (StringConstructor | NumberConstructor)[];
    openDelay: (StringConstructor | NumberConstructor)[];
    browseText: {
        type: StringConstructor;
        default: string;
    };
    dividerText: {
        type: StringConstructor;
        default: string;
    };
    title: {
        type: StringConstructor;
        default: string;
    };
    subtitle: StringConstructor;
    icon: {
        type: PropType<IconValue>;
        default: string;
    };
    modelValue: {
        type: PropType<File[] | File>;
        default: null;
        validator: (val: any) => boolean;
    };
    clearable: BooleanConstructor;
    disabled: BooleanConstructor;
    hideBrowse: BooleanConstructor;
    multiple: BooleanConstructor;
    scrim: {
        type: (StringConstructor | BooleanConstructor)[];
        default: boolean;
    };
    showSize: BooleanConstructor;
    name: StringConstructor;
}, vue.ExtractPropTypes<{
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
    position: {
        type: PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    location: PropType<Anchor | null>;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
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
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    length: {
        type: PropType<string | number>;
        default: NonNullable<string | number>;
    };
    opacity: (StringConstructor | NumberConstructor)[];
    thickness: (StringConstructor | NumberConstructor)[];
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    closeDelay: (StringConstructor | NumberConstructor)[];
    openDelay: (StringConstructor | NumberConstructor)[];
    browseText: {
        type: StringConstructor;
        default: string;
    };
    dividerText: {
        type: StringConstructor;
        default: string;
    };
    title: {
        type: StringConstructor;
        default: string;
    };
    subtitle: StringConstructor;
    icon: {
        type: PropType<IconValue>;
        default: string;
    };
    modelValue: {
        type: PropType<File[] | File>;
        default: null;
        validator: (val: any) => boolean;
    };
    clearable: BooleanConstructor;
    disabled: BooleanConstructor;
    hideBrowse: BooleanConstructor;
    multiple: BooleanConstructor;
    scrim: {
        type: (StringConstructor | BooleanConstructor)[];
        default: boolean;
    };
    showSize: BooleanConstructor;
    name: StringConstructor;
}>>;
type VFileUpload = InstanceType<typeof VFileUpload>;

declare const allowedVariants: readonly ["elevated", "flat", "tonal", "outlined", "text", "plain"];
type Variant = typeof allowedVariants[number];

interface RippleDirectiveBinding extends Omit<DirectiveBinding, 'modifiers' | 'value'> {
    value?: boolean | {
        class: string;
    };
    modifiers: {
        center?: boolean;
        circle?: boolean;
        stop?: boolean;
    };
}

type ListItemSlot = {
    isActive: boolean;
    isOpen: boolean;
    isSelected: boolean;
    isIndeterminate: boolean;
    select: (value: boolean) => void;
};
type ListItemTitleSlot = {
    title?: string | number;
};
type ListItemSubtitleSlot = {
    subtitle?: string | number;
};

declare const VFileUploadItem: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        replace: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        file: File;
        border: string | number | boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        clearable: boolean;
        showSize: boolean;
        fileIcon: string;
    } & {
        link?: boolean | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        value?: any;
        title?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
        to?: vue_router.RouteLocationRaw | undefined;
        onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
        onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        appendAvatar?: string | undefined;
        prependAvatar?: string | undefined;
        subtitle?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | ((arg: ListItemSlot) => vue.VNodeChild) | {
            clear?: ((arg: {
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
            prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            clear?: false | ((arg: {
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
            prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:clear"?: false | ((arg: {
            props: {
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    } & {
        onClick?: ((e: MouseEvent | KeyboardEvent) => any) | undefined;
        "onClick:remove"?: (() => any) | undefined;
    }, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'click:remove': () => true;
        click: (e: MouseEvent | KeyboardEvent) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        replace: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        file: File;
        border: string | number | boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        clearable: boolean;
        showSize: boolean;
        fileIcon: string;
    } & {
        link?: boolean | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        value?: any;
        title?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
        to?: vue_router.RouteLocationRaw | undefined;
        onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
        onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        appendAvatar?: string | undefined;
        prependAvatar?: string | undefined;
        subtitle?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | ((arg: ListItemSlot) => vue.VNodeChild) | {
            clear?: ((arg: {
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
            prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            clear?: false | ((arg: {
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
            prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:clear"?: false | ((arg: {
            props: {
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    } & {
        onClick?: ((e: MouseEvent | KeyboardEvent) => any) | undefined;
        "onClick:remove"?: (() => any) | undefined;
    }, {
        replace: boolean;
        link: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        file: File;
        active: boolean;
        border: string | number | boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        clearable: boolean;
        showSize: boolean;
        fileIcon: string;
    }, true, {}, vue.SlotsType<Partial<{
        clear: (arg: {
            props: {
                onClick: () => void;
            };
        }) => vue.VNode[];
        prepend: (arg: ListItemSlot) => vue.VNode[];
        append: (arg: ListItemSlot) => vue.VNode[];
        default: (arg: ListItemSlot) => vue.VNode[];
        title: (arg: ListItemTitleSlot) => vue.VNode[];
        subtitle: (arg: ListItemSubtitleSlot) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        replace: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        file: File;
        border: string | number | boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        clearable: boolean;
        showSize: boolean;
        fileIcon: string;
    } & {
        link?: boolean | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        value?: any;
        title?: string | number | undefined;
        class?: any;
        theme?: string | undefined;
        to?: vue_router.RouteLocationRaw | undefined;
        onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
        onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        appendAvatar?: string | undefined;
        prependAvatar?: string | undefined;
        subtitle?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | ((arg: ListItemSlot) => vue.VNodeChild) | {
            clear?: ((arg: {
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
            prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            clear?: false | ((arg: {
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
            prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:clear"?: false | ((arg: {
            props: {
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    } & {
        onClick?: ((e: MouseEvent | KeyboardEvent) => any) | undefined;
        "onClick:remove"?: (() => any) | undefined;
    }, {}, {}, {}, {}, {
        replace: boolean;
        link: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        file: File;
        active: boolean;
        border: string | number | boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
        clearable: boolean;
        showSize: boolean;
        fileIcon: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    replace: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    exact: boolean;
    file: File;
    border: string | number | boolean;
    nav: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    lines: false | "one" | "two" | "three";
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    slim: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
    clearable: boolean;
    showSize: boolean;
    fileIcon: string;
} & {
    link?: boolean | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    active?: boolean | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    value?: any;
    title?: string | number | undefined;
    class?: any;
    theme?: string | undefined;
    to?: vue_router.RouteLocationRaw | undefined;
    onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
    onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
    href?: string | undefined;
    elevation?: string | number | undefined;
    baseColor?: string | undefined;
    activeColor?: string | undefined;
    prependIcon?: IconValue | undefined;
    appendIcon?: IconValue | undefined;
    activeClass?: string | undefined;
    appendAvatar?: string | undefined;
    prependAvatar?: string | undefined;
    subtitle?: string | number | undefined;
} & {
    $children?: vue.VNodeChild | ((arg: ListItemSlot) => vue.VNodeChild) | {
        clear?: ((arg: {
            props: {
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
        prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        clear?: false | ((arg: {
            props: {
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
        prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:clear"?: false | ((arg: {
        props: {
            onClick: () => void;
        };
    }) => vue.VNodeChild) | undefined;
    "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
    "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
    "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
    "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
    "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
} & {
    onClick?: ((e: MouseEvent | KeyboardEvent) => any) | undefined;
    "onClick:remove"?: (() => any) | undefined;
}, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'click:remove': () => true;
    click: (e: MouseEvent | KeyboardEvent) => true;
}, string, {
    replace: boolean;
    link: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    exact: boolean;
    file: File;
    active: boolean;
    border: string | number | boolean;
    nav: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    lines: false | "one" | "two" | "three";
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    slim: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
    clearable: boolean;
    showSize: boolean;
    fileIcon: string;
}, {}, string, vue.SlotsType<Partial<{
    clear: (arg: {
        props: {
            onClick: () => void;
        };
    }) => vue.VNode[];
    prepend: (arg: ListItemSlot) => vue.VNode[];
    append: (arg: ListItemSlot) => vue.VNode[];
    default: (arg: ListItemSlot) => vue.VNode[];
    title: (arg: ListItemTitleSlot) => vue.VNode[];
    subtitle: (arg: ListItemSubtitleSlot) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    color: StringConstructor;
    variant: Omit<{
        type: PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    href: StringConstructor;
    replace: BooleanConstructor;
    to: PropType<vue_router.RouteLocationRaw>;
    exact: BooleanConstructor;
    rounded: Omit<{
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    }, "type" | "default"> & {
        type: PropType<string | number | boolean>;
        default: NonNullable<string | number | boolean>;
    };
    tile: BooleanConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    border: {
        type: PropType<string | number | boolean>;
        default: NonNullable<string | number | boolean>;
    };
    active: {
        type: BooleanConstructor;
        default: undefined;
    };
    activeClass: StringConstructor;
    activeColor: StringConstructor;
    appendAvatar: StringConstructor;
    appendIcon: PropType<IconValue>;
    baseColor: StringConstructor;
    disabled: BooleanConstructor;
    lines: {
        type: PropType<false | "one" | "two" | "three">;
        default: NonNullable<false | "one" | "two" | "three">;
    };
    link: {
        type: BooleanConstructor;
        default: undefined;
    };
    nav: BooleanConstructor;
    prependAvatar: StringConstructor;
    prependIcon: PropType<IconValue>;
    ripple: {
        type: PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    slim: BooleanConstructor;
    subtitle: (StringConstructor | NumberConstructor)[];
    title: (StringConstructor | NumberConstructor)[];
    value: null;
    onClick: PropType<(args_0: MouseEvent | KeyboardEvent) => void>;
    onClickOnce: PropType<(args_0: MouseEvent) => void>;
    clearable: BooleanConstructor;
    file: {
        type: PropType<File>;
        default: null;
    };
    fileIcon: {
        type: StringConstructor;
        default: string;
    };
    showSize: BooleanConstructor;
}, vue.ExtractPropTypes<{
    color: StringConstructor;
    variant: Omit<{
        type: PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    href: StringConstructor;
    replace: BooleanConstructor;
    to: PropType<vue_router.RouteLocationRaw>;
    exact: BooleanConstructor;
    rounded: Omit<{
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    }, "type" | "default"> & {
        type: PropType<string | number | boolean>;
        default: NonNullable<string | number | boolean>;
    };
    tile: BooleanConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    border: {
        type: PropType<string | number | boolean>;
        default: NonNullable<string | number | boolean>;
    };
    active: {
        type: BooleanConstructor;
        default: undefined;
    };
    activeClass: StringConstructor;
    activeColor: StringConstructor;
    appendAvatar: StringConstructor;
    appendIcon: PropType<IconValue>;
    baseColor: StringConstructor;
    disabled: BooleanConstructor;
    lines: {
        type: PropType<false | "one" | "two" | "three">;
        default: NonNullable<false | "one" | "two" | "three">;
    };
    link: {
        type: BooleanConstructor;
        default: undefined;
    };
    nav: BooleanConstructor;
    prependAvatar: StringConstructor;
    prependIcon: PropType<IconValue>;
    ripple: {
        type: PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    slim: BooleanConstructor;
    subtitle: (StringConstructor | NumberConstructor)[];
    title: (StringConstructor | NumberConstructor)[];
    value: null;
    onClick: PropType<(args_0: MouseEvent | KeyboardEvent) => void>;
    onClickOnce: PropType<(args_0: MouseEvent) => void>;
    clearable: BooleanConstructor;
    file: {
        type: PropType<File>;
        default: null;
    };
    fileIcon: {
        type: StringConstructor;
        default: string;
    };
    showSize: BooleanConstructor;
}>>;
type VFileUploadItem = InstanceType<typeof VFileUploadItem>;

export { VFileUpload, VFileUploadItem };
