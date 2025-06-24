import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, VNodeChild, VNode, PropType, ComponentPublicInstance, FunctionalComponent, ComponentInternalInstance, Ref, ComputedRef } from 'vue';

type SlotsToProps<U extends RawSlots, T = MakeInternalSlots<U>> = {
    $children?: (VNodeChild | (T extends {
        default: infer V;
    } ? V : {}) | {
        [K in keyof T]?: T[K];
    });
    'v-slots'?: {
        [K in keyof T]?: T[K] | false;
    };
} & {
    [K in keyof T as `v-slot:${K & string}`]?: T[K] | false;
};
type RawSlots = Record<string, unknown>;
type Slot<T> = [T] extends [never] ? () => VNodeChild : (arg: T) => VNodeChild;
type VueSlot<T> = [T] extends [never] ? () => VNode[] : (arg: T) => VNode[];
type MakeInternalSlots<T extends RawSlots> = {
    [K in keyof T]: Slot<T[K]>;
};
type MakeSlots<T extends RawSlots> = {
    [K in keyof T]: VueSlot<T[K]>;
};
type GenericProps<Props, Slots extends Record<string, unknown>> = {
    $props: Props & SlotsToProps<Slots>;
    $slots: MakeSlots<Slots>;
};
interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

interface TouchHandlers {
    start?: (wrapperEvent: {
        originalEvent: TouchEvent;
    } & TouchData) => void;
    end?: (wrapperEvent: {
        originalEvent: TouchEvent;
    } & TouchData) => void;
    move?: (wrapperEvent: {
        originalEvent: TouchEvent;
    } & TouchData) => void;
    left?: (wrapper: TouchData) => void;
    right?: (wrapper: TouchData) => void;
    up?: (wrapper: TouchData) => void;
    down?: (wrapper: TouchData) => void;
}
interface TouchData {
    touchstartX: number;
    touchstartY: number;
    touchmoveX: number;
    touchmoveY: number;
    touchendX: number;
    touchendY: number;
    offsetX: number;
    offsetY: number;
}

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

interface GroupItem {
    id: number;
    value: Ref<unknown>;
    disabled: Ref<boolean | undefined>;
    useIndexAsValue?: boolean;
}
interface GroupProvide {
    register: (item: GroupItem, cmp: ComponentInternalInstance) => void;
    unregister: (id: number) => void;
    select: (id: number, value: boolean) => void;
    selected: Ref<Readonly<number[]>>;
    isSelected: (id: number) => boolean;
    prev: () => void;
    next: () => void;
    selectedClass: Ref<string | undefined>;
    items: ComputedRef<{
        id: number;
        value: unknown;
        disabled: boolean | undefined;
    }[]>;
    disabled: Ref<boolean | undefined>;
    getItemIndex: (value: unknown) => number;
}

type VWindowSlots = {
    default: {
        group: GroupProvide;
    };
    additional: {
        group: GroupProvide;
    };
    prev: {
        props: ControlProps;
    };
    next: {
        props: ControlProps;
    };
};
type ControlProps = {
    icon: IconValue;
    class: string;
    onClick: () => void;
    'aria-label': string;
};

