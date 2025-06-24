import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, VNodeChild, VNode, ComponentPublicInstance, PropType, FunctionalComponent, ComputedRef, Ref, EffectScope, Component, nextTick } from 'vue';

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

declare function deepEqual(a: any, b: any): boolean;
type SelectItemKey<T = Record<string, any>> = boolean | null | undefined | string | readonly (string | number)[] | ((item: T, fallback?: any) => any);
type EventProp<T extends any[] = any[], F = (...args: T) => void> = F;
declare const EventProp: <T extends any[] = any[]>() => PropType<EventProp<T>>;
type TemplateRef = {
    (target: Element | ComponentPublicInstance | null): void;
    value: HTMLElement | ComponentPublicInstance | null | undefined;
    readonly el: HTMLElement | undefined;
};

interface LoaderSlotProps {
    color: string | undefined;
    isActive: boolean;
}

type Density = null | 'default' | 'comfortable' | 'compact';

type ValidationResult = string | boolean;
type ValidationRule = ValidationResult | PromiseLike<ValidationResult> | ((value: any) => ValidationResult) | ((value: any) => PromiseLike<ValidationResult>);
type ValidateOnValue = 'blur' | 'input' | 'submit' | 'invalid-input';
type ValidateOn = ValidateOnValue | `${ValidateOnValue} lazy` | `${ValidateOnValue} eager` | `lazy ${ValidateOnValue}` | `eager ${ValidateOnValue}` | 'lazy' | 'eager';
interface ValidationProps {
    disabled: boolean | null;
    error: boolean;
    errorMessages: string | readonly string[] | null;
    focused: boolean;
    maxErrors: string | number;
    name: string | undefined;
    label: string | undefined;
    readonly: boolean | null;
    rules: readonly ValidationRule[];
    modelValue: any;
    'onUpdate:modelValue': EventProp | undefined;
    validateOn?: ValidateOn;
    validationValue: any;
}

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

type VMessageSlot = {
    message: string;
};

interface VInputSlot {
    id: ComputedRef<string>;
    messagesId: ComputedRef<string>;
    isDirty: ComputedRef<boolean>;
    isDisabled: ComputedRef<boolean>;
    isReadonly: ComputedRef<boolean>;
    isPristine: Ref<boolean>;
    isValid: ComputedRef<boolean | null>;
    isValidating: Ref<boolean>;
    reset: () => void;
    resetValidation: () => void;
    validate: () => void;
}
type VInputSlots = {
    default: VInputSlot;
    prepend: VInputSlot;
    append: VInputSlot;
    details: VInputSlot;
    message: VMessageSlot;
};

interface DefaultInputSlot {
    isActive: Ref<boolean>;
    isFocused: Ref<boolean>;
    controlRef: Ref<HTMLElement | undefined>;
    focus: () => void;
    blur: () => void;
}
interface VFieldSlot extends DefaultInputSlot {
    props: Record<string, unknown>;
}
type VFieldSlots = {
    clear: DefaultInputSlot & {
        props: Record<string, any>;
    };
    'prepend-inner': DefaultInputSlot;
    'append-inner': DefaultInputSlot;
    label: DefaultInputSlot & {
        label: string | undefined;
        props: Record<string, any>;
    };
    loader: LoaderSlotProps;
    default: VFieldSlot;
};

/**
 * - match without highlight
 * - single match (index), length already known
 * - single match (start, end)
 * - multiple matches (start, end), probably shouldn't overlap
 */
type FilterMatch = boolean | number | [number, number] | [number, number][];
type FilterFunction = (value: string, query: string, item?: InternalItem) => FilterMatch;
type FilterKeyFunctions = Record<string, FilterFunction>;
type FilterKeys = string | string[];
type FilterMode = 'some' | 'every' | 'union' | 'intersection';
interface InternalItem<T = any> {
    value: any;
    raw: T;
}

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

declare const allowedVariants: readonly ["elevated", "flat", "tonal", "outlined", "text", "plain"];
type Variant = typeof allowedVariants[number];

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
type VListItemSlots = {
    prepend: ListItemSlot;
    append: ListItemSlot;
    default: ListItemSlot;
    title: ListItemTitleSlot;
    subtitle: ListItemSubtitleSlot;
};

type ActiveStrategyFn = (data: {
    id: unknown;
    value: boolean;
    activated: Set<unknown>;
    children: Map<unknown, unknown[]>;
    parents: Map<unknown, unknown>;
    event?: Event;
}) => Set<unknown>;
type ActiveStrategyTransformInFn = (v: unknown | undefined, children: Map<unknown, unknown[]>, parents: Map<unknown, unknown>) => Set<unknown>;
type ActiveStrategyTransformOutFn = (v: Set<unknown>, children: Map<unknown, unknown[]>, parents: Map<unknown, unknown>) => unknown;
type ActiveStrategy = {
    activate: ActiveStrategyFn;
    in: ActiveStrategyTransformInFn;
    out: ActiveStrategyTransformOutFn;
};

type OpenStrategyFn = (data: {
    id: unknown;
    value: boolean;
    opened: Set<unknown>;
    children: Map<unknown, unknown[]>;
    parents: Map<unknown, unknown>;
    event?: Event;
}) => Set<unknown>;
type OpenSelectStrategyFn = (data: {
    id: unknown;
    value: boolean;
    opened: Set<unknown>;
    selected: Map<unknown, 'on' | 'off' | 'indeterminate'>;
    children: Map<unknown, unknown[]>;
    parents: Map<unknown, unknown>;
    event?: Event;
}) => Set<unknown> | null;
type OpenStrategy = {
    open: OpenStrategyFn;
    select: OpenSelectStrategyFn;
};

type SelectStrategyFn = (data: {
    id: unknown;
    value: boolean;
    selected: Map<unknown, 'on' | 'off' | 'indeterminate'>;
    children: Map<unknown, unknown[]>;
    parents: Map<unknown, unknown>;
    event?: Event;
}) => Map<unknown, 'on' | 'off' | 'indeterminate'>;
type SelectStrategyTransformInFn = (v: readonly unknown[] | undefined, children: Map<unknown, unknown[]>, parents: Map<unknown, unknown>) => Map<unknown, 'on' | 'off' | 'indeterminate'>;
type SelectStrategyTransformOutFn = (v: Map<unknown, 'on' | 'off' | 'indeterminate'>, children: Map<unknown, unknown[]>, parents: Map<unknown, unknown>) => unknown[];
type SelectStrategy = {
    select: SelectStrategyFn;
    in: SelectStrategyTransformInFn;
    out: SelectStrategyTransformOutFn;
};

type ActiveStrategyProp = 'single-leaf' | 'leaf' | 'independent' | 'single-independent' | ActiveStrategy | ((mandatory: boolean) => ActiveStrategy);
type SelectStrategyProp = 'single-leaf' | 'leaf' | 'independent' | 'single-independent' | 'classic' | SelectStrategy | ((mandatory: boolean) => SelectStrategy);
type OpenStrategyProp = 'single' | 'multiple' | 'list' | OpenStrategy;

type VListChildrenSlots<T> = {
    [K in keyof Omit<VListItemSlots, 'default'>]: VListItemSlots[K] & {
        item: T;
    };
} & {
    default: never;
    item: {
        props: InternalListItem['props'];
    };
    divider: {
        props: InternalListItem['props'];
    };
    subheader: {
        props: InternalListItem['props'];
    };
    header: {
        props: InternalListItem['props'];
    };
};

interface ListItem<T = any> extends InternalItem<T> {
    title: string;
    props: {
        [key: string]: any;
        title: string;
        value: any;
    };
    children?: ListItem<T>[];
}
interface ItemProps {
    items: any[];
    itemTitle: SelectItemKey;
    itemValue: SelectItemKey;
    itemChildren: SelectItemKey;
    itemProps: SelectItemKey;
    returnObject: boolean;
    valueComparator: typeof deepEqual;
}

