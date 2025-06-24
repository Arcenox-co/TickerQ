import * as vue from 'vue';
import { Ref, ComponentPropsOptions, ExtractPropTypes, VNodeChild, VNode, PropType, Component } from 'vue';

type ClassValue = any;

type ExpandProps = {
    expandOnClick: boolean;
    expanded: readonly string[];
    'onUpdate:expanded': ((value: any[]) => void) | undefined;
};
declare function provideExpanded(props: ExpandProps): {
    expand: (item: DataTableItem, value: boolean) => void;
    expanded: Ref<Set<string>> & {
        readonly externalValue: readonly string[];
    };
    expandOnClick: Ref<boolean>;
    isExpanded: (item: DataTableItem) => boolean;
    toggleExpand: (item: DataTableItem) => void;
};

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

interface DataTableItemProps {
    items: any[];
    itemValue: SelectItemKey;
    itemSelectable: SelectItemKey;
    returnObject: boolean;
}

interface SelectableItem {
    value: any;
    selectable: boolean;
}
type SelectionProps = Pick<DataTableItemProps, 'itemValue'> & {
    modelValue: readonly any[];
    selectStrategy: 'single' | 'page' | 'all';
    valueComparator: typeof deepEqual;
    'onUpdate:modelValue': EventProp<[any[]]> | undefined;
};
declare function provideSelection(props: SelectionProps, { allItems, currentPage }: {
    allItems: Ref<SelectableItem[]>;
    currentPage: Ref<SelectableItem[]>;
}): {
    toggleSelect: (item: SelectableItem) => void;
    select: (items: SelectableItem[], value: boolean) => void;
    selectAll: (value: boolean) => void;
    isSelected: (items: SelectableItem | SelectableItem[]) => boolean;
    isSomeSelected: (items: SelectableItem | SelectableItem[]) => boolean;
    someSelected: vue.ComputedRef<boolean>;
    allSelected: vue.ComputedRef<boolean>;
    showSelectAll: vue.ComputedRef<boolean>;
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

type DataTableCompareFunction<T = any> = (a: T, b: T) => number | null;
type DataTableHeader<T = Record<string, any>> = {
    key?: 'data-table-group' | 'data-table-select' | 'data-table-expand' | (string & {});
    value?: SelectItemKey<T>;
    title?: string;
    fixed?: boolean;
    align?: 'start' | 'end' | 'center';
    width?: number | string;
    minWidth?: string;
    maxWidth?: string;
    nowrap?: boolean;
    headerProps?: Record<string, any>;
    cellProps?: HeaderCellProps;
    sortable?: boolean;
    sort?: DataTableCompareFunction;
    sortRaw?: DataTableCompareFunction;
    filter?: FilterFunction;
    mobile?: boolean;
    children?: DataTableHeader<T>[];
};
type InternalDataTableHeader = Omit<DataTableHeader, 'key' | 'value' | 'children'> & {
    key: string | null;
    value: SelectItemKey | null;
    sortable: boolean;
    fixedOffset?: number;
    lastFixed?: boolean;
    nowrap?: boolean;
    colspan?: number;
    rowspan?: number;
    children?: InternalDataTableHeader[];
};
interface DataTableItem<T = any> extends InternalItem<T>, GroupableItem<T>, SelectableItem {
    key: any;
    index: number;
    columns: {
        [key: string]: any;
    };
}
type ItemSlotBase<T> = {
    index: number;
    item: T;
    internalItem: DataTableItem<T>;
    isExpanded: ReturnType<typeof provideExpanded>['isExpanded'];
    toggleExpand: ReturnType<typeof provideExpanded>['toggleExpand'];
    isSelected: ReturnType<typeof provideSelection>['isSelected'];
    toggleSelect: ReturnType<typeof provideSelection>['toggleSelect'];
};
type ItemKeySlot<T> = ItemSlotBase<T> & {
    value: any;
    column: InternalDataTableHeader;
};
type HeaderCellProps = Record<string, any> | ((data: Pick<ItemKeySlot<any>, 'index' | 'item' | 'internalItem' | 'value'>) => Record<string, any>);

type SortItem = {
    key: string;
    order?: boolean | 'asc' | 'desc';
};
declare function provideSort(options: {
    sortBy: Ref<readonly SortItem[]>;
    mustSort: Ref<boolean>;
    multiSort: Ref<boolean>;
    page?: Ref<number>;
}): {
    sortBy: Ref<readonly SortItem[]>;
    toggleSort: (column: InternalDataTableHeader) => void;
    isSorted: (column: InternalDataTableHeader) => boolean;
};

interface GroupableItem<T = any> {
    type: 'item';
    raw: T;
}
interface Group<T = any> {
    type: 'group';
    depth: number;
    id: string;
    key: string;
    value: any;
    items: readonly (T | Group<T>)[];
}
declare function provideGroupBy(options: {
    groupBy: Ref<readonly SortItem[]>;
    sortBy: Ref<readonly SortItem[]>;
    disableSort?: Ref<boolean>;
}): {
    sortByWithGroups: vue.ComputedRef<SortItem[]>;
    toggleGroup: (group: Group) => void;
    opened: Ref<Set<string> & Omit<Set<string>, keyof Set<any>>>;
    groupBy: Ref<readonly SortItem[]>;
    extractRows: <T extends GroupableItem>(items: readonly (T | Group<T>)[]) => T[];
    isGroupOpen: (group: Group) => boolean;
};

interface DataIteratorItemProps {
    items: any[];
    itemValue: SelectItemKey;
    itemSelectable: SelectItemKey;
    returnObject: boolean;
}
interface DataIteratorItem<T = any> extends GroupableItem<T>, SelectableItem {
    value: unknown;
}

declare function providePagination(options: {
    page: Ref<number>;
    itemsPerPage: Ref<number>;
    itemsLength: Ref<number>;
}): {
    page: Ref<number>;
    itemsPerPage: Ref<number>;
    startIndex: vue.ComputedRef<number>;
    stopIndex: vue.ComputedRef<number>;
    pageCount: vue.ComputedRef<number>;
    itemsLength: Ref<number>;
    nextPage: () => void;
    prevPage: () => void;
    setPage: (value: number) => void;
    setItemsPerPage: (value: number) => void;
};

interface LoaderSlotProps {
    color: string | undefined;
    isActive: boolean;
}

type VDataIteratorSlotProps<T> = {
    page: number;
    itemsPerPage: number;
    sortBy: readonly SortItem[];
    pageCount: number;
    toggleSort: ReturnType<typeof provideSort>['toggleSort'];
    prevPage: ReturnType<typeof providePagination>['prevPage'];
    nextPage: ReturnType<typeof providePagination>['nextPage'];
    setPage: ReturnType<typeof providePagination>['setPage'];
    setItemsPerPage: ReturnType<typeof providePagination>['setItemsPerPage'];
    isSelected: ReturnType<typeof provideSelection>['isSelected'];
    select: ReturnType<typeof provideSelection>['select'];
    selectAll: ReturnType<typeof provideSelection>['selectAll'];
    toggleSelect: ReturnType<typeof provideSelection>['toggleSelect'];
    isExpanded: ReturnType<typeof provideExpanded>['isExpanded'];
    toggleExpand: ReturnType<typeof provideExpanded>['toggleExpand'];
    isGroupOpen: ReturnType<typeof provideGroupBy>['isGroupOpen'];
    toggleGroup: ReturnType<typeof provideGroupBy>['toggleGroup'];
    items: readonly DataIteratorItem<T>[];
    groupedItems: readonly (DataIteratorItem<T> | Group<DataIteratorItem<T>>)[];
};
type VDataIteratorSlots<T> = {
    default: VDataIteratorSlotProps<T>;
    header: VDataIteratorSlotProps<T>;
    footer: VDataIteratorSlotProps<T>;
    loader: LoaderSlotProps;
    'no-data': never;
};
declare const VDataIterator: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        page: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            hideOnLeave: boolean;
        };
        expanded: readonly string[];
        loading: boolean;
        style: vue.StyleValue;
        tag: string;
        sortBy: readonly SortItem[];
        modelValue: readonly any[];
        valueComparator: typeof deepEqual;
        selectStrategy: "all" | "page" | "single";
        returnObject: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        itemValue: SelectItemKey;
        multiSort: boolean;
        mustSort: boolean;
        groupBy: readonly SortItem[];
        itemSelectable: SelectItemKey;
        showSelect: boolean;
        expandOnClick: boolean;
        showExpand: boolean;
        itemsPerPage: string | number;
    } & {
        search?: string | undefined;
        class?: any;
        customFilter?: FilterFunction | undefined;
        customKeyFilter?: FilterKeyFunctions | undefined;
        filterKeys?: FilterKeys | undefined;
        customKeySort?: Record<string, DataTableCompareFunction> | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: any[]) => any) | undefined;
        "onUpdate:sortBy"?: ((value: any) => any) | undefined;
        "onUpdate:groupBy"?: ((value: any) => any) | undefined;
        "onUpdate:expanded"?: ((value: any) => any) | undefined;
        "onUpdate:page"?: ((value: number) => any) | undefined;
        "onUpdate:itemsPerPage"?: ((value: number) => any) | undefined;
        "onUpdate:options"?: ((value: any) => any) | undefined;
        "onUpdate:currentItems"?: ((value: any) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
        'update:modelValue': (value: any[]) => true;
        'update:groupBy': (value: any) => true;
        'update:page': (value: number) => true;
        'update:itemsPerPage': (value: number) => true;
        'update:sortBy': (value: any) => true;
        'update:options': (value: any) => true;
        'update:expanded': (value: any) => true;
        'update:currentItems': (value: any) => true;
    }, "$children" | "v-slots" | "v-slot:default" | "items" | "v-slot:loader" | "v-slot:header" | "v-slot:no-data" | "v-slot:footer">, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        page: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            hideOnLeave: boolean;
        };
        expanded: readonly string[];
        loading: boolean;
        style: vue.StyleValue;
        tag: string;
        sortBy: readonly SortItem[];
        modelValue: readonly any[];
        valueComparator: typeof deepEqual;
        selectStrategy: "all" | "page" | "single";
        returnObject: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        itemValue: SelectItemKey;
        multiSort: boolean;
        mustSort: boolean;
        groupBy: readonly SortItem[];
        itemSelectable: SelectItemKey;
        showSelect: boolean;
        expandOnClick: boolean;
        showExpand: boolean;
        itemsPerPage: string | number;
    } & {
        search?: string | undefined;
        class?: any;
        customFilter?: FilterFunction | undefined;
        customKeyFilter?: FilterKeyFunctions | undefined;
        filterKeys?: FilterKeys | undefined;
        customKeySort?: Record<string, DataTableCompareFunction> | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: any[]) => any) | undefined;
        "onUpdate:sortBy"?: ((value: any) => any) | undefined;
        "onUpdate:groupBy"?: ((value: any) => any) | undefined;
        "onUpdate:expanded"?: ((value: any) => any) | undefined;
        "onUpdate:page"?: ((value: number) => any) | undefined;
        "onUpdate:itemsPerPage"?: ((value: number) => any) | undefined;
        "onUpdate:options"?: ((value: any) => any) | undefined;
        "onUpdate:currentItems"?: ((value: any) => any) | undefined;
    }, {
        page: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            hideOnLeave: boolean;
        };
        expanded: readonly string[];
        loading: boolean;
        style: vue.StyleValue;
        tag: string;
        sortBy: readonly SortItem[];
        modelValue: readonly any[];
        valueComparator: typeof deepEqual;
        selectStrategy: "all" | "page" | "single";
        returnObject: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        itemValue: SelectItemKey;
        multiSort: boolean;
        mustSort: boolean;
        groupBy: readonly SortItem[];
        itemSelectable: SelectItemKey;
        showSelect: boolean;
        expandOnClick: boolean;
        showExpand: boolean;
        itemsPerPage: string | number;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: VDataIteratorSlotProps<unknown>) => vue.VNode[];
        header: (arg: VDataIteratorSlotProps<unknown>) => vue.VNode[];
        footer: (arg: VDataIteratorSlotProps<unknown>) => vue.VNode[];
        loader: (arg: LoaderSlotProps) => vue.VNode[];
        'no-data': () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        page: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            hideOnLeave: boolean;
        };
        expanded: readonly string[];
        loading: boolean;
        style: vue.StyleValue;
        tag: string;
        sortBy: readonly SortItem[];
        modelValue: readonly any[];
        valueComparator: typeof deepEqual;
        selectStrategy: "all" | "page" | "single";
        returnObject: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        itemValue: SelectItemKey;
        multiSort: boolean;
        mustSort: boolean;
        groupBy: readonly SortItem[];
        itemSelectable: SelectItemKey;
        showSelect: boolean;
        expandOnClick: boolean;
        showExpand: boolean;
        itemsPerPage: string | number;
    } & {
        search?: string | undefined;
        class?: any;
        customFilter?: FilterFunction | undefined;
        customKeyFilter?: FilterKeyFunctions | undefined;
        filterKeys?: FilterKeys | undefined;
        customKeySort?: Record<string, DataTableCompareFunction> | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: any[]) => any) | undefined;
        "onUpdate:sortBy"?: ((value: any) => any) | undefined;
        "onUpdate:groupBy"?: ((value: any) => any) | undefined;
        "onUpdate:expanded"?: ((value: any) => any) | undefined;
        "onUpdate:page"?: ((value: number) => any) | undefined;
        "onUpdate:itemsPerPage"?: ((value: number) => any) | undefined;
        "onUpdate:options"?: ((value: any) => any) | undefined;
        "onUpdate:currentItems"?: ((value: any) => any) | undefined;
    }, {}, {}, {}, {}, {
        page: string | number;
        transition: string | boolean | (vue.TransitionProps & {
            component?: Component;
        }) | {
            component: Component;
            hideOnLeave: boolean;
        };
        expanded: readonly string[];
        loading: boolean;
        style: vue.StyleValue;
        tag: string;
        sortBy: readonly SortItem[];
        modelValue: readonly any[];
        valueComparator: typeof deepEqual;
        selectStrategy: "all" | "page" | "single";
        returnObject: boolean;
        filterMode: FilterMode;
        noFilter: boolean;
        itemValue: SelectItemKey;
        multiSort: boolean;
        mustSort: boolean;
        groupBy: readonly SortItem[];
        itemSelectable: SelectItemKey;
        showSelect: boolean;
        expandOnClick: boolean;
        showExpand: boolean;
        itemsPerPage: string | number;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    page: string | number;
    transition: string | boolean | (vue.TransitionProps & {
        component?: Component;
    }) | {
        component: Component;
        hideOnLeave: boolean;
    };
    expanded: readonly string[];
    loading: boolean;
    style: vue.StyleValue;
    tag: string;
    sortBy: readonly SortItem[];
    modelValue: readonly any[];
    valueComparator: typeof deepEqual;
    selectStrategy: "all" | "page" | "single";
    returnObject: boolean;
    filterMode: FilterMode;
    noFilter: boolean;
    itemValue: SelectItemKey;
    multiSort: boolean;
    mustSort: boolean;
    groupBy: readonly SortItem[];
    itemSelectable: SelectItemKey;
    showSelect: boolean;
    expandOnClick: boolean;
    showExpand: boolean;
    itemsPerPage: string | number;
} & {
    search?: string | undefined;
    class?: any;
    customFilter?: FilterFunction | undefined;
    customKeyFilter?: FilterKeyFunctions | undefined;
    filterKeys?: FilterKeys | undefined;
    customKeySort?: Record<string, DataTableCompareFunction> | undefined;
} & {
    "onUpdate:modelValue"?: ((value: any[]) => any) | undefined;
    "onUpdate:sortBy"?: ((value: any) => any) | undefined;
    "onUpdate:groupBy"?: ((value: any) => any) | undefined;
    "onUpdate:expanded"?: ((value: any) => any) | undefined;
    "onUpdate:page"?: ((value: number) => any) | undefined;
    "onUpdate:itemsPerPage"?: ((value: number) => any) | undefined;
    "onUpdate:options"?: ((value: any) => any) | undefined;
    "onUpdate:currentItems"?: ((value: any) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
    'update:modelValue': (value: any[]) => true;
    'update:groupBy': (value: any) => true;
    'update:page': (value: number) => true;
    'update:itemsPerPage': (value: number) => true;
    'update:sortBy': (value: any) => true;
    'update:options': (value: any) => true;
    'update:expanded': (value: any) => true;
    'update:currentItems': (value: any) => true;
}, "$children" | "v-slots" | "v-slot:default" | "items" | "v-slot:loader" | "v-slot:header" | "v-slot:no-data" | "v-slot:footer">, string, {
    page: string | number;
    transition: string | boolean | (vue.TransitionProps & {
        component?: Component;
    }) | {
        component: Component;
        hideOnLeave: boolean;
    };
    expanded: readonly string[];
    loading: boolean;
    style: vue.StyleValue;
    tag: string;
    sortBy: readonly SortItem[];
    modelValue: readonly any[];
    valueComparator: typeof deepEqual;
    selectStrategy: "all" | "page" | "single";
    returnObject: boolean;
    filterMode: FilterMode;
    noFilter: boolean;
    itemValue: SelectItemKey;
    multiSort: boolean;
    mustSort: boolean;
    groupBy: readonly SortItem[];
    itemSelectable: SelectItemKey;
    showSelect: boolean;
    expandOnClick: boolean;
    showExpand: boolean;
    itemsPerPage: string | number;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: VDataIteratorSlotProps<unknown>) => vue.VNode[];
    header: (arg: VDataIteratorSlotProps<unknown>) => vue.VNode[];
    footer: (arg: VDataIteratorSlotProps<unknown>) => vue.VNode[];
    loader: (arg: LoaderSlotProps) => vue.VNode[];
    'no-data': () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & (new <T>(props: {
    items?: readonly T[];
}, slots: VDataIteratorSlots<T>) => GenericProps<typeof props, typeof slots>) & FilterPropsOptions<{
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
            hideOnLeave: boolean;
        }>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: Component;
        })> | {
            component: Component;
            hideOnLeave: boolean;
        };
    };
    tag: {
        type: StringConstructor;
        default: string;
    };
    customFilter: vue.PropType<FilterFunction>;
    customKeyFilter: vue.PropType<FilterKeyFunctions>;
    filterKeys: vue.PropType<FilterKeys>;
    filterMode: {
        type: vue.PropType<FilterMode>;
        default: string;
    };
    noFilter: BooleanConstructor;
    groupBy: {
        type: vue.PropType<readonly SortItem[]>;
        default: () => never[];
    };
    expandOnClick: BooleanConstructor;
    showExpand: BooleanConstructor;
    expanded: {
        type: vue.PropType<readonly string[]>;
        default: () => never[];
    };
    page: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    itemsPerPage: Omit<{
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    }, "type" | "default"> & {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    sortBy: {
        type: vue.PropType<readonly SortItem[]>;
        default: () => never[];
    };
    customKeySort: vue.PropType<Record<string, DataTableCompareFunction>>;
    multiSort: BooleanConstructor;
    mustSort: BooleanConstructor;
    showSelect: BooleanConstructor;
    selectStrategy: {
        type: vue.PropType<"single" | "page" | "all">;
        default: string;
    };
    modelValue: {
        type: vue.PropType<readonly any[]>;
        default: () => never[];
    };
    valueComparator: {
        type: vue.PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
    items: {
        type: vue.PropType<DataIteratorItemProps["items"]>;
        default: () => never[];
    };
    itemValue: {
        type: vue.PropType<SelectItemKey>;
        default: string;
    };
    itemSelectable: {
        type: vue.PropType<SelectItemKey>;
        default: null;
    };
    returnObject: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    search: StringConstructor;
    loading: BooleanConstructor;
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
            hideOnLeave: boolean;
        }>;
        default: NonNullable<string | boolean | (vue.TransitionProps & {
            component?: Component;
        })> | {
            component: Component;
            hideOnLeave: boolean;
        };
    };
    tag: {
        type: StringConstructor;
        default: string;
    };
    customFilter: vue.PropType<FilterFunction>;
    customKeyFilter: vue.PropType<FilterKeyFunctions>;
    filterKeys: vue.PropType<FilterKeys>;
    filterMode: {
        type: vue.PropType<FilterMode>;
        default: string;
    };
    noFilter: BooleanConstructor;
    groupBy: {
        type: vue.PropType<readonly SortItem[]>;
        default: () => never[];
    };
    expandOnClick: BooleanConstructor;
    showExpand: BooleanConstructor;
    expanded: {
        type: vue.PropType<readonly string[]>;
        default: () => never[];
    };
    page: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    itemsPerPage: Omit<{
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    }, "type" | "default"> & {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    sortBy: {
        type: vue.PropType<readonly SortItem[]>;
        default: () => never[];
    };
    customKeySort: vue.PropType<Record<string, DataTableCompareFunction>>;
    multiSort: BooleanConstructor;
    mustSort: BooleanConstructor;
    showSelect: BooleanConstructor;
    selectStrategy: {
        type: vue.PropType<"single" | "page" | "all">;
        default: string;
    };
    modelValue: {
        type: vue.PropType<readonly any[]>;
        default: () => never[];
    };
    valueComparator: {
        type: vue.PropType<typeof deepEqual>;
        default: typeof deepEqual;
    };
    items: {
        type: vue.PropType<DataIteratorItemProps["items"]>;
        default: () => never[];
    };
    itemValue: {
        type: vue.PropType<SelectItemKey>;
        default: string;
    };
    itemSelectable: {
        type: vue.PropType<SelectItemKey>;
        default: null;
    };
    returnObject: BooleanConstructor;
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    search: StringConstructor;
    loading: BooleanConstructor;
}>>;
type VDataIterator = InstanceType<typeof VDataIterator>;

export { VDataIterator };