type VCarouselSlots = VWindowSlots & {
    item: {
        props: Record<string, any>;
        item: {
            id: number;
            value: unknown;
            disabled: boolean | undefined;
        };
    };
};
declare const VCarousel: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        reverse: boolean;
        interval: string | number;
        height: string | number;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        cycle: boolean;
        mandatory: boolean | "force";
        selectedClass: string;
        nextIcon: IconValue;
        prevIcon: IconValue;
        showArrows: string | boolean;
        continuous: boolean;
        delimiterIcon: IconValue;
        hideDelimiters: boolean;
        hideDelimiterBackground: boolean;
    } & {
        progress?: string | boolean | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        touch?: boolean | TouchHandlers | undefined;
        verticalDelimiters?: boolean | "left" | "right" | undefined;
    } & {}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
        'update:modelValue': (value: any) => true;
    }, "$children" | "v-slots" | "v-slot:default" | "modelValue" | "v-slot:additional" | "update:modelValue" | "v-slot:next" | "v-slot:prev" | "v-slot:item">, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        reverse: boolean;
        interval: string | number;
        height: string | number;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        cycle: boolean;
        mandatory: boolean | "force";
        selectedClass: string;
        nextIcon: IconValue;
        prevIcon: IconValue;
        showArrows: string | boolean;
        continuous: boolean;
        delimiterIcon: IconValue;
        hideDelimiters: boolean;
        hideDelimiterBackground: boolean;
    } & {
        progress?: string | boolean | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        touch?: boolean | TouchHandlers | undefined;
        verticalDelimiters?: boolean | "left" | "right" | undefined;
    } & {}, {
        reverse: boolean;
        interval: string | number;
        height: string | number;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        cycle: boolean;
        mandatory: boolean | "force";
        touch: boolean | TouchHandlers;
        selectedClass: string;
        nextIcon: IconValue;
        prevIcon: IconValue;
        showArrows: string | boolean;
        continuous: boolean;
        delimiterIcon: IconValue;
        hideDelimiters: boolean;
        hideDelimiterBackground: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: {
            group: GroupProvide;
        }) => vue.VNode[];
        additional: (arg: {
            group: GroupProvide;
        }) => vue.VNode[];
        prev: (arg: {
            props: {
                icon: IconValue;
                class: string;
                onClick: () => void;
                'aria-label': string;
            };
        }) => vue.VNode[];
        next: (arg: {
            props: {
                icon: IconValue;
                class: string;
                onClick: () => void;
                'aria-label': string;
            };
        }) => vue.VNode[];
        item: (arg: {
            props: Record<string, any>;
            item: {
                id: number;
                value: unknown;
                disabled: boolean | undefined;
            };
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        reverse: boolean;
        interval: string | number;
        height: string | number;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        cycle: boolean;
        mandatory: boolean | "force";
        selectedClass: string;
        nextIcon: IconValue;
        prevIcon: IconValue;
        showArrows: string | boolean;
        continuous: boolean;
        delimiterIcon: IconValue;
        hideDelimiters: boolean;
        hideDelimiterBackground: boolean;
    } & {
        progress?: string | boolean | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        touch?: boolean | TouchHandlers | undefined;
        verticalDelimiters?: boolean | "left" | "right" | undefined;
    } & {}, {}, {}, {}, {}, {
        reverse: boolean;
        interval: string | number;
        height: string | number;
        direction: "horizontal" | "vertical";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        cycle: boolean;
        mandatory: boolean | "force";
        touch: boolean | TouchHandlers;
        selectedClass: string;
        nextIcon: IconValue;
        prevIcon: IconValue;
        showArrows: string | boolean;
        continuous: boolean;
        delimiterIcon: IconValue;
        hideDelimiters: boolean;
        hideDelimiterBackground: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    reverse: boolean;
    interval: string | number;
    height: string | number;
    direction: "horizontal" | "vertical";
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    cycle: boolean;
    mandatory: boolean | "force";
    selectedClass: string;
    nextIcon: IconValue;
    prevIcon: IconValue;
    showArrows: string | boolean;
    continuous: boolean;
    delimiterIcon: IconValue;
    hideDelimiters: boolean;
    hideDelimiterBackground: boolean;
} & {
    progress?: string | boolean | undefined;
    color?: string | undefined;
    class?: any;
    theme?: string | undefined;
    touch?: boolean | TouchHandlers | undefined;
    verticalDelimiters?: boolean | "left" | "right" | undefined;
} & {}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
    'update:modelValue': (value: any) => true;
}, "$children" | "v-slots" | "v-slot:default" | "modelValue" | "v-slot:additional" | "update:modelValue" | "v-slot:next" | "v-slot:prev" | "v-slot:item">, string, {
    reverse: boolean;
    interval: string | number;
    height: string | number;
    direction: "horizontal" | "vertical";
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    cycle: boolean;
    mandatory: boolean | "force";
    touch: boolean | TouchHandlers;
    selectedClass: string;
    nextIcon: IconValue;
    prevIcon: IconValue;
    showArrows: string | boolean;
    continuous: boolean;
    delimiterIcon: IconValue;
    hideDelimiters: boolean;
    hideDelimiterBackground: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: {
        group: GroupProvide;
    }) => vue.VNode[];
    additional: (arg: {
        group: GroupProvide;
    }) => vue.VNode[];
    prev: (arg: {
        props: {
            icon: IconValue;
            class: string;
            onClick: () => void;
            'aria-label': string;
        };
    }) => vue.VNode[];
    next: (arg: {
        props: {
            icon: IconValue;
            class: string;
            onClick: () => void;
            'aria-label': string;
        };
    }) => vue.VNode[];
    item: (arg: {
        props: Record<string, any>;
        item: {
            id: number;
            value: unknown;
            disabled: boolean | undefined;
        };
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & (new <T>(props: {
    modelValue?: T;
    "onUpdate:modelValue"?: (value: T) => void;
}, slots: VCarouselSlots) => GenericProps<typeof props, typeof slots>) & FilterPropsOptions<{
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    continuous: {
        type: PropType<boolean>;
        default: boolean;
    };
    nextIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    prevIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    reverse: BooleanConstructor;
    showArrows: Omit<{
        type: (StringConstructor | BooleanConstructor)[];
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: PropType<string | boolean>;
        default: NonNullable<string | boolean>;
    };
    touch: {
        type: PropType<boolean | TouchHandlers>;
        default: undefined;
    };
    direction: {
        type: PropType<"horizontal" | "vertical">;
        default: string;
    };
    modelValue: null;
    disabled: BooleanConstructor;
    selectedClass: {
        type: StringConstructor;
        default: string;
    };
    mandatory: Omit<{
        type: PropType<boolean | "force">;
        default: "force";
    }, "type" | "default"> & {
        type: PropType<boolean | "force">;
        default: NonNullable<boolean | "force">;
    };
    color: StringConstructor;
    cycle: BooleanConstructor;
    delimiterIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    height: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    hideDelimiters: BooleanConstructor;
    hideDelimiterBackground: BooleanConstructor;
    interval: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
        validator: (value: string | number) => boolean;
    };
    progress: (StringConstructor | BooleanConstructor)[];
    verticalDelimiters: PropType<boolean | "left" | "right">;
}, vue.ExtractPropTypes<{
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    continuous: {
        type: PropType<boolean>;
        default: boolean;
    };
    nextIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    prevIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    reverse: BooleanConstructor;
    showArrows: Omit<{
        type: (StringConstructor | BooleanConstructor)[];
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: PropType<string | boolean>;
        default: NonNullable<string | boolean>;
    };
    touch: {
        type: PropType<boolean | TouchHandlers>;
        default: undefined;
    };
    direction: {
        type: PropType<"horizontal" | "vertical">;
        default: string;
    };
    modelValue: null;
    disabled: BooleanConstructor;
    selectedClass: {
        type: StringConstructor;
        default: string;
    };
    mandatory: Omit<{
        type: PropType<boolean | "force">;
        default: "force";
    }, "type" | "default"> & {
        type: PropType<boolean | "force">;
        default: NonNullable<boolean | "force">;
    };
    color: StringConstructor;
    cycle: BooleanConstructor;
    delimiterIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    height: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    hideDelimiters: BooleanConstructor;
    hideDelimiterBackground: BooleanConstructor;
    interval: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
        validator: (value: string | number) => boolean;
    };
    progress: (StringConstructor | BooleanConstructor)[];
    verticalDelimiters: PropType<boolean | "left" | "right">;
}>>;
type VCarousel = InstanceType<typeof VCarousel>;