interface InternalListItem<T = any> extends ListItem<T> {
    type?: 'item' | 'subheader' | 'divider';
}
type ItemType$1<T> = T extends readonly (infer U)[] ? U : never;
declare const VList: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        activated?: any;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
        activeStrategy?: ActiveStrategyProp | undefined;
        collapseIcon?: IconValue | undefined;
        expandIcon?: IconValue | undefined;
    } & {
        "onUpdate:activated"?: ((value: unknown) => any) | undefined;
        "onClick:activate"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
    }, {
        open: (id: unknown, value: boolean, event?: Event) => void;
        select: (id: unknown, value: boolean, event?: Event) => void;
        focus: (location?: "next" | "prev" | "first" | "last") => void;
        children: vue.Ref<Map<unknown, unknown[]>>;
        parents: vue.Ref<Map<unknown, unknown>>;
        getPath: (id: unknown) => unknown[];
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
        'update:selected': (value: unknown) => true;
        'update:activated': (value: unknown) => true;
        'update:opened': (value: unknown) => true;
        'click:open': (value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => true;
        'click:activate': (value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => true;
        'click:select': (value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => true;
    }, "$children" | "selected" | "v-slots" | "v-slot:default" | "items" | "v-slot:prepend" | "v-slot:append" | "v-slot:title" | "opened" | "click:open" | "click:select" | "v-slot:subtitle" | "v-slot:item" | "v-slot:header" | "v-slot:divider" | "v-slot:subheader" | "itemTitle" | "itemValue" | "itemChildren" | "itemProps" | "update:selected" | "update:opened">, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        activated?: any;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
        activeStrategy?: ActiveStrategyProp | undefined;
        collapseIcon?: IconValue | undefined;
        expandIcon?: IconValue | undefined;
    } & {
        "onUpdate:activated"?: ((value: unknown) => any) | undefined;
        "onClick:activate"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
    }, {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    }, true, {}, vue.SlotsType<Partial<{
        title: (arg: ListItemTitleSlot & {
            item: any;
        }) => vue.VNode[];
        append: (arg: ListItemSlot & {
            item: any;
        }) => vue.VNode[];
        prepend: (arg: ListItemSlot & {
            item: any;
        }) => vue.VNode[];
        subtitle: (arg: ListItemSubtitleSlot & {
            item: any;
        }) => vue.VNode[];
        default: () => vue.VNode[];
        item: (arg: {
            props: InternalListItem["props"];
        }) => vue.VNode[];
        divider: (arg: {
            props: InternalListItem["props"];
        }) => vue.VNode[];
        subheader: (arg: {
            props: InternalListItem["props"];
        }) => vue.VNode[];
        header: (arg: {
            props: InternalListItem["props"];
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        activated?: any;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
        activeStrategy?: ActiveStrategyProp | undefined;
        collapseIcon?: IconValue | undefined;
        expandIcon?: IconValue | undefined;
    } & {
        "onUpdate:activated"?: ((value: unknown) => any) | undefined;
        "onClick:activate"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
    }, {
        open: (id: unknown, value: boolean, event?: Event) => void;
        select: (id: unknown, value: boolean, event?: Event) => void;
        focus: (location?: "next" | "prev" | "first" | "last") => void;
        children: vue.Ref<Map<unknown, unknown[]>>;
        parents: vue.Ref<Map<unknown, unknown>>;
        getPath: (id: unknown) => unknown[];
    }, {}, {}, {}, {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    nav: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    lines: false | "one" | "two" | "three";
    mandatory: boolean;
    tile: boolean;
    density: Density;
    slim: boolean;
    valueComparator: typeof deepEqual;
    activatable: boolean;
    selectable: boolean;
    selectStrategy: SelectStrategyProp;
    openStrategy: OpenStrategyProp;
    returnObject: boolean;
    itemType: string;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    border?: string | number | boolean | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    activated?: any;
    class?: any;
    theme?: string | undefined;
    elevation?: string | number | undefined;
    rounded?: string | number | boolean | undefined;
    baseColor?: string | undefined;
    bgColor?: string | undefined;
    activeColor?: string | undefined;
    activeClass?: string | undefined;
    activeStrategy?: ActiveStrategyProp | undefined;
    collapseIcon?: IconValue | undefined;
    expandIcon?: IconValue | undefined;
} & {
    "onUpdate:activated"?: ((value: unknown) => any) | undefined;
    "onClick:activate"?: ((value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => any) | undefined;
}, {
    open: (id: unknown, value: boolean, event?: Event) => void;
    select: (id: unknown, value: boolean, event?: Event) => void;
    focus: (location?: "next" | "prev" | "first" | "last") => void;
    children: vue.Ref<Map<unknown, unknown[]>>;
    parents: vue.Ref<Map<unknown, unknown>>;
    getPath: (id: unknown) => unknown[];
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
    'update:selected': (value: unknown) => true;
    'update:activated': (value: unknown) => true;
    'update:opened': (value: unknown) => true;
    'click:open': (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => true;
    'click:activate': (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => true;
    'click:select': (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => true;
}, "$children" | "selected" | "v-slots" | "v-slot:default" | "items" | "v-slot:prepend" | "v-slot:append" | "v-slot:title" | "opened" | "click:open" | "click:select" | "v-slot:subtitle" | "v-slot:item" | "v-slot:header" | "v-slot:divider" | "v-slot:subheader" | "itemTitle" | "itemValue" | "itemChildren" | "itemProps" | "update:selected" | "update:opened">, string, {
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    nav: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    lines: false | "one" | "two" | "three";
    mandatory: boolean;
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    slim: boolean;
    valueComparator: typeof deepEqual;
    activatable: boolean;
    selectable: boolean;
    selectStrategy: SelectStrategyProp;
    openStrategy: OpenStrategyProp;
    returnObject: boolean;
    itemType: string;
}, {}, string, vue.SlotsType<Partial<{
    title: (arg: ListItemTitleSlot & {
        item: any;
    }) => vue.VNode[];
    append: (arg: ListItemSlot & {
        item: any;
    }) => vue.VNode[];
    prepend: (arg: ListItemSlot & {
        item: any;
    }) => vue.VNode[];
    subtitle: (arg: ListItemSubtitleSlot & {
        item: any;
    }) => vue.VNode[];
    default: () => vue.VNode[];
    item: (arg: {
        props: InternalListItem["props"];
    }) => vue.VNode[];
    divider: (arg: {
        props: InternalListItem["props"];
    }) => vue.VNode[];
    subheader: (arg: {
        props: InternalListItem["props"];
    }) => vue.VNode[];
    header: (arg: {
        props: InternalListItem["props"];
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & (new <T extends readonly any[], S = unknown, O = unknown>(props: {
    items?: T;
    itemTitle?: SelectItemKey<ItemType$1<T>>;
    itemValue?: SelectItemKey<ItemType$1<T>>;
    itemChildren?: SelectItemKey<ItemType$1<T>>;
    itemProps?: SelectItemKey<ItemType$1<T>>;
    selected?: S;
    "onUpdate:selected"?: (value: S) => void;
    "onClick:open"?: (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void;
    "onClick:select"?: (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void;
    opened?: O;
    "onUpdate:opened"?: (value: O) => void;
}, slots: VListChildrenSlots<ItemType$1<T>>) => GenericProps<typeof props, typeof slots>) & FilterPropsOptions<{
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
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    items: {
        type: PropType<ItemProps["items"]>;
        default: () => never[];
    };
    itemTitle: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemValue: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemChildren: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemProps: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    returnObject: BooleanConstructor;
    valueComparator: {
        type: PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
    itemType: {
        type: StringConstructor;
        default: string;
    };
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
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    activatable: BooleanConstructor;
    selectable: BooleanConstructor;
    activeStrategy: PropType<ActiveStrategyProp>;
    selectStrategy: {
        type: PropType<SelectStrategyProp>;
        default: NonNullable<SelectStrategyProp>;
    };
    openStrategy: {
        type: PropType<OpenStrategyProp>;
        default: NonNullable<OpenStrategyProp>;
    };
    opened: null;
    activated: null;
    selected: null;
    mandatory: BooleanConstructor;
    baseColor: StringConstructor;
    activeColor: StringConstructor;
    activeClass: StringConstructor;
    bgColor: StringConstructor;
    disabled: BooleanConstructor;
    expandIcon: PropType<IconValue>;
    collapseIcon: PropType<IconValue>;
    lines: {
        type: PropType<"one" | "two" | "three" | false>;
        default: string;
    };
    slim: BooleanConstructor;
    nav: BooleanConstructor;
    'onClick:open': PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    'onClick:select': PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    'onUpdate:opened': PropType<() => void>;
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
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    items: {
        type: PropType<ItemProps["items"]>;
        default: () => never[];
    };
    itemTitle: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemValue: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemChildren: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemProps: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    returnObject: BooleanConstructor;
    valueComparator: {
        type: PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
    itemType: {
        type: StringConstructor;
        default: string;
    };
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
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    activatable: BooleanConstructor;
    selectable: BooleanConstructor;
    activeStrategy: PropType<ActiveStrategyProp>;
    selectStrategy: {
        type: PropType<SelectStrategyProp>;
        default: NonNullable<SelectStrategyProp>;
    };
    openStrategy: {
        type: PropType<OpenStrategyProp>;
        default: NonNullable<OpenStrategyProp>;
    };
    opened: null;
    activated: null;
    selected: null;
    mandatory: BooleanConstructor;
    baseColor: StringConstructor;
    activeColor: StringConstructor;
    activeClass: StringConstructor;
    bgColor: StringConstructor;
    disabled: BooleanConstructor;
    expandIcon: PropType<IconValue>;
    collapseIcon: PropType<IconValue>;
    lines: {
        type: PropType<"one" | "two" | "three" | false>;
        default: string;
    };
    slim: BooleanConstructor;
    nav: BooleanConstructor;
    'onClick:open': PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    'onClick:select': PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    'onUpdate:opened': PropType<() => void>;
}>>;
type VList = InstanceType<typeof VList>;

declare const VMenu: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
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
        offset?: string | number | number[] | undefined;
        id?: string | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
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
        id: vue.ComputedRef<string>;
        ΨopenChildren: vue.ShallowRef<Set<number>>;
    } & Omit<Omit<{
        $: vue.ComponentInternalInstance;
        $data: {};
        $props: Partial<{
            absolute: boolean;
            location: Anchor;
            origin: "auto" | Anchor | "overlap";
            transition: string | boolean | (vue.TransitionProps & {
                component?: Component;
            });
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
            activatorProps: Record<string, any>;
            openOnClick: boolean;
            openOnHover: boolean;
            openOnFocus: boolean;
            closeOnContentClick: boolean;
            closeOnBack: boolean;
            contained: boolean;
            noClickAnimation: boolean;
            scrim: string | boolean;
            _disableGlobalStack: boolean;
        }> & Omit<{
            absolute: boolean;
            location: Anchor;
            origin: "auto" | Anchor | "overlap";
            transition: string | boolean | (vue.TransitionProps & {
                component?: Component;
            });
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
            activatorProps: Record<string, any>;
            openOnHover: boolean;
            closeOnContentClick: boolean;
            closeOnBack: boolean;
            contained: boolean;
            noClickAnimation: boolean;
            scrim: string | boolean;
            _disableGlobalStack: boolean;
            offset?: string | number | number[] | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            opacity?: string | number | undefined;
            target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
            class?: any;
            theme?: string | undefined;
            contentClass?: any;
            activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
            closeDelay?: string | number | undefined;
            openDelay?: string | number | undefined;
            openOnClick?: boolean | undefined;
            openOnFocus?: boolean | undefined;
            contentProps?: any;
            attach?: string | boolean | Element | undefined;
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
            "v-slot:default"?: false | ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNodeChild) | undefined;
            "v-slot:activator"?: false | ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNodeChild) | undefined;
            onAfterEnter?: (() => any) | undefined;
            onAfterLeave?: (() => any) | undefined;
            "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
            "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
        } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
            absolute: boolean;
            location: Anchor;
            origin: "auto" | Anchor | "overlap";
            transition: string | boolean | (vue.TransitionProps & {
                component?: Component;
            });
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
            activatorProps: Record<string, any>;
            openOnHover: boolean;
            closeOnContentClick: boolean;
            closeOnBack: boolean;
            contained: boolean;
            noClickAnimation: boolean;
            scrim: string | boolean;
            _disableGlobalStack: boolean;
        } & {
            offset?: string | number | number[] | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            opacity?: string | number | undefined;
            target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
            class?: any;
            theme?: string | undefined;
            contentClass?: any;
            activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
            closeDelay?: string | number | undefined;
            openDelay?: string | number | undefined;
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
            onAfterEnter?: (() => any) | undefined;
            onAfterLeave?: (() => any) | undefined;
            "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
            "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
        }, "absolute" | "location" | "origin" | "transition" | "zIndex" | "style" | "eager" | "disabled" | "persistent" | "modelValue" | "locationStrategy" | "scrollStrategy" | "activatorProps" | "openOnClick" | "openOnHover" | "openOnFocus" | "closeOnContentClick" | "closeOnBack" | "contained" | "noClickAnimation" | "scrim" | "_disableGlobalStack">;
        $attrs: {
            [x: string]: unknown;
        };
        $refs: {
            [x: string]: unknown;
        };
        $slots: Readonly<{
            default?: ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNode[]) | undefined;
            activator?: ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNode[]) | undefined;
        }>;
        $root: vue.ComponentPublicInstance | null;
        $parent: vue.ComponentPublicInstance | null;
        $emit: ((event: "update:modelValue", value: boolean) => void) & ((event: "click:outside", e: MouseEvent) => void) & ((event: "afterEnter") => void) & ((event: "afterLeave") => void);
        $el: any;
        $options: vue.ComponentOptionsBase<{
            absolute: boolean;
            location: Anchor;
            origin: "auto" | Anchor | "overlap";
            transition: string | boolean | (vue.TransitionProps & {
                component?: Component;
            });
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
            activatorProps: Record<string, any>;
            openOnHover: boolean;
            closeOnContentClick: boolean;
            closeOnBack: boolean;
            contained: boolean;
            noClickAnimation: boolean;
            scrim: string | boolean;
            _disableGlobalStack: boolean;
        } & {
            offset?: string | number | number[] | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            opacity?: string | number | undefined;
            target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
            class?: any;
            theme?: string | undefined;
            contentClass?: any;
            activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
            closeDelay?: string | number | undefined;
            openDelay?: string | number | undefined;
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
            onAfterEnter?: (() => any) | undefined;
            onAfterLeave?: (() => any) | undefined;
            "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
            "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
        }, {
            activatorEl: vue.Ref<HTMLElement | undefined>;
            scrimEl: vue.Ref<HTMLElement | undefined>;
            target: vue.ComputedRef<HTMLElement | [x: number, y: number] | undefined>;
            animateClick: () => void;
            contentEl: vue.Ref<HTMLElement | undefined>;
            globalTop: Readonly<vue.Ref<boolean>>;
            localTop: vue.ComputedRef<boolean>;
            updateLocation: vue.Ref<((e: Event) => void) | undefined>;
        }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
            'click:outside': (e: MouseEvent) => true;
            'update:modelValue': (value: boolean) => true;
            afterEnter: () => true;
            afterLeave: () => true;
        }, string, {
            absolute: boolean;
            location: Anchor;
            origin: "auto" | Anchor | "overlap";
            transition: string | boolean | (vue.TransitionProps & {
                component?: Component;
            });
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
            activatorProps: Record<string, any>;
            openOnClick: boolean;
            openOnHover: boolean;
            openOnFocus: boolean;
            closeOnContentClick: boolean;
            closeOnBack: boolean;
            contained: boolean;
            noClickAnimation: boolean;
            scrim: string | boolean;
            _disableGlobalStack: boolean;
        }, {}, string, vue.SlotsType<Partial<{
            default: (arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNode[];
            activator: (arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNode[];
        }>>> & {
            beforeCreate?: (() => void) | (() => void)[];
            created?: (() => void) | (() => void)[];
            beforeMount?: (() => void) | (() => void)[];
            mounted?: (() => void) | (() => void)[];
            beforeUpdate?: (() => void) | (() => void)[];
            updated?: (() => void) | (() => void)[];
            activated?: (() => void) | (() => void)[];
            deactivated?: (() => void) | (() => void)[];
            beforeDestroy?: (() => void) | (() => void)[];
            beforeUnmount?: (() => void) | (() => void)[];
            destroyed?: (() => void) | (() => void)[];
            unmounted?: (() => void) | (() => void)[];
            renderTracked?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
            renderTriggered?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
            errorCaptured?: ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void) | ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void)[];
        };
        $forceUpdate: () => void;
        $nextTick: typeof nextTick;
        $watch<T extends string | ((...args: any) => any)>(source: T, cb: T extends (...args: any) => infer R ? (...args: [R, R]) => any : (...args: any) => any, options?: vue.WatchOptions): vue.WatchStopHandle;
    } & Omit<{
        absolute: boolean;
        location: Anchor;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        });
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
        activatorProps: Record<string, any>;
        openOnHover: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        _disableGlobalStack: boolean;
    } & {
        offset?: string | number | number[] | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        opacity?: string | number | undefined;
        target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
        class?: any;
        theme?: string | undefined;
        contentClass?: any;
        activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
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
        onAfterEnter?: (() => any) | undefined;
        onAfterLeave?: (() => any) | undefined;
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
        "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
    }, "target" | "contentEl" | "activatorEl" | "scrimEl" | "animateClick" | "globalTop" | "localTop" | "updateLocation"> & vue.ShallowUnwrapRef<{
        activatorEl: vue.Ref<HTMLElement | undefined>;
        scrimEl: vue.Ref<HTMLElement | undefined>;
        target: vue.ComputedRef<HTMLElement | [x: number, y: number] | undefined>;
        animateClick: () => void;
        contentEl: vue.Ref<HTMLElement | undefined>;
        globalTop: Readonly<vue.Ref<boolean>>;
        localTop: vue.ComputedRef<boolean>;
        updateLocation: vue.Ref<((e: Event) => void) | undefined>;
    }> & {} & vue.ComponentCustomProperties & {}, "offset" | "key" | "height" | "width" | "maxHeight" | "maxWidth" | "minHeight" | "minWidth" | "opacity" | "target" | "class" | "theme" | "ref" | "onAfterEnter" | "onAfterLeave" | "$children" | "v-slots" | "v-slot:default" | "ref_for" | "ref_key" | "onVnodeBeforeMount" | "onVnodeMounted" | "onVnodeBeforeUpdate" | "onVnodeUpdated" | "onVnodeBeforeUnmount" | "onVnodeUnmounted" | "onUpdate:modelValue" | "contentClass" | "activator" | "v-slot:activator" | "closeDelay" | "openDelay" | "contentProps" | "attach" | "onClick:outside" | ("absolute" | "location" | "origin" | "transition" | "zIndex" | "style" | "eager" | "disabled" | "persistent" | "modelValue" | "locationStrategy" | "scrollStrategy" | "activatorProps" | "openOnClick" | "openOnHover" | "openOnFocus" | "closeOnContentClick" | "closeOnBack" | "contained" | "noClickAnimation" | "scrim" | "_disableGlobalStack")>, `$${any}`> & {
        _allExposed: {
            activatorEl: vue.Ref<HTMLElement | undefined>;
            scrimEl: vue.Ref<HTMLElement | undefined>;
            target: vue.ComputedRef<HTMLElement | [x: number, y: number] | undefined>;
            animateClick: () => void;
            contentEl: vue.Ref<HTMLElement | undefined>;
            globalTop: Readonly<vue.Ref<boolean>>;
            localTop: vue.ComputedRef<boolean>;
            updateLocation: vue.Ref<((e: Event) => void) | undefined>;
        } | {
            id: vue.ComputedRef<string>;
            ΨopenChildren: vue.ShallowRef<Set<number>>;
        };
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (value: boolean) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
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
        offset?: string | number | number[] | undefined;
        id?: string | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
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
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
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
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
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
        offset?: string | number | number[] | undefined;
        id?: string | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
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
        id: vue.ComputedRef<string>;
        ΨopenChildren: vue.ShallowRef<Set<number>>;
    } & Omit<Omit<{
        $: vue.ComponentInternalInstance;
        $data: {};
        $props: Partial<{
            absolute: boolean;
            location: Anchor;
            origin: "auto" | Anchor | "overlap";
            transition: string | boolean | (vue.TransitionProps & {
                component?: Component;
            });
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
            activatorProps: Record<string, any>;
            openOnClick: boolean;
            openOnHover: boolean;
            openOnFocus: boolean;
            closeOnContentClick: boolean;
            closeOnBack: boolean;
            contained: boolean;
            noClickAnimation: boolean;
            scrim: string | boolean;
            _disableGlobalStack: boolean;
        }> & Omit<{
            absolute: boolean;
            location: Anchor;
            origin: "auto" | Anchor | "overlap";
            transition: string | boolean | (vue.TransitionProps & {
                component?: Component;
            });
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
            activatorProps: Record<string, any>;
            openOnHover: boolean;
            closeOnContentClick: boolean;
            closeOnBack: boolean;
            contained: boolean;
            noClickAnimation: boolean;
            scrim: string | boolean;
            _disableGlobalStack: boolean;
            offset?: string | number | number[] | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            opacity?: string | number | undefined;
            target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
            class?: any;
            theme?: string | undefined;
            contentClass?: any;
            activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
            closeDelay?: string | number | undefined;
            openDelay?: string | number | undefined;
            openOnClick?: boolean | undefined;
            openOnFocus?: boolean | undefined;
            contentProps?: any;
            attach?: string | boolean | Element | undefined;
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
            "v-slot:default"?: false | ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNodeChild) | undefined;
            "v-slot:activator"?: false | ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNodeChild) | undefined;
            onAfterEnter?: (() => any) | undefined;
            onAfterLeave?: (() => any) | undefined;
            "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
            "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
        } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
            absolute: boolean;
            location: Anchor;
            origin: "auto" | Anchor | "overlap";
            transition: string | boolean | (vue.TransitionProps & {
                component?: Component;
            });
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
            activatorProps: Record<string, any>;
            openOnHover: boolean;
            closeOnContentClick: boolean;
            closeOnBack: boolean;
            contained: boolean;
            noClickAnimation: boolean;
            scrim: string | boolean;
            _disableGlobalStack: boolean;
        } & {
            offset?: string | number | number[] | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            opacity?: string | number | undefined;
            target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
            class?: any;
            theme?: string | undefined;
            contentClass?: any;
            activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
            closeDelay?: string | number | undefined;
            openDelay?: string | number | undefined;
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
            onAfterEnter?: (() => any) | undefined;
            onAfterLeave?: (() => any) | undefined;
            "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
            "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
        }, "absolute" | "location" | "origin" | "transition" | "zIndex" | "style" | "eager" | "disabled" | "persistent" | "modelValue" | "locationStrategy" | "scrollStrategy" | "activatorProps" | "openOnClick" | "openOnHover" | "openOnFocus" | "closeOnContentClick" | "closeOnBack" | "contained" | "noClickAnimation" | "scrim" | "_disableGlobalStack">;
        $attrs: {
            [x: string]: unknown;
        };
        $refs: {
            [x: string]: unknown;
        };
        $slots: Readonly<{
            default?: ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNode[]) | undefined;
            activator?: ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNode[]) | undefined;
        }>;
        $root: vue.ComponentPublicInstance | null;
        $parent: vue.ComponentPublicInstance | null;
        $emit: ((event: "update:modelValue", value: boolean) => void) & ((event: "click:outside", e: MouseEvent) => void) & ((event: "afterEnter") => void) & ((event: "afterLeave") => void);
        $el: any;
        $options: vue.ComponentOptionsBase<{
            absolute: boolean;
            location: Anchor;
            origin: "auto" | Anchor | "overlap";
            transition: string | boolean | (vue.TransitionProps & {
                component?: Component;
            });
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
            activatorProps: Record<string, any>;
            openOnHover: boolean;
            closeOnContentClick: boolean;
            closeOnBack: boolean;
            contained: boolean;
            noClickAnimation: boolean;
            scrim: string | boolean;
            _disableGlobalStack: boolean;
        } & {
            offset?: string | number | number[] | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            opacity?: string | number | undefined;
            target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
            class?: any;
            theme?: string | undefined;
            contentClass?: any;
            activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
            closeDelay?: string | number | undefined;
            openDelay?: string | number | undefined;
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
            onAfterEnter?: (() => any) | undefined;
            onAfterLeave?: (() => any) | undefined;
            "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
            "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
        }, {
            activatorEl: vue.Ref<HTMLElement | undefined>;
            scrimEl: vue.Ref<HTMLElement | undefined>;
            target: vue.ComputedRef<HTMLElement | [x: number, y: number] | undefined>;
            animateClick: () => void;
            contentEl: vue.Ref<HTMLElement | undefined>;
            globalTop: Readonly<vue.Ref<boolean>>;
            localTop: vue.ComputedRef<boolean>;
            updateLocation: vue.Ref<((e: Event) => void) | undefined>;
        }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
            'click:outside': (e: MouseEvent) => true;
            'update:modelValue': (value: boolean) => true;
            afterEnter: () => true;
            afterLeave: () => true;
        }, string, {
            absolute: boolean;
            location: Anchor;
            origin: "auto" | Anchor | "overlap";
            transition: string | boolean | (vue.TransitionProps & {
                component?: Component;
            });
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
            activatorProps: Record<string, any>;
            openOnClick: boolean;
            openOnHover: boolean;
            openOnFocus: boolean;
            closeOnContentClick: boolean;
            closeOnBack: boolean;
            contained: boolean;
            noClickAnimation: boolean;
            scrim: string | boolean;
            _disableGlobalStack: boolean;
        }, {}, string, vue.SlotsType<Partial<{
            default: (arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNode[];
            activator: (arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNode[];
        }>>> & {
            beforeCreate?: (() => void) | (() => void)[];
            created?: (() => void) | (() => void)[];
            beforeMount?: (() => void) | (() => void)[];
            mounted?: (() => void) | (() => void)[];
            beforeUpdate?: (() => void) | (() => void)[];
            updated?: (() => void) | (() => void)[];
            activated?: (() => void) | (() => void)[];
            deactivated?: (() => void) | (() => void)[];
            beforeDestroy?: (() => void) | (() => void)[];
            beforeUnmount?: (() => void) | (() => void)[];
            destroyed?: (() => void) | (() => void)[];
            unmounted?: (() => void) | (() => void)[];
            renderTracked?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
            renderTriggered?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
            errorCaptured?: ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void) | ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void)[];
        };
        $forceUpdate: () => void;
        $nextTick: typeof nextTick;
        $watch<T extends string | ((...args: any) => any)>(source: T, cb: T extends (...args: any) => infer R ? (...args: [R, R]) => any : (...args: any) => any, options?: vue.WatchOptions): vue.WatchStopHandle;
    } & Omit<{
        absolute: boolean;
        location: Anchor;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        });
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
        activatorProps: Record<string, any>;
        openOnHover: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        _disableGlobalStack: boolean;
    } & {
        offset?: string | number | number[] | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        opacity?: string | number | undefined;
        target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
        class?: any;
        theme?: string | undefined;
        contentClass?: any;
        activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
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
        onAfterEnter?: (() => any) | undefined;
        onAfterLeave?: (() => any) | undefined;
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
        "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
    }, "target" | "contentEl" | "activatorEl" | "scrimEl" | "animateClick" | "globalTop" | "localTop" | "updateLocation"> & vue.ShallowUnwrapRef<{
        activatorEl: vue.Ref<HTMLElement | undefined>;
        scrimEl: vue.Ref<HTMLElement | undefined>;
        target: vue.ComputedRef<HTMLElement | [x: number, y: number] | undefined>;
        animateClick: () => void;
        contentEl: vue.Ref<HTMLElement | undefined>;
        globalTop: Readonly<vue.Ref<boolean>>;
        localTop: vue.ComputedRef<boolean>;
        updateLocation: vue.Ref<((e: Event) => void) | undefined>;
    }> & {} & vue.ComponentCustomProperties & {}, "offset" | "key" | "height" | "width" | "maxHeight" | "maxWidth" | "minHeight" | "minWidth" | "opacity" | "target" | "class" | "theme" | "ref" | "onAfterEnter" | "onAfterLeave" | "$children" | "v-slots" | "v-slot:default" | "ref_for" | "ref_key" | "onVnodeBeforeMount" | "onVnodeMounted" | "onVnodeBeforeUpdate" | "onVnodeUpdated" | "onVnodeBeforeUnmount" | "onVnodeUnmounted" | "onUpdate:modelValue" | "contentClass" | "activator" | "v-slot:activator" | "closeDelay" | "openDelay" | "contentProps" | "attach" | "onClick:outside" | ("absolute" | "location" | "origin" | "transition" | "zIndex" | "style" | "eager" | "disabled" | "persistent" | "modelValue" | "locationStrategy" | "scrollStrategy" | "activatorProps" | "openOnClick" | "openOnHover" | "openOnFocus" | "closeOnContentClick" | "closeOnBack" | "contained" | "noClickAnimation" | "scrim" | "_disableGlobalStack")>, `$${any}`> & {
        _allExposed: {
            activatorEl: vue.Ref<HTMLElement | undefined>;
            scrimEl: vue.Ref<HTMLElement | undefined>;
            target: vue.ComputedRef<HTMLElement | [x: number, y: number] | undefined>;
            animateClick: () => void;
            contentEl: vue.Ref<HTMLElement | undefined>;
            globalTop: Readonly<vue.Ref<boolean>>;
            localTop: vue.ComputedRef<boolean>;
            updateLocation: vue.Ref<((e: Event) => void) | undefined>;
        } | {
            id: vue.ComputedRef<string>;
            ΨopenChildren: vue.ShallowRef<Set<number>>;
        };
    }, {}, {}, {}, {
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
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
    location: Anchor | undefined;
    origin: "auto" | Anchor | "overlap";
    transition: string | boolean | (vue.TransitionProps & {
        component?: Component;
    }) | {
        component: Component;
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
    offset?: string | number | number[] | undefined;
    id?: string | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
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
    id: vue.ComputedRef<string>;
    ΨopenChildren: vue.ShallowRef<Set<number>>;
} & Omit<Omit<{
    $: vue.ComponentInternalInstance;
    $data: {};
    $props: Partial<{
        absolute: boolean;
        location: Anchor;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        });
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
        activatorProps: Record<string, any>;
        openOnClick: boolean;
        openOnHover: boolean;
        openOnFocus: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        _disableGlobalStack: boolean;
    }> & Omit<{
        absolute: boolean;
        location: Anchor;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        });
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
        activatorProps: Record<string, any>;
        openOnHover: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        _disableGlobalStack: boolean;
        offset?: string | number | number[] | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        opacity?: string | number | undefined;
        target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
        class?: any;
        theme?: string | undefined;
        contentClass?: any;
        activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
        openOnClick?: boolean | undefined;
        openOnFocus?: boolean | undefined;
        contentProps?: any;
        attach?: string | boolean | Element | undefined;
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
        "v-slot:default"?: false | ((arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNodeChild) | undefined;
        "v-slot:activator"?: false | ((arg: {
            isActive: boolean;
            props: Record<string, any>;
            targetRef: TemplateRef;
        }) => vue.VNodeChild) | undefined;
        onAfterEnter?: (() => any) | undefined;
        onAfterLeave?: (() => any) | undefined;
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
        "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
    } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        absolute: boolean;
        location: Anchor;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        });
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
        activatorProps: Record<string, any>;
        openOnHover: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        _disableGlobalStack: boolean;
    } & {
        offset?: string | number | number[] | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        opacity?: string | number | undefined;
        target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
        class?: any;
        theme?: string | undefined;
        contentClass?: any;
        activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
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
        onAfterEnter?: (() => any) | undefined;
        onAfterLeave?: (() => any) | undefined;
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
        "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
    }, "absolute" | "location" | "origin" | "transition" | "zIndex" | "style" | "eager" | "disabled" | "persistent" | "modelValue" | "locationStrategy" | "scrollStrategy" | "activatorProps" | "openOnClick" | "openOnHover" | "openOnFocus" | "closeOnContentClick" | "closeOnBack" | "contained" | "noClickAnimation" | "scrim" | "_disableGlobalStack">;
    $attrs: {
        [x: string]: unknown;
    };
    $refs: {
        [x: string]: unknown;
    };
    $slots: Readonly<{
        default?: ((arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNode[]) | undefined;
        activator?: ((arg: {
            isActive: boolean;
            props: Record<string, any>;
            targetRef: TemplateRef;
        }) => vue.VNode[]) | undefined;
    }>;
    $root: vue.ComponentPublicInstance | null;
    $parent: vue.ComponentPublicInstance | null;
    $emit: ((event: "update:modelValue", value: boolean) => void) & ((event: "click:outside", e: MouseEvent) => void) & ((event: "afterEnter") => void) & ((event: "afterLeave") => void);
    $el: any;
    $options: vue.ComponentOptionsBase<{
        absolute: boolean;
        location: Anchor;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        });
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
        activatorProps: Record<string, any>;
        openOnHover: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        _disableGlobalStack: boolean;
    } & {
        offset?: string | number | number[] | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        opacity?: string | number | undefined;
        target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
        class?: any;
        theme?: string | undefined;
        contentClass?: any;
        activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
        closeDelay?: string | number | undefined;
        openDelay?: string | number | undefined;
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
        onAfterEnter?: (() => any) | undefined;
        onAfterLeave?: (() => any) | undefined;
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
        "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
    }, {
        activatorEl: vue.Ref<HTMLElement | undefined>;
        scrimEl: vue.Ref<HTMLElement | undefined>;
        target: vue.ComputedRef<HTMLElement | [x: number, y: number] | undefined>;
        animateClick: () => void;
        contentEl: vue.Ref<HTMLElement | undefined>;
        globalTop: Readonly<vue.Ref<boolean>>;
        localTop: vue.ComputedRef<boolean>;
        updateLocation: vue.Ref<((e: Event) => void) | undefined>;
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'click:outside': (e: MouseEvent) => true;
        'update:modelValue': (value: boolean) => true;
        afterEnter: () => true;
        afterLeave: () => true;
    }, string, {
        absolute: boolean;
        location: Anchor;
        origin: "auto" | Anchor | "overlap";
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        });
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
        activatorProps: Record<string, any>;
        openOnClick: boolean;
        openOnHover: boolean;
        openOnFocus: boolean;
        closeOnContentClick: boolean;
        closeOnBack: boolean;
        contained: boolean;
        noClickAnimation: boolean;
        scrim: string | boolean;
        _disableGlobalStack: boolean;
    }, {}, string, vue.SlotsType<Partial<{
        default: (arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNode[];
        activator: (arg: {
            isActive: boolean;
            props: Record<string, any>;
            targetRef: TemplateRef;
        }) => vue.VNode[];
    }>>> & {
        beforeCreate?: (() => void) | (() => void)[];
        created?: (() => void) | (() => void)[];
        beforeMount?: (() => void) | (() => void)[];
        mounted?: (() => void) | (() => void)[];
        beforeUpdate?: (() => void) | (() => void)[];
        updated?: (() => void) | (() => void)[];
        activated?: (() => void) | (() => void)[];
        deactivated?: (() => void) | (() => void)[];
        beforeDestroy?: (() => void) | (() => void)[];
        beforeUnmount?: (() => void) | (() => void)[];
        destroyed?: (() => void) | (() => void)[];
        unmounted?: (() => void) | (() => void)[];
        renderTracked?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
        renderTriggered?: ((e: vue.DebuggerEvent) => void) | ((e: vue.DebuggerEvent) => void)[];
        errorCaptured?: ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void) | ((err: unknown, instance: vue.ComponentPublicInstance | null, info: string) => boolean | void)[];
    };
    $forceUpdate: () => void;
    $nextTick: typeof nextTick;
    $watch<T extends string | ((...args: any) => any)>(source: T, cb: T extends (...args: any) => infer R ? (...args: [R, R]) => any : (...args: any) => any, options?: vue.WatchOptions): vue.WatchStopHandle;
} & Omit<{
    absolute: boolean;
    location: Anchor;
    origin: "auto" | Anchor | "overlap";
    transition: string | boolean | (vue.TransitionProps & {
        component?: Component;
    });
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
    activatorProps: Record<string, any>;
    openOnHover: boolean;
    closeOnContentClick: boolean;
    closeOnBack: boolean;
    contained: boolean;
    noClickAnimation: boolean;
    scrim: string | boolean;
    _disableGlobalStack: boolean;
} & {
    offset?: string | number | number[] | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    opacity?: string | number | undefined;
    target?: Element | "cursor" | "parent" | (string & {}) | vue.ComponentPublicInstance | [x: number, y: number] | undefined;
    class?: any;
    theme?: string | undefined;
    contentClass?: any;
    activator?: Element | "parent" | (string & {}) | vue.ComponentPublicInstance | undefined;
    closeDelay?: string | number | undefined;
    openDelay?: string | number | undefined;
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
    onAfterEnter?: (() => any) | undefined;
    onAfterLeave?: (() => any) | undefined;
    "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
    "onClick:outside"?: ((e: MouseEvent) => any) | undefined;
}, "target" | "contentEl" | "activatorEl" | "scrimEl" | "animateClick" | "globalTop" | "localTop" | "updateLocation"> & vue.ShallowUnwrapRef<{
    activatorEl: vue.Ref<HTMLElement | undefined>;
    scrimEl: vue.Ref<HTMLElement | undefined>;
    target: vue.ComputedRef<HTMLElement | [x: number, y: number] | undefined>;
    animateClick: () => void;
    contentEl: vue.Ref<HTMLElement | undefined>;
    globalTop: Readonly<vue.Ref<boolean>>;
    localTop: vue.ComputedRef<boolean>;
    updateLocation: vue.Ref<((e: Event) => void) | undefined>;
}> & {} & vue.ComponentCustomProperties & {}, "offset" | "key" | "height" | "width" | "maxHeight" | "maxWidth" | "minHeight" | "minWidth" | "opacity" | "target" | "class" | "theme" | "ref" | "onAfterEnter" | "onAfterLeave" | "$children" | "v-slots" | "v-slot:default" | "ref_for" | "ref_key" | "onVnodeBeforeMount" | "onVnodeMounted" | "onVnodeBeforeUpdate" | "onVnodeUpdated" | "onVnodeBeforeUnmount" | "onVnodeUnmounted" | "onUpdate:modelValue" | "contentClass" | "activator" | "v-slot:activator" | "closeDelay" | "openDelay" | "contentProps" | "attach" | "onClick:outside" | ("absolute" | "location" | "origin" | "transition" | "zIndex" | "style" | "eager" | "disabled" | "persistent" | "modelValue" | "locationStrategy" | "scrollStrategy" | "activatorProps" | "openOnClick" | "openOnHover" | "openOnFocus" | "closeOnContentClick" | "closeOnBack" | "contained" | "noClickAnimation" | "scrim" | "_disableGlobalStack")>, `$${any}`> & {
    _allExposed: {
        activatorEl: vue.Ref<HTMLElement | undefined>;
        scrimEl: vue.Ref<HTMLElement | undefined>;
        target: vue.ComputedRef<HTMLElement | [x: number, y: number] | undefined>;
        animateClick: () => void;
        contentEl: vue.Ref<HTMLElement | undefined>;
        globalTop: Readonly<vue.Ref<boolean>>;
        localTop: vue.ComputedRef<boolean>;
        updateLocation: vue.Ref<((e: Event) => void) | undefined>;
    } | {
        id: vue.ComputedRef<string>;
        ΨopenChildren: vue.ShallowRef<Set<number>>;
    };
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (value: boolean) => true;
}, string, {
    location: Anchor | undefined;
    origin: "auto" | Anchor | "overlap";
    transition: string | boolean | (vue.TransitionProps & {
        component?: Component;
    }) | {
        component: Component;
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
    offset: vue.PropType<StrategyProps["offset"]>;
    location: Omit<{
        type: vue.PropType<StrategyProps["location"]>;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<Anchor | undefined>;
        default: NonNullable<Anchor> | undefined;
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
    minWidth: (StringConstructor | NumberConstructor)[];
    opacity: (StringConstructor | NumberConstructor)[];
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
    closeDelay: {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    openDelay: {
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
    offset: vue.PropType<StrategyProps["offset"]>;
    location: Omit<{
        type: vue.PropType<StrategyProps["location"]>;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<Anchor | undefined>;
        default: NonNullable<Anchor> | undefined;
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
    minWidth: (StringConstructor | NumberConstructor)[];
    opacity: (StringConstructor | NumberConstructor)[];
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
    closeDelay: {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    openDelay: {
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
type VMenu = InstanceType<typeof VMenu>;

type Primitive = string | number | boolean | symbol;
type Val<T, ReturnObject extends boolean> = [T] extends [Primitive] ? T : (ReturnObject extends true ? T : any);
type Value<T, ReturnObject extends boolean, Multiple extends boolean> = Multiple extends true ? readonly Val<T, ReturnObject>[] : Val<T, ReturnObject> | null;
type ItemType<T> = T extends readonly (infer U)[] ? U : never;
declare const VAutocomplete: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        flat: boolean;
        reverse: boolean;
        variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
        type: string;
        error: boolean;
        active: boolean;
        direction: "horizontal" | "vertical";
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        menu: boolean;
        style: vue.StyleValue;
        role: string;
        autofocus: boolean;
        eager: boolean;
        disabled: boolean;
        readonly: boolean | null;
        messages: string | readonly string[];
        noDataText: string;
        focused: boolean;
        errorMessages: string | readonly string[] | null;
        maxErrors: string | number;
        rules: readonly ValidationRule[];
        tile: boolean;
        density: Density;
        valueComparator: typeof deepEqual;
        clearIcon: IconValue;
        hideSpinButtons: boolean;
        persistentHint: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        filterKeys: FilterKeys;
        itemChildren: SelectItemKey;
        clearable: boolean;
        persistentClear: boolean;
        singleLine: boolean;
        persistentPlaceholder: boolean;
        persistentCounter: boolean;
        chips: boolean;
        closableChips: boolean;
        closeText: string;
        openText: string;
        hideNoData: boolean;
        hideSelected: boolean;
        menuIcon: IconValue;
        openOnClear: boolean;
        clearOnSelect: boolean;
    } & {
        search?: string | undefined;
        name?: string | undefined;
        id?: string | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxWidth?: string | number | undefined;
        minWidth?: string | number | undefined;
        loading?: string | boolean | undefined;
        label?: string | undefined;
        prefix?: string | undefined;
        class?: any;
        theme?: string | undefined;
        placeholder?: string | undefined;
        counter?: string | number | boolean | undefined;
        'onUpdate:focused'?: ((args_0: boolean) => void) | undefined;
        validateOn?: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        prependInnerIcon?: IconValue | undefined;
        'onClick:clear'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:append'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prepend'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:appendInner'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prependInner'?: ((args_0: MouseEvent) => void) | undefined;
        centerAffix?: boolean | undefined;
        hint?: string | undefined;
        hideDetails?: boolean | "auto" | undefined;
        customFilter?: FilterFunction | undefined;
        customKeyFilter?: FilterKeyFunctions | undefined;
        suffix?: string | undefined;
        counterValue?: number | ((value: any) => number) | undefined;
        modelModifiers?: Record<string, boolean> | undefined;
        listProps?: (Partial<{
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            nav: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            tag: string;
            lines: false | "one" | "two" | "three";
            mandatory: boolean;
            rounded: string | number | boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            valueComparator: typeof deepEqual;
            activatable: boolean;
            selectable: boolean;
            selectStrategy: SelectStrategyProp;
            openStrategy: OpenStrategyProp;
            returnObject: boolean;
            itemType: string;
        }> & Omit<{
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            nav: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            tag: string;
            lines: false | "one" | "two" | "three";
            mandatory: boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            valueComparator: typeof deepEqual;
            activatable: boolean;
            selectable: boolean;
            selectStrategy: SelectStrategyProp;
            openStrategy: OpenStrategyProp;
            returnObject: boolean;
            itemType: string;
            height?: string | number | undefined;
            width?: string | number | undefined;
            border?: string | number | boolean | undefined;
            color?: string | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            activated?: any;
            class?: any;
            theme?: string | undefined;
            elevation?: string | number | undefined;
            rounded?: string | number | boolean | undefined;
            baseColor?: string | undefined;
            bgColor?: string | undefined;
            activeColor?: string | undefined;
            activeClass?: string | undefined;
            activeStrategy?: ActiveStrategyProp | undefined;
            collapseIcon?: IconValue | undefined;
            expandIcon?: IconValue | undefined;
            "onUpdate:activated"?: ((value: unknown) => any) | undefined;
            "onClick:activate"?: ((value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => any) | undefined;
        } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            nav: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            tag: string;
            lines: false | "one" | "two" | "three";
            mandatory: boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            valueComparator: typeof deepEqual;
            activatable: boolean;
            selectable: boolean;
            selectStrategy: SelectStrategyProp;
            openStrategy: OpenStrategyProp;
            returnObject: boolean;
            itemType: string;
        } & {
            height?: string | number | undefined;
            width?: string | number | undefined;
            border?: string | number | boolean | undefined;
            color?: string | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            activated?: any;
            class?: any;
            theme?: string | undefined;
            elevation?: string | number | undefined;
            rounded?: string | number | boolean | undefined;
            baseColor?: string | undefined;
            bgColor?: string | undefined;
            activeColor?: string | undefined;
            activeClass?: string | undefined;
            activeStrategy?: ActiveStrategyProp | undefined;
            collapseIcon?: IconValue | undefined;
            expandIcon?: IconValue | undefined;
        } & {
            "onUpdate:activated"?: ((value: unknown) => any) | undefined;
            "onClick:activate"?: ((value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => any) | undefined;
        }, "variant" | "nav" | "style" | "disabled" | "tag" | "lines" | "mandatory" | "rounded" | "tile" | "density" | "slim" | "valueComparator" | "activatable" | "selectable" | "selectStrategy" | "openStrategy" | "returnObject" | "itemType"> & {
            items?: readonly any[] | undefined;
            itemTitle?: SelectItemKey<any>;
            itemValue?: SelectItemKey<any>;
            itemChildren?: SelectItemKey<any>;
            itemProps?: SelectItemKey<any>;
            selected?: unknown;
            'onUpdate:selected'?: ((value: unknown) => void) | undefined;
            'onClick:open'?: (value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => void;
            'onClick:select'?: (value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => void;
            opened?: unknown;
            'onUpdate:opened'?: ((value: unknown) => void) | undefined;
        } & {
            $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
                title?: ((arg: ListItemTitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                append?: ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                prepend?: ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                subtitle?: ((arg: ListItemSubtitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                default?: (() => vue.VNodeChild) | undefined;
                item?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                divider?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                subheader?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                header?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
            };
            'v-slots'?: {
                title?: false | ((arg: ListItemTitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                append?: false | ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                prepend?: false | ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                subtitle?: false | ((arg: ListItemSubtitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                default?: false | (() => vue.VNodeChild) | undefined;
                item?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                divider?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                subheader?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                header?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
            } | undefined;
        } & {
            "v-slot:title"?: false | ((arg: ListItemTitleSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:append"?: false | ((arg: ListItemSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:prepend"?: false | ((arg: ListItemSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:item"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            "v-slot:divider"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            "v-slot:subheader"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            "v-slot:header"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
        }) | undefined;
        menuProps?: (Partial<{
            location: Anchor | undefined;
            origin: "auto" | Anchor | "overlap";
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
        }> & Omit<{
            location: Anchor | undefined;
            origin: "auto" | Anchor | "overlap";
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
            offset?: string | number | number[] | undefined;
            id?: string | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
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
            "v-slot:default"?: false | ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNodeChild) | undefined;
            "v-slot:activator"?: false | ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNodeChild) | undefined;
            "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
        } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
            location: Anchor | undefined;
            origin: "auto" | Anchor | "overlap";
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
            offset?: string | number | number[] | undefined;
            id?: string | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
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
        }, "location" | "origin" | "transition" | "zIndex" | "style" | "eager" | "disabled" | "persistent" | "modelValue" | "locationStrategy" | "scrollStrategy" | "closeDelay" | "openDelay" | "activatorProps" | "openOnClick" | "openOnHover" | "openOnFocus" | "closeOnContentClick" | "closeOnBack" | "contained" | "noClickAnimation" | "scrim" | "submenu">) | undefined;
        itemColor?: string | undefined;
        autoSelectFirst?: boolean | "exact" | undefined;
    } & {
        "onUpdate:focused"?: ((focused: boolean) => any) | undefined;
        "onUpdate:menu"?: ((value: boolean) => any) | undefined;
        "onUpdate:search"?: ((value: any) => any) | undefined;
    }, any, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
        'update:focused': (focused: boolean) => true;
        'update:search': (value: any) => true;
        'update:modelValue': (value: any) => true;
        'update:menu': (value: boolean) => true;
    }, "multiple" | "$children" | "v-slots" | "modelValue" | "items" | "v-slot:prepend" | "v-slot:append" | "update:modelValue" | "v-slot:loader" | "v-slot:label" | "v-slot:message" | "v-slot:details" | "returnObject" | "v-slot:item" | "itemTitle" | "itemValue" | "itemProps" | "v-slot:clear" | "v-slot:prepend-inner" | "v-slot:append-inner" | "v-slot:chip" | "v-slot:selection" | "v-slot:prepend-item" | "v-slot:append-item" | "v-slot:no-data">, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        flat: boolean;
        reverse: boolean;
        variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
        type: string;
        error: boolean;
        active: boolean;
        direction: "horizontal" | "vertical";
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        menu: boolean;
        style: vue.StyleValue;
        role: string;
        autofocus: boolean;
        eager: boolean;
        disabled: boolean;
        readonly: boolean | null;
        messages: string | readonly string[];
        noDataText: string;
        focused: boolean;
        errorMessages: string | readonly string[] | null;
        maxErrors: string | number;
        rules: readonly ValidationRule[];
        tile: boolean;
        density: Density;
        valueComparator: typeof deepEqual;
        clearIcon: IconValue;
        hideSpinButtons: boolean;
        persistentHint: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        filterKeys: FilterKeys;
        itemChildren: SelectItemKey;
        clearable: boolean;
        persistentClear: boolean;
        singleLine: boolean;
        persistentPlaceholder: boolean;
        persistentCounter: boolean;
        chips: boolean;
        closableChips: boolean;
        closeText: string;
        openText: string;
        hideNoData: boolean;
        hideSelected: boolean;
        menuIcon: IconValue;
        openOnClear: boolean;
        clearOnSelect: boolean;
    } & {
        search?: string | undefined;
        name?: string | undefined;
        id?: string | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxWidth?: string | number | undefined;
        minWidth?: string | number | undefined;
        loading?: string | boolean | undefined;
        label?: string | undefined;
        prefix?: string | undefined;
        class?: any;
        theme?: string | undefined;
        placeholder?: string | undefined;
        counter?: string | number | boolean | undefined;
        'onUpdate:focused'?: ((args_0: boolean) => void) | undefined;
        validateOn?: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        prependInnerIcon?: IconValue | undefined;
        'onClick:clear'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:append'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prepend'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:appendInner'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prependInner'?: ((args_0: MouseEvent) => void) | undefined;
        centerAffix?: boolean | undefined;
        hint?: string | undefined;
        hideDetails?: boolean | "auto" | undefined;
        customFilter?: FilterFunction | undefined;
        customKeyFilter?: FilterKeyFunctions | undefined;
        suffix?: string | undefined;
        counterValue?: number | ((value: any) => number) | undefined;
        modelModifiers?: Record<string, boolean> | undefined;
        listProps?: (Partial<{
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            nav: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            tag: string;
            lines: false | "one" | "two" | "three";
            mandatory: boolean;
            rounded: string | number | boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            valueComparator: typeof deepEqual;
            activatable: boolean;
            selectable: boolean;
            selectStrategy: SelectStrategyProp;
            openStrategy: OpenStrategyProp;
            returnObject: boolean;
            itemType: string;
        }> & Omit<{
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            nav: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            tag: string;
            lines: false | "one" | "two" | "three";
            mandatory: boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            valueComparator: typeof deepEqual;
            activatable: boolean;
            selectable: boolean;
            selectStrategy: SelectStrategyProp;
            openStrategy: OpenStrategyProp;
            returnObject: boolean;
            itemType: string;
            height?: string | number | undefined;
            width?: string | number | undefined;
            border?: string | number | boolean | undefined;
            color?: string | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            activated?: any;
            class?: any;
            theme?: string | undefined;
            elevation?: string | number | undefined;
            rounded?: string | number | boolean | undefined;
            baseColor?: string | undefined;
            bgColor?: string | undefined;
            activeColor?: string | undefined;
            activeClass?: string | undefined;
            activeStrategy?: ActiveStrategyProp | undefined;
            collapseIcon?: IconValue | undefined;
            expandIcon?: IconValue | undefined;
            "onUpdate:activated"?: ((value: unknown) => any) | undefined;
            "onClick:activate"?: ((value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => any) | undefined;
        } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            nav: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            tag: string;
            lines: false | "one" | "two" | "three";
            mandatory: boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            valueComparator: typeof deepEqual;
            activatable: boolean;
            selectable: boolean;
            selectStrategy: SelectStrategyProp;
            openStrategy: OpenStrategyProp;
            returnObject: boolean;
            itemType: string;
        } & {
            height?: string | number | undefined;
            width?: string | number | undefined;
            border?: string | number | boolean | undefined;
            color?: string | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            activated?: any;
            class?: any;
            theme?: string | undefined;
            elevation?: string | number | undefined;
            rounded?: string | number | boolean | undefined;
            baseColor?: string | undefined;
            bgColor?: string | undefined;
            activeColor?: string | undefined;
            activeClass?: string | undefined;
            activeStrategy?: ActiveStrategyProp | undefined;
            collapseIcon?: IconValue | undefined;
            expandIcon?: IconValue | undefined;
        } & {
            "onUpdate:activated"?: ((value: unknown) => any) | undefined;
            "onClick:activate"?: ((value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => any) | undefined;
        }, "variant" | "nav" | "style" | "disabled" | "tag" | "lines" | "mandatory" | "rounded" | "tile" | "density" | "slim" | "valueComparator" | "activatable" | "selectable" | "selectStrategy" | "openStrategy" | "returnObject" | "itemType"> & {
            items?: readonly any[] | undefined;
            itemTitle?: SelectItemKey<any>;
            itemValue?: SelectItemKey<any>;
            itemChildren?: SelectItemKey<any>;
            itemProps?: SelectItemKey<any>;
            selected?: unknown;
            'onUpdate:selected'?: ((value: unknown) => void) | undefined;
            'onClick:open'?: (value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => void;
            'onClick:select'?: (value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => void;
            opened?: unknown;
            'onUpdate:opened'?: ((value: unknown) => void) | undefined;
        } & {
            $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
                title?: ((arg: ListItemTitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                append?: ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                prepend?: ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                subtitle?: ((arg: ListItemSubtitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                default?: (() => vue.VNodeChild) | undefined;
                item?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                divider?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                subheader?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                header?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
            };
            'v-slots'?: {
                title?: false | ((arg: ListItemTitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                append?: false | ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                prepend?: false | ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                subtitle?: false | ((arg: ListItemSubtitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                default?: false | (() => vue.VNodeChild) | undefined;
                item?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                divider?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                subheader?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                header?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
            } | undefined;
        } & {
            "v-slot:title"?: false | ((arg: ListItemTitleSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:append"?: false | ((arg: ListItemSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:prepend"?: false | ((arg: ListItemSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:item"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            "v-slot:divider"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            "v-slot:subheader"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            "v-slot:header"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
        }) | undefined;
        menuProps?: (Partial<{
            location: Anchor | undefined;
            origin: "auto" | Anchor | "overlap";
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
        }> & Omit<{
            location: Anchor | undefined;
            origin: "auto" | Anchor | "overlap";
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
            offset?: string | number | number[] | undefined;
            id?: string | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
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
            "v-slot:default"?: false | ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNodeChild) | undefined;
            "v-slot:activator"?: false | ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNodeChild) | undefined;
            "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
        } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
            location: Anchor | undefined;
            origin: "auto" | Anchor | "overlap";
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
            offset?: string | number | number[] | undefined;
            id?: string | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
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
        }, "location" | "origin" | "transition" | "zIndex" | "style" | "eager" | "disabled" | "persistent" | "modelValue" | "locationStrategy" | "scrollStrategy" | "closeDelay" | "openDelay" | "activatorProps" | "openOnClick" | "openOnHover" | "openOnFocus" | "closeOnContentClick" | "closeOnBack" | "contained" | "noClickAnimation" | "scrim" | "submenu">) | undefined;
        itemColor?: string | undefined;
        autoSelectFirst?: boolean | "exact" | undefined;
    } & {
        "onUpdate:focused"?: ((focused: boolean) => any) | undefined;
        "onUpdate:menu"?: ((value: boolean) => any) | undefined;
        "onUpdate:search"?: ((value: any) => any) | undefined;
    }, {
        flat: boolean;
        reverse: boolean;
        variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
        type: string;
        error: boolean;
        active: boolean;
        direction: "horizontal" | "vertical";
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        menu: boolean;
        style: vue.StyleValue;
        role: string;
        autofocus: boolean;
        eager: boolean;
        disabled: boolean;
        readonly: boolean | null;
        messages: string | readonly string[];
        noDataText: string;
        focused: boolean;
        errorMessages: string | readonly string[] | null;
        maxErrors: string | number;
        rules: readonly ValidationRule[];
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        valueComparator: typeof deepEqual;
        clearIcon: IconValue;
        centerAffix: boolean;
        hideSpinButtons: boolean;
        persistentHint: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        filterKeys: FilterKeys;
        itemChildren: SelectItemKey;
        clearable: boolean;
        persistentClear: boolean;
        singleLine: boolean;
        persistentPlaceholder: boolean;
        persistentCounter: boolean;
        chips: boolean;
        closableChips: boolean;
        closeText: string;
        openText: string;
        hideNoData: boolean;
        hideSelected: boolean;
        menuIcon: IconValue;
        openOnClear: boolean;
        clearOnSelect: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        message: (arg: VMessageSlot) => vue.VNode[];
        clear: (arg: DefaultInputSlot & {
            props: Record<string, any>;
        }) => vue.VNode[];
        details: (arg: VInputSlot) => vue.VNode[];
        label: (arg: DefaultInputSlot & {
            label: string | undefined;
            props: Record<string, any>;
        }) => vue.VNode[];
        append: (arg: VInputSlot) => vue.VNode[];
        prepend: (arg: VInputSlot) => vue.VNode[];
        loader: (arg: LoaderSlotProps) => vue.VNode[];
        'prepend-inner': (arg: DefaultInputSlot) => vue.VNode[];
        'append-inner': (arg: DefaultInputSlot) => vue.VNode[];
        item: (arg: {
            item: ListItem<unknown>;
            index: number;
            props: Record<string, unknown>;
        }) => vue.VNode[];
        chip: (arg: {
            item: ListItem<unknown>;
            index: number;
            props: Record<string, unknown>;
        }) => vue.VNode[];
        selection: (arg: {
            item: ListItem<unknown>;
            index: number;
        }) => vue.VNode[];
        'prepend-item': () => vue.VNode[];
        'append-item': () => vue.VNode[];
        'no-data': () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        flat: boolean;
        reverse: boolean;
        variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
        type: string;
        error: boolean;
        active: boolean;
        direction: "horizontal" | "vertical";
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        menu: boolean;
        style: vue.StyleValue;
        role: string;
        autofocus: boolean;
        eager: boolean;
        disabled: boolean;
        readonly: boolean | null;
        messages: string | readonly string[];
        noDataText: string;
        focused: boolean;
        errorMessages: string | readonly string[] | null;
        maxErrors: string | number;
        rules: readonly ValidationRule[];
        tile: boolean;
        density: Density;
        valueComparator: typeof deepEqual;
        clearIcon: IconValue;
        hideSpinButtons: boolean;
        persistentHint: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        filterKeys: FilterKeys;
        itemChildren: SelectItemKey;
        clearable: boolean;
        persistentClear: boolean;
        singleLine: boolean;
        persistentPlaceholder: boolean;
        persistentCounter: boolean;
        chips: boolean;
        closableChips: boolean;
        closeText: string;
        openText: string;
        hideNoData: boolean;
        hideSelected: boolean;
        menuIcon: IconValue;
        openOnClear: boolean;
        clearOnSelect: boolean;
    } & {
        search?: string | undefined;
        name?: string | undefined;
        id?: string | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxWidth?: string | number | undefined;
        minWidth?: string | number | undefined;
        loading?: string | boolean | undefined;
        label?: string | undefined;
        prefix?: string | undefined;
        class?: any;
        theme?: string | undefined;
        placeholder?: string | undefined;
        counter?: string | number | boolean | undefined;
        'onUpdate:focused'?: ((args_0: boolean) => void) | undefined;
        validateOn?: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        prependInnerIcon?: IconValue | undefined;
        'onClick:clear'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:append'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prepend'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:appendInner'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prependInner'?: ((args_0: MouseEvent) => void) | undefined;
        centerAffix?: boolean | undefined;
        hint?: string | undefined;
        hideDetails?: boolean | "auto" | undefined;
        customFilter?: FilterFunction | undefined;
        customKeyFilter?: FilterKeyFunctions | undefined;
        suffix?: string | undefined;
        counterValue?: number | ((value: any) => number) | undefined;
        modelModifiers?: Record<string, boolean> | undefined;
        listProps?: (Partial<{
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            nav: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            tag: string;
            lines: false | "one" | "two" | "three";
            mandatory: boolean;
            rounded: string | number | boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            valueComparator: typeof deepEqual;
            activatable: boolean;
            selectable: boolean;
            selectStrategy: SelectStrategyProp;
            openStrategy: OpenStrategyProp;
            returnObject: boolean;
            itemType: string;
        }> & Omit<{
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            nav: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            tag: string;
            lines: false | "one" | "two" | "three";
            mandatory: boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            valueComparator: typeof deepEqual;
            activatable: boolean;
            selectable: boolean;
            selectStrategy: SelectStrategyProp;
            openStrategy: OpenStrategyProp;
            returnObject: boolean;
            itemType: string;
            height?: string | number | undefined;
            width?: string | number | undefined;
            border?: string | number | boolean | undefined;
            color?: string | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            activated?: any;
            class?: any;
            theme?: string | undefined;
            elevation?: string | number | undefined;
            rounded?: string | number | boolean | undefined;
            baseColor?: string | undefined;
            bgColor?: string | undefined;
            activeColor?: string | undefined;
            activeClass?: string | undefined;
            activeStrategy?: ActiveStrategyProp | undefined;
            collapseIcon?: IconValue | undefined;
            expandIcon?: IconValue | undefined;
            "onUpdate:activated"?: ((value: unknown) => any) | undefined;
            "onClick:activate"?: ((value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => any) | undefined;
        } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
            variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
            nav: boolean;
            style: vue.StyleValue;
            disabled: boolean;
            tag: string;
            lines: false | "one" | "two" | "three";
            mandatory: boolean;
            tile: boolean;
            density: Density;
            slim: boolean;
            valueComparator: typeof deepEqual;
            activatable: boolean;
            selectable: boolean;
            selectStrategy: SelectStrategyProp;
            openStrategy: OpenStrategyProp;
            returnObject: boolean;
            itemType: string;
        } & {
            height?: string | number | undefined;
            width?: string | number | undefined;
            border?: string | number | boolean | undefined;
            color?: string | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
            activated?: any;
            class?: any;
            theme?: string | undefined;
            elevation?: string | number | undefined;
            rounded?: string | number | boolean | undefined;
            baseColor?: string | undefined;
            bgColor?: string | undefined;
            activeColor?: string | undefined;
            activeClass?: string | undefined;
            activeStrategy?: ActiveStrategyProp | undefined;
            collapseIcon?: IconValue | undefined;
            expandIcon?: IconValue | undefined;
        } & {
            "onUpdate:activated"?: ((value: unknown) => any) | undefined;
            "onClick:activate"?: ((value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => any) | undefined;
        }, "variant" | "nav" | "style" | "disabled" | "tag" | "lines" | "mandatory" | "rounded" | "tile" | "density" | "slim" | "valueComparator" | "activatable" | "selectable" | "selectStrategy" | "openStrategy" | "returnObject" | "itemType"> & {
            items?: readonly any[] | undefined;
            itemTitle?: SelectItemKey<any>;
            itemValue?: SelectItemKey<any>;
            itemChildren?: SelectItemKey<any>;
            itemProps?: SelectItemKey<any>;
            selected?: unknown;
            'onUpdate:selected'?: ((value: unknown) => void) | undefined;
            'onClick:open'?: (value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => void;
            'onClick:select'?: (value: {
                id: unknown;
                value: boolean;
                path: unknown[];
            }) => void;
            opened?: unknown;
            'onUpdate:opened'?: ((value: unknown) => void) | undefined;
        } & {
            $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
                title?: ((arg: ListItemTitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                append?: ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                prepend?: ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                subtitle?: ((arg: ListItemSubtitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                default?: (() => vue.VNodeChild) | undefined;
                item?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                divider?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                subheader?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                header?: ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
            };
            'v-slots'?: {
                title?: false | ((arg: ListItemTitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                append?: false | ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                prepend?: false | ((arg: ListItemSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                subtitle?: false | ((arg: ListItemSubtitleSlot & {
                    item: any;
                }) => vue.VNodeChild) | undefined;
                default?: false | (() => vue.VNodeChild) | undefined;
                item?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                divider?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                subheader?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
                header?: false | ((arg: {
                    props: InternalListItem["props"];
                }) => vue.VNodeChild) | undefined;
            } | undefined;
        } & {
            "v-slot:title"?: false | ((arg: ListItemTitleSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:append"?: false | ((arg: ListItemSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:prepend"?: false | ((arg: ListItemSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
            "v-slot:item"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            "v-slot:divider"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            "v-slot:subheader"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            "v-slot:header"?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
        }) | undefined;
        menuProps?: (Partial<{
            location: Anchor | undefined;
            origin: "auto" | Anchor | "overlap";
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
        }> & Omit<{
            location: Anchor | undefined;
            origin: "auto" | Anchor | "overlap";
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
            offset?: string | number | number[] | undefined;
            id?: string | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
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
            "v-slot:default"?: false | ((arg: {
                isActive: vue.Ref<boolean>;
            }) => vue.VNodeChild) | undefined;
            "v-slot:activator"?: false | ((arg: {
                isActive: boolean;
                props: Record<string, any>;
                targetRef: TemplateRef;
            }) => vue.VNodeChild) | undefined;
            "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
        } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
            location: Anchor | undefined;
            origin: "auto" | Anchor | "overlap";
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
            offset?: string | number | number[] | undefined;
            id?: string | undefined;
            height?: string | number | undefined;
            width?: string | number | undefined;
            maxHeight?: string | number | undefined;
            maxWidth?: string | number | undefined;
            minHeight?: string | number | undefined;
            minWidth?: string | number | undefined;
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
        }, "location" | "origin" | "transition" | "zIndex" | "style" | "eager" | "disabled" | "persistent" | "modelValue" | "locationStrategy" | "scrollStrategy" | "closeDelay" | "openDelay" | "activatorProps" | "openOnClick" | "openOnHover" | "openOnFocus" | "closeOnContentClick" | "closeOnBack" | "contained" | "noClickAnimation" | "scrim" | "submenu">) | undefined;
        itemColor?: string | undefined;
        autoSelectFirst?: boolean | "exact" | undefined;
    } & {
        "onUpdate:focused"?: ((focused: boolean) => any) | undefined;
        "onUpdate:menu"?: ((value: boolean) => any) | undefined;
        "onUpdate:search"?: ((value: any) => any) | undefined;
    }, any, {}, {}, {}, {
        flat: boolean;
        reverse: boolean;
        variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
        type: string;
        error: boolean;
        active: boolean;
        direction: "horizontal" | "vertical";
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        menu: boolean;
        style: vue.StyleValue;
        role: string;
        autofocus: boolean;
        eager: boolean;
        disabled: boolean;
        readonly: boolean | null;
        messages: string | readonly string[];
        noDataText: string;
        focused: boolean;
        errorMessages: string | readonly string[] | null;
        maxErrors: string | number;
        rules: readonly ValidationRule[];
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        valueComparator: typeof deepEqual;
        clearIcon: IconValue;
        centerAffix: boolean;
        hideSpinButtons: boolean;
        persistentHint: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        filterKeys: FilterKeys;
        itemChildren: SelectItemKey;
        clearable: boolean;
        persistentClear: boolean;
        singleLine: boolean;
        persistentPlaceholder: boolean;
        persistentCounter: boolean;
        chips: boolean;
        closableChips: boolean;
        closeText: string;
        openText: string;
        hideNoData: boolean;
        hideSelected: boolean;
        menuIcon: IconValue;
        openOnClear: boolean;
        clearOnSelect: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    flat: boolean;
    reverse: boolean;
    variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
    type: string;
    error: boolean;
    active: boolean;
    direction: "horizontal" | "vertical";
    transition: string | boolean | (vue.TransitionProps & {
        component?: vue.Component;
    });
    menu: boolean;
    style: vue.StyleValue;
    role: string;
    autofocus: boolean;
    eager: boolean;
    disabled: boolean;
    readonly: boolean | null;
    messages: string | readonly string[];
    noDataText: string;
    focused: boolean;
    errorMessages: string | readonly string[] | null;
    maxErrors: string | number;
    rules: readonly ValidationRule[];
    tile: boolean;
    density: Density;
    valueComparator: typeof deepEqual;
    clearIcon: IconValue;
    hideSpinButtons: boolean;
    persistentHint: boolean;
    filterMode: FilterMode;
    noFilter: boolean;
    filterKeys: FilterKeys;
    itemChildren: SelectItemKey;
    clearable: boolean;
    persistentClear: boolean;
    singleLine: boolean;
    persistentPlaceholder: boolean;
    persistentCounter: boolean;
    chips: boolean;
    closableChips: boolean;
    closeText: string;
    openText: string;
    hideNoData: boolean;
    hideSelected: boolean;
    menuIcon: IconValue;
    openOnClear: boolean;
    clearOnSelect: boolean;
} & {
    search?: string | undefined;
    name?: string | undefined;
    id?: string | undefined;
    width?: string | number | undefined;
    color?: string | undefined;
    maxWidth?: string | number | undefined;
    minWidth?: string | number | undefined;
    loading?: string | boolean | undefined;
    label?: string | undefined;
    prefix?: string | undefined;
    class?: any;
    theme?: string | undefined;
    placeholder?: string | undefined;
    counter?: string | number | boolean | undefined;
    'onUpdate:focused'?: ((args_0: boolean) => void) | undefined;
    validateOn?: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
    rounded?: string | number | boolean | undefined;
    baseColor?: string | undefined;
    bgColor?: string | undefined;
    prependIcon?: IconValue | undefined;
    appendIcon?: IconValue | undefined;
    prependInnerIcon?: IconValue | undefined;
    'onClick:clear'?: ((args_0: MouseEvent) => void) | undefined;
    'onClick:append'?: ((args_0: MouseEvent) => void) | undefined;
    'onClick:prepend'?: ((args_0: MouseEvent) => void) | undefined;
    'onClick:appendInner'?: ((args_0: MouseEvent) => void) | undefined;
    'onClick:prependInner'?: ((args_0: MouseEvent) => void) | undefined;
    centerAffix?: boolean | undefined;
    hint?: string | undefined;
    hideDetails?: boolean | "auto" | undefined;
    customFilter?: FilterFunction | undefined;
    customKeyFilter?: FilterKeyFunctions | undefined;
    suffix?: string | undefined;
    counterValue?: number | ((value: any) => number) | undefined;
    modelModifiers?: Record<string, boolean> | undefined;
    listProps?: (Partial<{
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    }> & Omit<{
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        activated?: any;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
        activeStrategy?: ActiveStrategyProp | undefined;
        collapseIcon?: IconValue | undefined;
        expandIcon?: IconValue | undefined;
        "onUpdate:activated"?: ((value: unknown) => any) | undefined;
        "onClick:activate"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
    } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        openStrategy: OpenStrategyProp;
        returnObject: boolean;
        itemType: string;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        activated?: any;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        activeClass?: string | undefined;
        activeStrategy?: ActiveStrategyProp | undefined;
        collapseIcon?: IconValue | undefined;
        expandIcon?: IconValue | undefined;
    } & {
        "onUpdate:activated"?: ((value: unknown) => any) | undefined;
        "onClick:activate"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
    }, "variant" | "nav" | "style" | "disabled" | "tag" | "lines" | "mandatory" | "rounded" | "tile" | "density" | "slim" | "valueComparator" | "activatable" | "selectable" | "selectStrategy" | "openStrategy" | "returnObject" | "itemType"> & {
        items?: readonly any[] | undefined;
        itemTitle?: SelectItemKey<any>;
        itemValue?: SelectItemKey<any>;
        itemChildren?: SelectItemKey<any>;
        itemProps?: SelectItemKey<any>;
        selected?: unknown;
        'onUpdate:selected'?: ((value: unknown) => void) | undefined;
        'onClick:open'?: (value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => void;
        'onClick:select'?: (value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => void;
        opened?: unknown;
        'onUpdate:opened'?: ((value: unknown) => void) | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            title?: ((arg: ListItemTitleSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            append?: ((arg: ListItemSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            prepend?: ((arg: ListItemSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            subtitle?: ((arg: ListItemSubtitleSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            item?: ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            divider?: ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            subheader?: ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            header?: ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            title?: false | ((arg: ListItemTitleSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            append?: false | ((arg: ListItemSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            prepend?: false | ((arg: ListItemSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            subtitle?: false | ((arg: ListItemSubtitleSlot & {
                item: any;
            }) => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            item?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            divider?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            subheader?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
            header?: false | ((arg: {
                props: InternalListItem["props"];
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:title"?: false | ((arg: ListItemTitleSlot & {
            item: any;
        }) => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | ((arg: ListItemSlot & {
            item: any;
        }) => vue.VNodeChild) | undefined;
        "v-slot:prepend"?: false | ((arg: ListItemSlot & {
            item: any;
        }) => vue.VNodeChild) | undefined;
        "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot & {
            item: any;
        }) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:item"?: false | ((arg: {
            props: InternalListItem["props"];
        }) => vue.VNodeChild) | undefined;
        "v-slot:divider"?: false | ((arg: {
            props: InternalListItem["props"];
        }) => vue.VNodeChild) | undefined;
        "v-slot:subheader"?: false | ((arg: {
            props: InternalListItem["props"];
        }) => vue.VNodeChild) | undefined;
        "v-slot:header"?: false | ((arg: {
            props: InternalListItem["props"];
        }) => vue.VNodeChild) | undefined;
    }) | undefined;
    menuProps?: (Partial<{
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
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
    }> & Omit<{
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
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
        offset?: string | number | number[] | undefined;
        id?: string | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
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
        "v-slot:default"?: false | ((arg: {
            isActive: vue.Ref<boolean>;
        }) => vue.VNodeChild) | undefined;
        "v-slot:activator"?: false | ((arg: {
            isActive: boolean;
            props: Record<string, any>;
            targetRef: TemplateRef;
        }) => vue.VNodeChild) | undefined;
        "onUpdate:modelValue"?: ((value: boolean) => any) | undefined;
    } & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        location: Anchor | undefined;
        origin: "auto" | Anchor | "overlap";
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
        offset?: string | number | number[] | undefined;
        id?: string | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
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
    }, "location" | "origin" | "transition" | "zIndex" | "style" | "eager" | "disabled" | "persistent" | "modelValue" | "locationStrategy" | "scrollStrategy" | "closeDelay" | "openDelay" | "activatorProps" | "openOnClick" | "openOnHover" | "openOnFocus" | "closeOnContentClick" | "closeOnBack" | "contained" | "noClickAnimation" | "scrim" | "submenu">) | undefined;
    itemColor?: string | undefined;
    autoSelectFirst?: boolean | "exact" | undefined;
} & {
    "onUpdate:focused"?: ((focused: boolean) => any) | undefined;
    "onUpdate:menu"?: ((value: boolean) => any) | undefined;
    "onUpdate:search"?: ((value: any) => any) | undefined;
}, any, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
    'update:focused': (focused: boolean) => true;
    'update:search': (value: any) => true;
    'update:modelValue': (value: any) => true;
    'update:menu': (value: boolean) => true;
}, "multiple" | "$children" | "v-slots" | "modelValue" | "items" | "v-slot:prepend" | "v-slot:append" | "update:modelValue" | "v-slot:loader" | "v-slot:label" | "v-slot:message" | "v-slot:details" | "returnObject" | "v-slot:item" | "itemTitle" | "itemValue" | "itemProps" | "v-slot:clear" | "v-slot:prepend-inner" | "v-slot:append-inner" | "v-slot:chip" | "v-slot:selection" | "v-slot:prepend-item" | "v-slot:append-item" | "v-slot:no-data">, string, {
    flat: boolean;
    reverse: boolean;
    variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
    type: string;
    error: boolean;
    active: boolean;
    direction: "horizontal" | "vertical";
    transition: string | boolean | (vue.TransitionProps & {
        component?: vue.Component;
    });
    menu: boolean;
    style: vue.StyleValue;
    role: string;
    autofocus: boolean;
    eager: boolean;
    disabled: boolean;
    readonly: boolean | null;
    messages: string | readonly string[];
    noDataText: string;
    focused: boolean;
    errorMessages: string | readonly string[] | null;
    maxErrors: string | number;
    rules: readonly ValidationRule[];
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    valueComparator: typeof deepEqual;
    clearIcon: IconValue;
    centerAffix: boolean;
    hideSpinButtons: boolean;
    persistentHint: boolean;
    filterMode: FilterMode;
    noFilter: boolean;
    filterKeys: FilterKeys;
    itemChildren: SelectItemKey;
    clearable: boolean;
    persistentClear: boolean;
    singleLine: boolean;
    persistentPlaceholder: boolean;
    persistentCounter: boolean;
    chips: boolean;
    closableChips: boolean;
    closeText: string;
    openText: string;
    hideNoData: boolean;
    hideSelected: boolean;
    menuIcon: IconValue;
    openOnClear: boolean;
    clearOnSelect: boolean;
}, {}, string, vue.SlotsType<Partial<{
    message: (arg: VMessageSlot) => vue.VNode[];
    clear: (arg: DefaultInputSlot & {
        props: Record<string, any>;
    }) => vue.VNode[];
    details: (arg: VInputSlot) => vue.VNode[];
    label: (arg: DefaultInputSlot & {
        label: string | undefined;
        props: Record<string, any>;
    }) => vue.VNode[];
    append: (arg: VInputSlot) => vue.VNode[];
    prepend: (arg: VInputSlot) => vue.VNode[];
    loader: (arg: LoaderSlotProps) => vue.VNode[];
    'prepend-inner': (arg: DefaultInputSlot) => vue.VNode[];
    'append-inner': (arg: DefaultInputSlot) => vue.VNode[];
    item: (arg: {
        item: ListItem<unknown>;
        index: number;
        props: Record<string, unknown>;
    }) => vue.VNode[];
    chip: (arg: {
        item: ListItem<unknown>;
        index: number;
        props: Record<string, unknown>;
    }) => vue.VNode[];
    selection: (arg: {
        item: ListItem<unknown>;
        index: number;
    }) => vue.VNode[];
    'prepend-item': () => vue.VNode[];
    'append-item': () => vue.VNode[];
    'no-data': () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & (new <T extends readonly any[], Item = ItemType<T>, ReturnObject extends boolean = false, Multiple extends boolean = false, V extends Value<Item, ReturnObject, Multiple> = Value<Item, ReturnObject, Multiple>>(props: {
    items?: T;
    itemTitle?: SelectItemKey<ItemType<T>>;
    itemValue?: SelectItemKey<ItemType<T>>;
    itemProps?: SelectItemKey<ItemType<T>>;
    returnObject?: ReturnObject;
    multiple?: Multiple;
    modelValue?: V | null;
    "onUpdate:modelValue"?: (value: V) => void;
}, slots: Omit<VInputSlots & VFieldSlots, "default"> & {
    item: {
        item: ListItem<Item>;
        index: number;
        props: Record<string, unknown>;
    };
    chip: {
        item: ListItem<Item>;
        index: number;
        props: Record<string, unknown>;
    };
    selection: {
        item: ListItem<Item>;
        index: number;
    };
    "prepend-item": never;
    "append-item": never;
    "no-data": never;
}) => GenericProps<typeof props, typeof slots>) & FilterPropsOptions<{
    transition: Omit<{
        type: PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
    };
    flat: BooleanConstructor;
    reverse: BooleanConstructor;
    variant: {
        type: PropType<"filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled">;
        default: string;
        validator: (v: any) => boolean;
    };
    name: StringConstructor;
    type: {
        type: StringConstructor;
        default: string;
    };
    error: BooleanConstructor;
    id: StringConstructor;
    width: (StringConstructor | NumberConstructor)[];
    active: BooleanConstructor;
    color: StringConstructor;
    direction: {
        type: PropType<"horizontal" | "vertical">;
        default: string;
        validator: (v: any) => boolean;
    };
    maxWidth: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    loading: (StringConstructor | BooleanConstructor)[];
    label: StringConstructor;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    prefix: StringConstructor;
    role: {
        type: PropType<string>;
        default: string;
    };
    autofocus: BooleanConstructor;
    disabled: {
        type: BooleanConstructor;
        default: null;
    };
    readonly: {
        type: PropType<boolean | null>;
        default: null;
    };
    class: PropType<ClassValue>;
    theme: StringConstructor;
    placeholder: StringConstructor;
    messages: {
        type: PropType<string | readonly string[]>;
        default: () => never[];
    };
    counter: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    focused: BooleanConstructor;
    'onUpdate:focused': PropType<(args_0: boolean) => void>;
    errorMessages: {
        type: PropType<string | readonly string[] | null>;
        default: () => never[];
    };
    maxErrors: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    rules: {
        type: PropType<readonly ValidationRule[]>;
        default: () => never[];
    };
    modelValue: {
        type: PropType<any>;
        default: any;
    };
    validateOn: PropType<ValidationProps["validateOn"]>;
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    baseColor: StringConstructor;
    bgColor: StringConstructor;
    prependIcon: PropType<IconValue>;
    appendIcon: PropType<IconValue>;
    clearIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    prependInnerIcon: PropType<IconValue>;
    'onClick:clear': PropType<(args_0: MouseEvent) => void>;
    'onClick:append': PropType<(args_0: MouseEvent) => void>;
    'onClick:prepend': PropType<(args_0: MouseEvent) => void>;
    'onClick:appendInner': PropType<(args_0: MouseEvent) => void>;
    'onClick:prependInner': PropType<(args_0: MouseEvent) => void>;
    centerAffix: {
        type: BooleanConstructor;
        default: undefined;
    };
    hideSpinButtons: BooleanConstructor;
    hint: StringConstructor;
    persistentHint: BooleanConstructor;
    hideDetails: PropType<boolean | "auto">;
    clearable: BooleanConstructor;
    persistentClear: BooleanConstructor;
    singleLine: BooleanConstructor;
    persistentPlaceholder: BooleanConstructor;
    persistentCounter: BooleanConstructor;
    suffix: StringConstructor;
    counterValue: PropType<number | ((value: any) => number)>;
    modelModifiers: PropType<Record<string, boolean>>;
    items: {
        type: PropType<ItemProps["items"]>;
        default: () => never[];
    };
    itemTitle: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemValue: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemChildren: Omit<{
        type: PropType<SelectItemKey>;
        default: string;
    }, "type" | "default"> & {
        type: PropType<SelectItemKey>;
        default: NonNullable<SelectItemKey>;
    };
    itemProps: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    returnObject: BooleanConstructor;
    valueComparator: {
        type: PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
    chips: BooleanConstructor;
    closableChips: BooleanConstructor;
    closeText: {
        type: StringConstructor;
        default: string;
    };
    openText: {
        type: StringConstructor;
        default: string;
    };
    eager: BooleanConstructor;
    hideNoData: BooleanConstructor;
    hideSelected: BooleanConstructor;
    listProps: {
        type: PropType<VList["$props"]>;
    };
    menu: BooleanConstructor;
    menuIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    menuProps: {
        type: PropType<VMenu["$props"]>;
    };
    multiple: BooleanConstructor;
    noDataText: {
        type: StringConstructor;
        default: string;
    };
    openOnClear: BooleanConstructor;
    itemColor: StringConstructor;
    customFilter: PropType<FilterFunction>;
    customKeyFilter: PropType<FilterKeyFunctions>;
    filterKeys: {
        type: PropType<FilterKeys>;
        default: NonNullable<FilterKeys>;
    };
    filterMode: {
        type: PropType<FilterMode>;
        default: string;
    };
    noFilter: BooleanConstructor;
    autoSelectFirst: {
        type: PropType<boolean | "exact">;
    };
    clearOnSelect: BooleanConstructor;
    search: StringConstructor;
}, vue.ExtractPropTypes<{
    transition: Omit<{
        type: PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    }, "type" | "default"> & {
        type: PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
    };
    flat: BooleanConstructor;
    reverse: BooleanConstructor;
    variant: {
        type: PropType<"filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled">;
        default: string;
        validator: (v: any) => boolean;
    };
    name: StringConstructor;
    type: {
        type: StringConstructor;
        default: string;
    };
    error: BooleanConstructor;
    id: StringConstructor;
    width: (StringConstructor | NumberConstructor)[];
    active: BooleanConstructor;
    color: StringConstructor;
    direction: {
        type: PropType<"horizontal" | "vertical">;
        default: string;
        validator: (v: any) => boolean;
    };
    maxWidth: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    loading: (StringConstructor | BooleanConstructor)[];
    label: StringConstructor;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    prefix: StringConstructor;
    role: {
        type: PropType<string>;
        default: string;
    };
    autofocus: BooleanConstructor;
    disabled: {
        type: BooleanConstructor;
        default: null;
    };
    readonly: {
        type: PropType<boolean | null>;
        default: null;
    };
    class: PropType<ClassValue>;
    theme: StringConstructor;
    placeholder: StringConstructor;
    messages: {
        type: PropType<string | readonly string[]>;
        default: () => never[];
    };
    counter: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    focused: BooleanConstructor;
    'onUpdate:focused': PropType<(args_0: boolean) => void>;
    errorMessages: {
        type: PropType<string | readonly string[] | null>;
        default: () => never[];
    };
    maxErrors: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    rules: {
        type: PropType<readonly ValidationRule[]>;
        default: () => never[];
    };
    modelValue: {
        type: PropType<any>;
        default: any;
    };
    validateOn: PropType<ValidationProps["validateOn"]>;
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    density: {
        type: PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    baseColor: StringConstructor;
    bgColor: StringConstructor;
    prependIcon: PropType<IconValue>;
    appendIcon: PropType<IconValue>;
    clearIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    prependInnerIcon: PropType<IconValue>;
    'onClick:clear': PropType<(args_0: MouseEvent) => void>;
    'onClick:append': PropType<(args_0: MouseEvent) => void>;
    'onClick:prepend': PropType<(args_0: MouseEvent) => void>;
    'onClick:appendInner': PropType<(args_0: MouseEvent) => void>;
    'onClick:prependInner': PropType<(args_0: MouseEvent) => void>;
    centerAffix: {
        type: BooleanConstructor;
        default: undefined;
    };
    hideSpinButtons: BooleanConstructor;
    hint: StringConstructor;
    persistentHint: BooleanConstructor;
    hideDetails: PropType<boolean | "auto">;
    clearable: BooleanConstructor;
    persistentClear: BooleanConstructor;
    singleLine: BooleanConstructor;
    persistentPlaceholder: BooleanConstructor;
    persistentCounter: BooleanConstructor;
    suffix: StringConstructor;
    counterValue: PropType<number | ((value: any) => number)>;
    modelModifiers: PropType<Record<string, boolean>>;
    items: {
        type: PropType<ItemProps["items"]>;
        default: () => never[];
    };
    itemTitle: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemValue: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    itemChildren: Omit<{
        type: PropType<SelectItemKey>;
        default: string;
    }, "type" | "default"> & {
        type: PropType<SelectItemKey>;
        default: NonNullable<SelectItemKey>;
    };
    itemProps: {
        type: PropType<SelectItemKey>;
        default: string;
    };
    returnObject: BooleanConstructor;
    valueComparator: {
        type: PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
    chips: BooleanConstructor;
    closableChips: BooleanConstructor;
    closeText: {
        type: StringConstructor;
        default: string;
    };
    openText: {
        type: StringConstructor;
        default: string;
    };
    eager: BooleanConstructor;
    hideNoData: BooleanConstructor;
    hideSelected: BooleanConstructor;
    listProps: {
        type: PropType<VList["$props"]>;
    };
    menu: BooleanConstructor;
    menuIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    menuProps: {
        type: PropType<VMenu["$props"]>;
    };
    multiple: BooleanConstructor;
    noDataText: {
        type: StringConstructor;
        default: string;
    };
    openOnClear: BooleanConstructor;
    itemColor: StringConstructor;
    customFilter: PropType<FilterFunction>;
    customKeyFilter: PropType<FilterKeyFunctions>;
    filterKeys: {
        type: PropType<FilterKeys>;
        default: NonNullable<FilterKeys>;
    };
    filterMode: {
        type: PropType<FilterMode>;
        default: string;
    };
    noFilter: BooleanConstructor;
    autoSelectFirst: {
        type: PropType<boolean | "exact">;
    };
    clearOnSelect: BooleanConstructor;
    search: StringConstructor;
}>>;
type VAutocomplete = InstanceType<typeof VAutocomplete>;

export { VAutocomplete };
