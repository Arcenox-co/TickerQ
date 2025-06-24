import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, VNodeChild, VNode, PropType, ComponentPublicInstance, FunctionalComponent, DirectiveBinding } from 'vue';
// @ts-ignore
import * as vue_router from 'vue-router';

type ClassValue = any;

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

type Density = null | 'default' | 'comfortable' | 'compact';

declare const allowedVariants: readonly ["elevated", "flat", "tonal", "outlined", "text", "plain"];
type Variant = typeof allowedVariants[number];

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

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
type VListItemSlots = {
    prepend: ListItemSlot;
    append: ListItemSlot;
    default: ListItemSlot;
    title: ListItemTitleSlot;
    subtitle: ListItemSubtitleSlot;
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

interface InternalListItem<T = any> extends ListItem<T> {
    type?: 'item' | 'subheader' | 'divider';
}

declare const VTreeview: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        modelValue: unknown[];
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        indeterminateIcon: IconValue;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        returnObject: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        filterKeys: FilterKeys;
        itemTitle: SelectItemKey;
        itemValue: SelectItemKey;
        itemChildren: SelectItemKey;
        itemProps: SelectItemKey;
        loadingIcon: string;
        openAll: boolean;
    } & {
        search?: string | undefined;
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
        selected?: any;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        falseIcon?: IconValue | undefined;
        trueIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        opened?: any;
        activeStrategy?: ActiveStrategyProp | undefined;
        'onUpdate:opened'?: (() => void) | undefined;
        customFilter?: FilterFunction | undefined;
        customKeyFilter?: FilterKeyFunctions | undefined;
        'onClick:open'?: ((args_0: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => void) | undefined;
        'onClick:select'?: ((args_0: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => void) | undefined;
        openOnClick?: boolean | undefined;
        selectedColor?: string | undefined;
        loadChildren?: ((item: unknown) => Promise<void>) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: unknown) => any) | undefined;
        "onUpdate:activated"?: ((val: unknown) => any) | undefined;
        "onUpdate:selected"?: ((val: unknown) => any) | undefined;
        "onUpdate:opened"?: ((val: unknown) => any) | undefined;
        "onClick:open"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
        "onClick:select"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
        'update:opened': (val: unknown) => true;
        'update:activated': (val: unknown) => true;
        'update:selected': (val: unknown) => true;
        'update:modelValue': (val: unknown) => true;
        'click:open': (value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => true;
        'click:select': (value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => true;
    }, "$children" | "v-slots" | "v-slot:default" | "items" | "v-slot:prepend" | "v-slot:append" | "v-slot:title" | "v-slot:subtitle" | "v-slot:item" | "v-slot:header" | "v-slot:divider" | "v-slot:subheader">, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        modelValue: unknown[];
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        indeterminateIcon: IconValue;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        returnObject: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        filterKeys: FilterKeys;
        itemTitle: SelectItemKey;
        itemValue: SelectItemKey;
        itemChildren: SelectItemKey;
        itemProps: SelectItemKey;
        loadingIcon: string;
        openAll: boolean;
    } & {
        search?: string | undefined;
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
        selected?: any;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        falseIcon?: IconValue | undefined;
        trueIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        opened?: any;
        activeStrategy?: ActiveStrategyProp | undefined;
        'onUpdate:opened'?: (() => void) | undefined;
        customFilter?: FilterFunction | undefined;
        customKeyFilter?: FilterKeyFunctions | undefined;
        'onClick:open'?: ((args_0: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => void) | undefined;
        'onClick:select'?: ((args_0: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => void) | undefined;
        openOnClick?: boolean | undefined;
        selectedColor?: string | undefined;
        loadChildren?: ((item: unknown) => Promise<void>) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: unknown) => any) | undefined;
        "onUpdate:activated"?: ((val: unknown) => any) | undefined;
        "onUpdate:selected"?: ((val: unknown) => any) | undefined;
        "onUpdate:opened"?: ((val: unknown) => any) | undefined;
        "onClick:open"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
        "onClick:select"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
    }, {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        modelValue: unknown[];
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        indeterminateIcon: IconValue;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        returnObject: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        filterKeys: FilterKeys;
        itemTitle: SelectItemKey;
        itemValue: SelectItemKey;
        itemChildren: SelectItemKey;
        itemProps: SelectItemKey;
        openOnClick: boolean;
        loadingIcon: string;
        openAll: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        title: (arg: ListItemTitleSlot & {
            item: unknown;
        }) => vue.VNode[];
        append: (arg: ListItemSlot & {
            item: unknown;
        }) => vue.VNode[];
        prepend: (arg: ListItemSlot & {
            item: unknown;
        }) => vue.VNode[];
        subtitle: (arg: ListItemSubtitleSlot & {
            item: unknown;
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
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        modelValue: unknown[];
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        indeterminateIcon: IconValue;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        returnObject: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        filterKeys: FilterKeys;
        itemTitle: SelectItemKey;
        itemValue: SelectItemKey;
        itemChildren: SelectItemKey;
        itemProps: SelectItemKey;
        loadingIcon: string;
        openAll: boolean;
    } & {
        search?: string | undefined;
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
        selected?: any;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        activeColor?: string | undefined;
        falseIcon?: IconValue | undefined;
        trueIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        opened?: any;
        activeStrategy?: ActiveStrategyProp | undefined;
        'onUpdate:opened'?: (() => void) | undefined;
        customFilter?: FilterFunction | undefined;
        customKeyFilter?: FilterKeyFunctions | undefined;
        'onClick:open'?: ((args_0: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => void) | undefined;
        'onClick:select'?: ((args_0: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => void) | undefined;
        openOnClick?: boolean | undefined;
        selectedColor?: string | undefined;
        loadChildren?: ((item: unknown) => Promise<void>) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: unknown) => any) | undefined;
        "onUpdate:activated"?: ((val: unknown) => any) | undefined;
        "onUpdate:selected"?: ((val: unknown) => any) | undefined;
        "onUpdate:opened"?: ((val: unknown) => any) | undefined;
        "onClick:open"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
        "onClick:select"?: ((value: {
            id: unknown;
            value: boolean;
            path: unknown[];
        }) => any) | undefined;
    }, {}, {}, {}, {}, {
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        lines: false | "one" | "two" | "three";
        mandatory: boolean;
        modelValue: unknown[];
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        valueComparator: typeof deepEqual;
        indeterminateIcon: IconValue;
        activatable: boolean;
        selectable: boolean;
        selectStrategy: SelectStrategyProp;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        returnObject: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        filterKeys: FilterKeys;
        itemTitle: SelectItemKey;
        itemValue: SelectItemKey;
        itemChildren: SelectItemKey;
        itemProps: SelectItemKey;
        openOnClick: boolean;
        loadingIcon: string;
        openAll: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    lines: false | "one" | "two" | "three";
    mandatory: boolean;
    modelValue: unknown[];
    tile: boolean;
    density: Density;
    slim: boolean;
    valueComparator: typeof deepEqual;
    indeterminateIcon: IconValue;
    activatable: boolean;
    selectable: boolean;
    selectStrategy: SelectStrategyProp;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    returnObject: boolean;
    filterMode: FilterMode;
    noFilter: boolean;
    filterKeys: FilterKeys;
    itemTitle: SelectItemKey;
    itemValue: SelectItemKey;
    itemChildren: SelectItemKey;
    itemProps: SelectItemKey;
    loadingIcon: string;
    openAll: boolean;
} & {
    search?: string | undefined;
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
    selected?: any;
    elevation?: string | number | undefined;
    rounded?: string | number | boolean | undefined;
    baseColor?: string | undefined;
    bgColor?: string | undefined;
    activeColor?: string | undefined;
    falseIcon?: IconValue | undefined;
    trueIcon?: IconValue | undefined;
    activeClass?: string | undefined;
    opened?: any;
    activeStrategy?: ActiveStrategyProp | undefined;
    'onUpdate:opened'?: (() => void) | undefined;
    customFilter?: FilterFunction | undefined;
    customKeyFilter?: FilterKeyFunctions | undefined;
    'onClick:open'?: ((args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void) | undefined;
    'onClick:select'?: ((args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void) | undefined;
    openOnClick?: boolean | undefined;
    selectedColor?: string | undefined;
    loadChildren?: ((item: unknown) => Promise<void>) | undefined;
} & {
    "onUpdate:modelValue"?: ((val: unknown) => any) | undefined;
    "onUpdate:activated"?: ((val: unknown) => any) | undefined;
    "onUpdate:selected"?: ((val: unknown) => any) | undefined;
    "onUpdate:opened"?: ((val: unknown) => any) | undefined;
    "onClick:open"?: ((value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => any) | undefined;
    "onClick:select"?: ((value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
    'update:opened': (val: unknown) => true;
    'update:activated': (val: unknown) => true;
    'update:selected': (val: unknown) => true;
    'update:modelValue': (val: unknown) => true;
    'click:open': (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => true;
    'click:select': (value: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => true;
}, "$children" | "v-slots" | "v-slot:default" | "items" | "v-slot:prepend" | "v-slot:append" | "v-slot:title" | "v-slot:subtitle" | "v-slot:item" | "v-slot:header" | "v-slot:divider" | "v-slot:subheader">, string, {
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    lines: false | "one" | "two" | "three";
    mandatory: boolean;
    modelValue: unknown[];
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    slim: boolean;
    valueComparator: typeof deepEqual;
    indeterminateIcon: IconValue;
    activatable: boolean;
    selectable: boolean;
    selectStrategy: SelectStrategyProp;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    returnObject: boolean;
    filterMode: FilterMode;
    noFilter: boolean;
    filterKeys: FilterKeys;
    itemTitle: SelectItemKey;
    itemValue: SelectItemKey;
    itemChildren: SelectItemKey;
    itemProps: SelectItemKey;
    openOnClick: boolean;
    loadingIcon: string;
    openAll: boolean;
}, {}, string, vue.SlotsType<Partial<{
    title: (arg: ListItemTitleSlot & {
        item: unknown;
    }) => vue.VNode[];
    append: (arg: ListItemSlot & {
        item: unknown;
    }) => vue.VNode[];
    prepend: (arg: ListItemSlot & {
        item: unknown;
    }) => vue.VNode[];
    subtitle: (arg: ListItemSubtitleSlot & {
        item: unknown;
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
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & (new <T>(props: {
    items?: T[];
}, slots: VListChildrenSlots<T>) => GenericProps<typeof props, typeof slots>) & FilterPropsOptions<{
    modelValue: {
        type: ArrayConstructor;
        default: () => never[];
    };
    variant: Omit<{
        type: vue.PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    disabled: BooleanConstructor;
    activated: null;
    class: vue.PropType<ClassValue>;
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    lines: {
        type: vue.PropType<"one" | "two" | "three" | false>;
        default: string;
    };
    mandatory: BooleanConstructor;
    selected: null;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    items: {
        type: vue.PropType<ItemProps["items"]>;
        default: () => never[];
    };
    tile: BooleanConstructor;
    density: {
        type: vue.PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    baseColor: StringConstructor;
    bgColor: StringConstructor;
    activeColor: StringConstructor;
    slim: {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    valueComparator: {
        type: vue.PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
    activeClass: StringConstructor;
    opened: null;
    activatable: BooleanConstructor;
    selectable: BooleanConstructor;
    activeStrategy: vue.PropType<ActiveStrategyProp>;
    selectStrategy: {
        type: vue.PropType<SelectStrategyProp>;
        default: NonNullable<SelectStrategyProp>;
    };
    'onUpdate:opened': vue.PropType<() => void>;
    collapseIcon: {
        type: vue.PropType<IconValue>;
        default: NonNullable<IconValue>;
    };
    expandIcon: {
        type: vue.PropType<IconValue>;
        default: NonNullable<IconValue>;
    };
    returnObject: BooleanConstructor;
    itemTitle: {
        type: vue.PropType<SelectItemKey>;
        default: string;
    };
    itemValue: {
        type: vue.PropType<SelectItemKey>;
        default: string;
    };
    itemChildren: {
        type: vue.PropType<SelectItemKey>;
        default: string;
    };
    itemProps: {
        type: vue.PropType<SelectItemKey>;
        default: string;
    };
    'onClick:open': vue.PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    'onClick:select': vue.PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    loadChildren: vue.PropType<(item: unknown) => Promise<void>>;
    loadingIcon: {
        type: StringConstructor;
        default: string;
    };
    openOnClick: {
        type: BooleanConstructor;
        default: undefined;
    };
    indeterminateIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    falseIcon: vue.PropType<IconValue>;
    trueIcon: vue.PropType<IconValue>;
    selectedColor: StringConstructor;
    customFilter: vue.PropType<FilterFunction>;
    customKeyFilter: vue.PropType<FilterKeyFunctions>;
    filterKeys: {
        type: vue.PropType<FilterKeys>;
        default: NonNullable<FilterKeys>;
    };
    filterMode: {
        type: vue.PropType<FilterMode>;
        default: string;
    };
    noFilter: BooleanConstructor;
    openAll: BooleanConstructor;
    search: StringConstructor;
}, vue.ExtractPropTypes<{
    modelValue: {
        type: ArrayConstructor;
        default: () => never[];
    };
    variant: Omit<{
        type: vue.PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    disabled: BooleanConstructor;
    activated: null;
    class: vue.PropType<ClassValue>;
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    lines: {
        type: vue.PropType<"one" | "two" | "three" | false>;
        default: string;
    };
    mandatory: BooleanConstructor;
    selected: null;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    items: {
        type: vue.PropType<ItemProps["items"]>;
        default: () => never[];
    };
    tile: BooleanConstructor;
    density: {
        type: vue.PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    baseColor: StringConstructor;
    bgColor: StringConstructor;
    activeColor: StringConstructor;
    slim: {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    valueComparator: {
        type: vue.PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
    activeClass: StringConstructor;
    opened: null;
    activatable: BooleanConstructor;
    selectable: BooleanConstructor;
    activeStrategy: vue.PropType<ActiveStrategyProp>;
    selectStrategy: {
        type: vue.PropType<SelectStrategyProp>;
        default: NonNullable<SelectStrategyProp>;
    };
    'onUpdate:opened': vue.PropType<() => void>;
    collapseIcon: {
        type: vue.PropType<IconValue>;
        default: NonNullable<IconValue>;
    };
    expandIcon: {
        type: vue.PropType<IconValue>;
        default: NonNullable<IconValue>;
    };
    returnObject: BooleanConstructor;
    itemTitle: {
        type: vue.PropType<SelectItemKey>;
        default: string;
    };
    itemValue: {
        type: vue.PropType<SelectItemKey>;
        default: string;
    };
    itemChildren: {
        type: vue.PropType<SelectItemKey>;
        default: string;
    };
    itemProps: {
        type: vue.PropType<SelectItemKey>;
        default: string;
    };
    'onClick:open': vue.PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    'onClick:select': vue.PropType<(args_0: {
        id: unknown;
        value: boolean;
        path: unknown[];
    }) => void>;
    loadChildren: vue.PropType<(item: unknown) => Promise<void>>;
    loadingIcon: {
        type: StringConstructor;
        default: string;
    };
    openOnClick: {
        type: BooleanConstructor;
        default: undefined;
    };
    indeterminateIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    falseIcon: vue.PropType<IconValue>;
    trueIcon: vue.PropType<IconValue>;
    selectedColor: StringConstructor;
    customFilter: vue.PropType<FilterFunction>;
    customKeyFilter: vue.PropType<FilterKeyFunctions>;
    filterKeys: {
        type: vue.PropType<FilterKeys>;
        default: NonNullable<FilterKeys>;
    };
    filterMode: {
        type: vue.PropType<FilterMode>;
        default: string;
    };
    noFilter: BooleanConstructor;
    openAll: BooleanConstructor;
    search: StringConstructor;
}>>;
type VTreeview = InstanceType<typeof VTreeview>;

declare const VTreeviewItem: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        replace: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        loading: boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        link?: boolean | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
        border?: string | number | boolean | undefined;
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
        lines?: false | "one" | "two" | "three" | undefined;
        onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
        onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        appendAvatar?: string | undefined;
        prependAvatar?: string | undefined;
        subtitle?: string | number | undefined;
        toggleIcon?: IconValue | undefined;
        onToggleExpand?: ((args_0: MouseEvent) => void) | undefined;
    } & {
        $children?: vue.VNodeChild | {
            prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | ((arg: ListItemSlot) => vue.VNodeChild);
        'v-slots'?: {
            prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        replace: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        loading: boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        link?: boolean | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
        border?: string | number | boolean | undefined;
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
        lines?: false | "one" | "two" | "three" | undefined;
        onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
        onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        appendAvatar?: string | undefined;
        prependAvatar?: string | undefined;
        subtitle?: string | number | undefined;
        toggleIcon?: IconValue | undefined;
        onToggleExpand?: ((args_0: MouseEvent) => void) | undefined;
    } & {
        $children?: vue.VNodeChild | {
            prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | ((arg: ListItemSlot) => vue.VNodeChild);
        'v-slots'?: {
            prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    }, {
        replace: boolean;
        link: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        active: boolean;
        loading: boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    }, true, {}, vue.SlotsType<Partial<{
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
        loading: boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    } & {
        link?: boolean | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: boolean | undefined;
        border?: string | number | boolean | undefined;
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
        lines?: false | "one" | "two" | "three" | undefined;
        onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
        onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
        href?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
        activeClass?: string | undefined;
        appendAvatar?: string | undefined;
        prependAvatar?: string | undefined;
        subtitle?: string | number | undefined;
        toggleIcon?: IconValue | undefined;
        onToggleExpand?: ((args_0: MouseEvent) => void) | undefined;
    } & {
        $children?: vue.VNodeChild | {
            prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | ((arg: ListItemSlot) => vue.VNodeChild);
        'v-slots'?: {
            prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
            title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
            subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        replace: boolean;
        link: boolean;
        variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
        exact: boolean;
        active: boolean;
        loading: boolean;
        nav: boolean;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        slim: boolean;
        ripple: boolean | {
            class: string;
        } | undefined;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    replace: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    exact: boolean;
    loading: boolean;
    nav: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    tile: boolean;
    density: Density;
    slim: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
} & {
    link?: boolean | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    active?: boolean | undefined;
    border?: string | number | boolean | undefined;
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
    lines?: false | "one" | "two" | "three" | undefined;
    onClick?: ((args_0: MouseEvent | KeyboardEvent) => void) | undefined;
    onClickOnce?: ((args_0: MouseEvent) => void) | undefined;
    href?: string | undefined;
    elevation?: string | number | undefined;
    rounded?: string | number | boolean | undefined;
    baseColor?: string | undefined;
    activeColor?: string | undefined;
    prependIcon?: IconValue | undefined;
    appendIcon?: IconValue | undefined;
    activeClass?: string | undefined;
    appendAvatar?: string | undefined;
    prependAvatar?: string | undefined;
    subtitle?: string | number | undefined;
    toggleIcon?: IconValue | undefined;
    onToggleExpand?: ((args_0: MouseEvent) => void) | undefined;
} & {
    $children?: vue.VNodeChild | {
        prepend?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        append?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        default?: ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        title?: ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        subtitle?: ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    } | ((arg: ListItemSlot) => vue.VNodeChild);
    'v-slots'?: {
        prepend?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        append?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        default?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
        title?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
        subtitle?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:prepend"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
    "v-slot:append"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
    "v-slot:default"?: false | ((arg: ListItemSlot) => vue.VNodeChild) | undefined;
    "v-slot:title"?: false | ((arg: ListItemTitleSlot) => vue.VNodeChild) | undefined;
    "v-slot:subtitle"?: false | ((arg: ListItemSubtitleSlot) => vue.VNodeChild) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    replace: boolean;
    link: boolean;
    variant: "flat" | "text" | "elevated" | "tonal" | "outlined" | "plain";
    exact: boolean;
    active: boolean;
    loading: boolean;
    nav: boolean;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    slim: boolean;
    ripple: boolean | {
        class: string;
    } | undefined;
}, {}, string, vue.SlotsType<Partial<{
    prepend: (arg: ListItemSlot) => vue.VNode[];
    append: (arg: ListItemSlot) => vue.VNode[];
    default: (arg: ListItemSlot) => vue.VNode[];
    title: (arg: ListItemTitleSlot) => vue.VNode[];
    subtitle: (arg: ListItemSubtitleSlot) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    color: StringConstructor;
    variant: Omit<{
        type: vue.PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    href: StringConstructor;
    replace: BooleanConstructor;
    to: vue.PropType<vue_router.RouteLocationRaw>;
    exact: BooleanConstructor;
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
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
        type: vue.PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    active: {
        type: BooleanConstructor;
        default: undefined;
    };
    activeClass: StringConstructor;
    activeColor: StringConstructor;
    appendAvatar: StringConstructor;
    appendIcon: vue.PropType<IconValue>;
    baseColor: StringConstructor;
    disabled: BooleanConstructor;
    lines: vue.PropType<"one" | "two" | "three" | false>;
    link: {
        type: BooleanConstructor;
        default: undefined;
    };
    nav: BooleanConstructor;
    prependAvatar: StringConstructor;
    prependIcon: vue.PropType<IconValue>;
    ripple: {
        type: vue.PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    slim: {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    subtitle: (StringConstructor | NumberConstructor)[];
    title: (StringConstructor | NumberConstructor)[];
    value: null;
    onClick: vue.PropType<(args_0: MouseEvent | KeyboardEvent) => void>;
    onClickOnce: vue.PropType<(args_0: MouseEvent) => void>;
    loading: BooleanConstructor;
    onToggleExpand: vue.PropType<(args_0: MouseEvent) => void>;
    toggleIcon: vue.PropType<IconValue>;
}, vue.ExtractPropTypes<{
    color: StringConstructor;
    variant: Omit<{
        type: vue.PropType<Variant>;
        default: string;
        validator: (v: any) => boolean;
    }, "type" | "default"> & {
        type: vue.PropType<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
        default: NonNullable<"flat" | "text" | "elevated" | "tonal" | "outlined" | "plain">;
    };
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    href: StringConstructor;
    replace: BooleanConstructor;
    to: vue.PropType<vue_router.RouteLocationRaw>;
    exact: BooleanConstructor;
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
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
        type: vue.PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    active: {
        type: BooleanConstructor;
        default: undefined;
    };
    activeClass: StringConstructor;
    activeColor: StringConstructor;
    appendAvatar: StringConstructor;
    appendIcon: vue.PropType<IconValue>;
    baseColor: StringConstructor;
    disabled: BooleanConstructor;
    lines: vue.PropType<"one" | "two" | "three" | false>;
    link: {
        type: BooleanConstructor;
        default: undefined;
    };
    nav: BooleanConstructor;
    prependAvatar: StringConstructor;
    prependIcon: vue.PropType<IconValue>;
    ripple: {
        type: vue.PropType<RippleDirectiveBinding["value"]>;
        default: boolean;
    };
    slim: {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    subtitle: (StringConstructor | NumberConstructor)[];
    title: (StringConstructor | NumberConstructor)[];
    value: null;
    onClick: vue.PropType<(args_0: MouseEvent | KeyboardEvent) => void>;
    onClickOnce: vue.PropType<(args_0: MouseEvent) => void>;
    loading: BooleanConstructor;
    onToggleExpand: vue.PropType<(args_0: MouseEvent) => void>;
    toggleIcon: vue.PropType<IconValue>;
}>>;
type VTreeviewItem = InstanceType<typeof VTreeviewItem>;

declare const VTreeviewGroup: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        tag: string;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        fluid: boolean;
    } & {
        color?: string | undefined;
        value?: any;
        title?: string | undefined;
        class?: any;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            activator?: ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            activator?: false | ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:activator"?: false | ((arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        tag: string;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        fluid: boolean;
    } & {
        color?: string | undefined;
        value?: any;
        title?: string | undefined;
        class?: any;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            activator?: ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            activator?: false | ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:activator"?: false | ((arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    }, {
        style: vue.StyleValue;
        tag: string;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        fluid: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        activator: (arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        style: vue.StyleValue;
        tag: string;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        fluid: boolean;
    } & {
        color?: string | undefined;
        value?: any;
        title?: string | undefined;
        class?: any;
        baseColor?: string | undefined;
        activeColor?: string | undefined;
        prependIcon?: IconValue | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            activator?: ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            activator?: false | ((arg: {
                isOpen: boolean;
                props: Record<string, unknown>;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:activator"?: false | ((arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        style: vue.StyleValue;
        tag: string;
        collapseIcon: IconValue;
        expandIcon: IconValue;
        fluid: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    tag: string;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    fluid: boolean;
} & {
    color?: string | undefined;
    value?: any;
    title?: string | undefined;
    class?: any;
    baseColor?: string | undefined;
    activeColor?: string | undefined;
    prependIcon?: IconValue | undefined;
    appendIcon?: IconValue | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        activator?: ((arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        activator?: false | ((arg: {
            isOpen: boolean;
            props: Record<string, unknown>;
        }) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:activator"?: false | ((arg: {
        isOpen: boolean;
        props: Record<string, unknown>;
    }) => vue.VNodeChild) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    style: vue.StyleValue;
    tag: string;
    collapseIcon: IconValue;
    expandIcon: IconValue;
    fluid: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    activator: (arg: {
        isOpen: boolean;
        props: Record<string, unknown>;
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    color: StringConstructor;
    value: null;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    title: StringConstructor;
    class: vue.PropType<ClassValue>;
    tag: {
        type: StringConstructor;
        default: string;
    };
    baseColor: StringConstructor;
    activeColor: StringConstructor;
    prependIcon: vue.PropType<IconValue>;
    appendIcon: vue.PropType<IconValue>;
    collapseIcon: Omit<{
        type: vue.PropType<IconValue>;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<IconValue>;
        default: NonNullable<IconValue>;
    };
    expandIcon: Omit<{
        type: vue.PropType<IconValue>;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<IconValue>;
        default: NonNullable<IconValue>;
    };
    fluid: BooleanConstructor;
}, vue.ExtractPropTypes<{
    color: StringConstructor;
    value: null;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    title: StringConstructor;
    class: vue.PropType<ClassValue>;
    tag: {
        type: StringConstructor;
        default: string;
    };
    baseColor: StringConstructor;
    activeColor: StringConstructor;
    prependIcon: vue.PropType<IconValue>;
    appendIcon: vue.PropType<IconValue>;
    collapseIcon: Omit<{
        type: vue.PropType<IconValue>;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<IconValue>;
        default: NonNullable<IconValue>;
    };
    expandIcon: Omit<{
        type: vue.PropType<IconValue>;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<IconValue>;
        default: NonNullable<IconValue>;
    };
    fluid: BooleanConstructor;
}>>;
type VTreeviewGroup = InstanceType<typeof VTreeviewGroup>;

export { VTreeview, VTreeviewGroup, VTreeviewItem };