interface srcObject {
    src?: string;
    srcset?: string;
    lazySrc?: string;
    aspect: number;
}

declare const VCarouselItem: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        inline: boolean;
        absolute: boolean;
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        options: IntersectionObserverInit;
        cover: boolean;
        src: string | srcObject;
        tile: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        aspectRatio?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: string | undefined;
        transition?: string | boolean | undefined;
        value?: any;
        draggable?: boolean | "false" | "true" | undefined;
        class?: any;
        alt?: string | undefined;
        referrerpolicy?: "origin" | "same-origin" | "no-referrer" | "no-referrer-when-downgrade" | "origin-when-cross-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url" | undefined;
        crossorigin?: "" | "anonymous" | "use-credentials" | undefined;
        sizes?: string | undefined;
        srcset?: string | undefined;
        rounded?: string | number | boolean | undefined;
        contentClass?: any;
        gradient?: string | undefined;
        lazySrc?: string | undefined;
        selectedClass?: string | undefined;
        reverseTransition?: string | boolean | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            placeholder?: (() => vue.VNodeChild) | undefined;
            error?: (() => vue.VNodeChild) | undefined;
            sources?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            placeholder?: false | (() => vue.VNodeChild) | undefined;
            error?: false | (() => vue.VNodeChild) | undefined;
            sources?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:placeholder"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:error"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:sources"?: false | (() => vue.VNodeChild) | undefined;
    }, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        inline: boolean;
        absolute: boolean;
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        options: IntersectionObserverInit;
        cover: boolean;
        src: string | srcObject;
        tile: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        aspectRatio?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: string | undefined;
        transition?: string | boolean | undefined;
        value?: any;
        draggable?: boolean | "false" | "true" | undefined;
        class?: any;
        alt?: string | undefined;
        referrerpolicy?: "origin" | "same-origin" | "no-referrer" | "no-referrer-when-downgrade" | "origin-when-cross-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url" | undefined;
        crossorigin?: "" | "anonymous" | "use-credentials" | undefined;
        sizes?: string | undefined;
        srcset?: string | undefined;
        rounded?: string | number | boolean | undefined;
        contentClass?: any;
        gradient?: string | undefined;
        lazySrc?: string | undefined;
        selectedClass?: string | undefined;
        reverseTransition?: string | boolean | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            placeholder?: (() => vue.VNodeChild) | undefined;
            error?: (() => vue.VNodeChild) | undefined;
            sources?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            placeholder?: false | (() => vue.VNodeChild) | undefined;
            error?: false | (() => vue.VNodeChild) | undefined;
            sources?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:placeholder"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:error"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:sources"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        inline: boolean;
        absolute: boolean;
        transition: string | boolean;
        style: vue.StyleValue;
        draggable: boolean | "false" | "true";
        eager: boolean;
        disabled: boolean;
        options: IntersectionObserverInit;
        cover: boolean;
        src: string | srcObject;
        rounded: string | number | boolean;
        tile: boolean;
        reverseTransition: string | boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        placeholder: () => vue.VNode[];
        error: () => vue.VNode[];
        sources: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        inline: boolean;
        absolute: boolean;
        style: vue.StyleValue;
        eager: boolean;
        disabled: boolean;
        options: IntersectionObserverInit;
        cover: boolean;
        src: string | srcObject;
        tile: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        aspectRatio?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: string | undefined;
        transition?: string | boolean | undefined;
        value?: any;
        draggable?: boolean | "false" | "true" | undefined;
        class?: any;
        alt?: string | undefined;
        referrerpolicy?: "origin" | "same-origin" | "no-referrer" | "no-referrer-when-downgrade" | "origin-when-cross-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url" | undefined;
        crossorigin?: "" | "anonymous" | "use-credentials" | undefined;
        sizes?: string | undefined;
        srcset?: string | undefined;
        rounded?: string | number | boolean | undefined;
        contentClass?: any;
        gradient?: string | undefined;
        lazySrc?: string | undefined;
        selectedClass?: string | undefined;
        reverseTransition?: string | boolean | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            placeholder?: (() => vue.VNodeChild) | undefined;
            error?: (() => vue.VNodeChild) | undefined;
            sources?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            placeholder?: false | (() => vue.VNodeChild) | undefined;
            error?: false | (() => vue.VNodeChild) | undefined;
            sources?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:placeholder"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:error"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:sources"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        inline: boolean;
        absolute: boolean;
        transition: string | boolean;
        style: vue.StyleValue;
        draggable: boolean | "false" | "true";
        eager: boolean;
        disabled: boolean;
        options: IntersectionObserverInit;
        cover: boolean;
        src: string | srcObject;
        rounded: string | number | boolean;
        tile: boolean;
        reverseTransition: string | boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    inline: boolean;
    absolute: boolean;
    style: vue.StyleValue;
    eager: boolean;
    disabled: boolean;
    options: IntersectionObserverInit;
    cover: boolean;
    src: string | srcObject;
    tile: boolean;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    aspectRatio?: string | number | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    position?: string | undefined;
    transition?: string | boolean | undefined;
    value?: any;
    draggable?: boolean | "false" | "true" | undefined;
    class?: any;
    alt?: string | undefined;
    referrerpolicy?: "origin" | "same-origin" | "no-referrer" | "no-referrer-when-downgrade" | "origin-when-cross-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url" | undefined;
    crossorigin?: "" | "anonymous" | "use-credentials" | undefined;
    sizes?: string | undefined;
    srcset?: string | undefined;
    rounded?: string | number | boolean | undefined;
    contentClass?: any;
    gradient?: string | undefined;
    lazySrc?: string | undefined;
    selectedClass?: string | undefined;
    reverseTransition?: string | boolean | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        placeholder?: (() => vue.VNodeChild) | undefined;
        error?: (() => vue.VNodeChild) | undefined;
        sources?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        placeholder?: false | (() => vue.VNodeChild) | undefined;
        error?: false | (() => vue.VNodeChild) | undefined;
        sources?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:placeholder"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:error"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:sources"?: false | (() => vue.VNodeChild) | undefined;
}, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    inline: boolean;
    absolute: boolean;
    transition: string | boolean;
    style: vue.StyleValue;
    draggable: boolean | "false" | "true";
    eager: boolean;
    disabled: boolean;
    options: IntersectionObserverInit;
    cover: boolean;
    src: string | srcObject;
    rounded: string | number | boolean;
    tile: boolean;
    reverseTransition: string | boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    placeholder: () => vue.VNode[];
    error: () => vue.VNode[];
    sources: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    eager: BooleanConstructor;
    value: null;
    disabled: BooleanConstructor;
    selectedClass: StringConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    reverseTransition: {
        type: (StringConstructor | BooleanConstructor)[];
        default: undefined;
    };
    transition: {
        type: (StringConstructor | BooleanConstructor)[];
        default: undefined;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    aspectRatio: (StringConstructor | NumberConstructor)[];
    contentClass: null;
    inline: BooleanConstructor;
    absolute: BooleanConstructor;
    alt: StringConstructor;
    cover: BooleanConstructor;
    color: StringConstructor;
    draggable: {
        type: vue.PropType<boolean | "true" | "false">;
        default: undefined;
    };
    gradient: StringConstructor;
    lazySrc: StringConstructor;
    options: {
        type: vue.PropType<IntersectionObserverInit>;
        default: () => {
            root: undefined;
            rootMargin: undefined;
            threshold: undefined;
        };
    };
    sizes: StringConstructor;
    src: {
        type: vue.PropType<string | srcObject>;
        default: string;
    };
    crossorigin: vue.PropType<"" | "anonymous" | "use-credentials">;
    referrerpolicy: vue.PropType<"no-referrer" | "no-referrer-when-downgrade" | "origin" | "origin-when-cross-origin" | "same-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url">;
    srcset: StringConstructor;
    position: StringConstructor;
}, vue.ExtractPropTypes<{
    eager: BooleanConstructor;
    value: null;
    disabled: BooleanConstructor;
    selectedClass: StringConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    reverseTransition: {
        type: (StringConstructor | BooleanConstructor)[];
        default: undefined;
    };
    transition: {
        type: (StringConstructor | BooleanConstructor)[];
        default: undefined;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    aspectRatio: (StringConstructor | NumberConstructor)[];
    contentClass: null;
    inline: BooleanConstructor;
    absolute: BooleanConstructor;
    alt: StringConstructor;
    cover: BooleanConstructor;
    color: StringConstructor;
    draggable: {
        type: vue.PropType<boolean | "true" | "false">;
        default: undefined;
    };
    gradient: StringConstructor;
    lazySrc: StringConstructor;
    options: {
        type: vue.PropType<IntersectionObserverInit>;
        default: () => {
            root: undefined;
            rootMargin: undefined;
            threshold: undefined;
        };
    };
    sizes: StringConstructor;
    src: {
        type: vue.PropType<string | srcObject>;
        default: string;
    };
    crossorigin: vue.PropType<"" | "anonymous" | "use-credentials">;
    referrerpolicy: vue.PropType<"no-referrer" | "no-referrer-when-downgrade" | "origin" | "origin-when-cross-origin" | "same-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url">;
    srcset: StringConstructor;
    position: StringConstructor;
}>>;
type VCarouselItem = InstanceType<typeof VCarouselItem>;

export { VCarousel, VCarouselItem };
