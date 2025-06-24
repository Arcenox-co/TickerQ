import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, VNodeChild, VNode, PropType, ComponentPublicInstance, FunctionalComponent } from 'vue';

type ClassValue = any;

declare const block: readonly ["top", "bottom"];
declare const inline: readonly ["start", "end", "left", "right"];
type Tblock = typeof block[number];
type Tinline = typeof inline[number];
type Anchor = Tblock | Tinline | 'center' | 'center center' | `${Tblock} ${Tinline | 'center'}` | `${Tinline} ${Tblock | 'center'}`;

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

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

type VPickerSlots = {
    header: never;
    default: never;
    actions: never;
    title: never;
};

type VDatePickerSlots = Omit<VPickerSlots, 'header'> & {
    header: {
        header: string;
        transition: string;
    };
};
declare const VDatePicker: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        transition: string;
        header: string;
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        tag: string;
        landscape: boolean;
        tile: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        reverseTransition: string;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
        hideHeader: boolean;
    } & {
        max?: unknown;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: string | string[] | undefined;
        min?: unknown;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        text?: string | undefined;
        month?: string | number | undefined;
        year?: number | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        bgColor?: string | undefined;
        firstDayOfWeek?: string | number | undefined;
        allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
        displayValue?: unknown;
    } & {
        "onUpdate:month"?: ((date: any) => any) | undefined;
        "onUpdate:year"?: ((date: any) => any) | undefined;
        "onUpdate:viewMode"?: ((date: any) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
        'update:modelValue': (date: any) => true;
        'update:month': (date: any) => true;
        'update:year': (date: any) => true;
        'update:viewMode': (date: any) => true;
    }, "multiple" | "$children" | "v-slots" | "v-slot:default" | "modelValue" | "v-slot:title" | "update:modelValue" | "v-slot:header" | "v-slot:actions">, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        transition: string;
        header: string;
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        tag: string;
        landscape: boolean;
        tile: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        reverseTransition: string;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
        hideHeader: boolean;
    } & {
        max?: unknown;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: string | string[] | undefined;
        min?: unknown;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        text?: string | undefined;
        month?: string | number | undefined;
        year?: number | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        bgColor?: string | undefined;
        firstDayOfWeek?: string | number | undefined;
        allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
        displayValue?: unknown;
    } & {
        "onUpdate:month"?: ((date: any) => any) | undefined;
        "onUpdate:year"?: ((date: any) => any) | undefined;
        "onUpdate:viewMode"?: ((date: any) => any) | undefined;
    }, {
        active: string | string[];
        transition: string;
        header: string;
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        tag: string;
        landscape: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        reverseTransition: string;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
        hideHeader: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        title: () => vue.VNode[];
        actions: () => vue.VNode[];
        header: (arg: {
            header: string;
            transition: string;
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        transition: string;
        header: string;
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        tag: string;
        landscape: boolean;
        tile: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        reverseTransition: string;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
        hideHeader: boolean;
    } & {
        max?: unknown;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        active?: string | string[] | undefined;
        min?: unknown;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        text?: string | undefined;
        month?: string | number | undefined;
        year?: number | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        bgColor?: string | undefined;
        firstDayOfWeek?: string | number | undefined;
        allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
        displayValue?: unknown;
    } & {
        "onUpdate:month"?: ((date: any) => any) | undefined;
        "onUpdate:year"?: ((date: any) => any) | undefined;
        "onUpdate:viewMode"?: ((date: any) => any) | undefined;
    }, {}, {}, {}, {}, {
        active: string | string[];
        transition: string;
        header: string;
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        tag: string;
        landscape: boolean;
        rounded: string | number | boolean;
        tile: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        reverseTransition: string;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
        hideHeader: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    transition: string;
    header: string;
    style: vue.StyleValue;
    title: string;
    disabled: boolean;
    tag: string;
    landscape: boolean;
    tile: boolean;
    nextIcon: IconValue;
    prevIcon: IconValue;
    reverseTransition: string;
    modeIcon: IconValue;
    viewMode: "month" | "year" | "months";
    showAdjacentMonths: boolean;
    weekdays: number[];
    weeksInMonth: "static" | "dynamic";
    hideWeekdays: boolean;
    showWeek: boolean;
    hideHeader: boolean;
} & {
    max?: unknown;
    location?: Anchor | null | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    active?: string | string[] | undefined;
    min?: unknown;
    border?: string | number | boolean | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
    text?: string | undefined;
    month?: string | number | undefined;
    year?: number | undefined;
    class?: any;
    theme?: string | undefined;
    elevation?: string | number | undefined;
    rounded?: string | number | boolean | undefined;
    bgColor?: string | undefined;
    firstDayOfWeek?: string | number | undefined;
    allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
    displayValue?: unknown;
} & {
    "onUpdate:month"?: ((date: any) => any) | undefined;
    "onUpdate:year"?: ((date: any) => any) | undefined;
    "onUpdate:viewMode"?: ((date: any) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Omit<{
    'update:modelValue': (date: any) => true;
    'update:month': (date: any) => true;
    'update:year': (date: any) => true;
    'update:viewMode': (date: any) => true;
}, "multiple" | "$children" | "v-slots" | "v-slot:default" | "modelValue" | "v-slot:title" | "update:modelValue" | "v-slot:header" | "v-slot:actions">, string, {
    active: string | string[];
    transition: string;
    header: string;
    style: vue.StyleValue;
    title: string;
    disabled: boolean;
    tag: string;
    landscape: boolean;
    rounded: string | number | boolean;
    tile: boolean;
    nextIcon: IconValue;
    prevIcon: IconValue;
    reverseTransition: string;
    modeIcon: IconValue;
    viewMode: "month" | "year" | "months";
    showAdjacentMonths: boolean;
    weekdays: number[];
    weeksInMonth: "static" | "dynamic";
    hideWeekdays: boolean;
    showWeek: boolean;
    hideHeader: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    title: () => vue.VNode[];
    actions: () => vue.VNode[];
    header: (arg: {
        header: string;
        transition: string;
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & (new <T, Multiple extends boolean | "range" | number | (string & {}) = false, TModel = Multiple extends string | number | true ? T[] : T>(props: {
    modelValue?: TModel;
    "onUpdate:modelValue"?: (value: TModel) => void;
    multiple?: Multiple;
}, slots: VDatePickerSlots) => GenericProps<typeof props, typeof slots>) & FilterPropsOptions<{
    modelValue: null;
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
        type: vue.PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    location: vue.PropType<Anchor | null>;
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
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    bgColor: StringConstructor;
    landscape: BooleanConstructor;
    title: {
        type: vue.PropType<string>;
        default: string;
    };
    hideHeader: BooleanConstructor;
    max: vue.PropType<unknown>;
    min: vue.PropType<unknown>;
    year: NumberConstructor;
    allowedDates: vue.PropType<unknown[] | ((date: unknown) => boolean)>;
    disabled: BooleanConstructor;
    displayValue: vue.PropType<unknown>;
    month: (StringConstructor | NumberConstructor)[];
    showAdjacentMonths: BooleanConstructor;
    weekdays: {
        type: {
            (arrayLength: number): number[];
            (...items: number[]): number[];
            new (arrayLength: number): number[];
            new (...items: number[]): number[];
            isArray(arg: any): arg is any[];
            readonly prototype: any[];
            from<T>(arrayLike: ArrayLike<T>): T[];
            from<T, U>(arrayLike: ArrayLike<T>, mapfn: (v: T, k: number) => U, thisArg?: any): U[];
            from<T>(iterable: Iterable<T> | ArrayLike<T>): T[];
            from<T, U>(iterable: Iterable<T> | ArrayLike<T>, mapfn: (v: T, k: number) => U, thisArg?: any): U[];
            of<T>(...items: T[]): T[];
            fromAsync<T>(iterableOrArrayLike: AsyncIterable<T> | Iterable<T | PromiseLike<T>> | ArrayLike<T | PromiseLike<T>>): Promise<T[]>;
            fromAsync<T, U>(iterableOrArrayLike: AsyncIterable<T> | Iterable<T> | ArrayLike<T>, mapFn: (value: Awaited<T>) => U, thisArg?: any): Promise<Awaited<U>[]>;
            readonly [Symbol.species]: ArrayConstructor;
        };
        default: () => number[];
    };
    weeksInMonth: Omit<{
        type: vue.PropType<"dynamic" | "static">;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<"static" | "dynamic">;
        default: NonNullable<"static" | "dynamic">;
    };
    firstDayOfWeek: (StringConstructor | NumberConstructor)[];
    hideWeekdays: BooleanConstructor;
    multiple: vue.PropType<boolean | "range" | number | (string & {})>;
    showWeek: BooleanConstructor;
    transition: {
        type: StringConstructor;
        default: string;
    };
    reverseTransition: {
        type: StringConstructor;
        default: string;
    };
    active: {
        type: vue.PropType<string | string[]>;
        default: undefined;
    };
    nextIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    prevIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    modeIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    text: StringConstructor;
    viewMode: {
        type: vue.PropType<"month" | "months" | "year">;
        default: string;
    };
    header: {
        type: StringConstructor;
        default: string;
    };
}, vue.ExtractPropTypes<{
    modelValue: null;
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
        type: vue.PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    location: vue.PropType<Anchor | null>;
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
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    bgColor: StringConstructor;
    landscape: BooleanConstructor;
    title: {
        type: vue.PropType<string>;
        default: string;
    };
    hideHeader: BooleanConstructor;
    max: vue.PropType<unknown>;
    min: vue.PropType<unknown>;
    year: NumberConstructor;
    allowedDates: vue.PropType<unknown[] | ((date: unknown) => boolean)>;
    disabled: BooleanConstructor;
    displayValue: vue.PropType<unknown>;
    month: (StringConstructor | NumberConstructor)[];
    showAdjacentMonths: BooleanConstructor;
    weekdays: {
        type: {
            (arrayLength: number): number[];
            (...items: number[]): number[];
            new (arrayLength: number): number[];
            new (...items: number[]): number[];
            isArray(arg: any): arg is any[];
            readonly prototype: any[];
            from<T>(arrayLike: ArrayLike<T>): T[];
            from<T, U>(arrayLike: ArrayLike<T>, mapfn: (v: T, k: number) => U, thisArg?: any): U[];
            from<T>(iterable: Iterable<T> | ArrayLike<T>): T[];
            from<T, U>(iterable: Iterable<T> | ArrayLike<T>, mapfn: (v: T, k: number) => U, thisArg?: any): U[];
            of<T>(...items: T[]): T[];
            fromAsync<T>(iterableOrArrayLike: AsyncIterable<T> | Iterable<T | PromiseLike<T>> | ArrayLike<T | PromiseLike<T>>): Promise<T[]>;
            fromAsync<T, U>(iterableOrArrayLike: AsyncIterable<T> | Iterable<T> | ArrayLike<T>, mapFn: (value: Awaited<T>) => U, thisArg?: any): Promise<Awaited<U>[]>;
            readonly [Symbol.species]: ArrayConstructor;
        };
        default: () => number[];
    };
    weeksInMonth: Omit<{
        type: vue.PropType<"dynamic" | "static">;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<"static" | "dynamic">;
        default: NonNullable<"static" | "dynamic">;
    };
    firstDayOfWeek: (StringConstructor | NumberConstructor)[];
    hideWeekdays: BooleanConstructor;
    multiple: vue.PropType<boolean | "range" | number | (string & {})>;
    showWeek: BooleanConstructor;
    transition: {
        type: StringConstructor;
        default: string;
    };
    reverseTransition: {
        type: StringConstructor;
        default: string;
    };
    active: {
        type: vue.PropType<string | string[]>;
        default: undefined;
    };
    nextIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    prevIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    modeIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    text: StringConstructor;
    viewMode: {
        type: vue.PropType<"month" | "months" | "year">;
        default: string;
    };
    header: {
        type: StringConstructor;
        default: string;
    };
}>>;
type VDatePicker = InstanceType<typeof VDatePicker>;

declare const VDatePickerControls: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        disabled: string | boolean | string[];
        nextIcon: IconValue;
        prevIcon: IconValue;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
    } & {
        active?: string | string[] | undefined;
        text?: string | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onClick:year"?: (() => any) | undefined;
        "onClick:month"?: (() => any) | undefined;
        "onClick:prev"?: (() => any) | undefined;
        "onClick:next"?: (() => any) | undefined;
        "onClick:text"?: (() => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'click:year': () => true;
        'click:month': () => true;
        'click:prev': () => true;
        'click:next': () => true;
        'click:text': () => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        disabled: string | boolean | string[];
        nextIcon: IconValue;
        prevIcon: IconValue;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
    } & {
        active?: string | string[] | undefined;
        text?: string | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onClick:year"?: (() => any) | undefined;
        "onClick:month"?: (() => any) | undefined;
        "onClick:prev"?: (() => any) | undefined;
        "onClick:next"?: (() => any) | undefined;
        "onClick:text"?: (() => any) | undefined;
    }, {
        active: string | string[];
        disabled: string | boolean | string[];
        nextIcon: IconValue;
        prevIcon: IconValue;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
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
        disabled: string | boolean | string[];
        nextIcon: IconValue;
        prevIcon: IconValue;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
    } & {
        active?: string | string[] | undefined;
        text?: string | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onClick:year"?: (() => any) | undefined;
        "onClick:month"?: (() => any) | undefined;
        "onClick:prev"?: (() => any) | undefined;
        "onClick:next"?: (() => any) | undefined;
        "onClick:text"?: (() => any) | undefined;
    }, {}, {}, {}, {}, {
        active: string | string[];
        disabled: string | boolean | string[];
        nextIcon: IconValue;
        prevIcon: IconValue;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    disabled: string | boolean | string[];
    nextIcon: IconValue;
    prevIcon: IconValue;
    modeIcon: IconValue;
    viewMode: "month" | "year" | "months";
} & {
    active?: string | string[] | undefined;
    text?: string | undefined;
} & {
    $children?: vue.VNodeChild | {
        default?: (() => vue.VNodeChild) | undefined;
    } | (() => vue.VNodeChild);
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
} & {
    "onClick:year"?: (() => any) | undefined;
    "onClick:month"?: (() => any) | undefined;
    "onClick:prev"?: (() => any) | undefined;
    "onClick:next"?: (() => any) | undefined;
    "onClick:text"?: (() => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'click:year': () => true;
    'click:month': () => true;
    'click:prev': () => true;
    'click:next': () => true;
    'click:text': () => true;
}, string, {
    active: string | string[];
    disabled: string | boolean | string[];
    nextIcon: IconValue;
    prevIcon: IconValue;
    modeIcon: IconValue;
    viewMode: "month" | "year" | "months";
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    active: {
        type: PropType<string | string[]>;
        default: undefined;
    };
    disabled: {
        type: PropType<boolean | string | string[]>;
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
    modeIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    text: StringConstructor;
    viewMode: {
        type: PropType<"month" | "months" | "year">;
        default: string;
    };
}, vue.ExtractPropTypes<{
    active: {
        type: PropType<string | string[]>;
        default: undefined;
    };
    disabled: {
        type: PropType<boolean | string | string[]>;
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
    modeIcon: {
        type: PropType<IconValue>;
        default: string;
    };
    text: StringConstructor;
    viewMode: {
        type: PropType<"month" | "months" | "year">;
        default: string;
    };
}>>;
type VDatePickerControls = InstanceType<typeof VDatePickerControls>;

declare const VDatePickerHeader: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{} & {
        color?: string | undefined;
        transition?: string | undefined;
        header?: string | undefined;
        onClick?: ((args_0: MouseEvent) => void) | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            prepend?: (() => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            prepend?: false | (() => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        onClick?: (() => any) | undefined;
        "onClick:append"?: (() => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        click: () => true;
        'click:append': () => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {} & {
        color?: string | undefined;
        transition?: string | undefined;
        header?: string | undefined;
        onClick?: ((args_0: MouseEvent) => void) | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            prepend?: (() => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            prepend?: false | (() => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        onClick?: (() => any) | undefined;
        "onClick:append"?: (() => any) | undefined;
    }, {}, true, {}, vue.SlotsType<Partial<{
        prepend: () => vue.VNode[];
        default: () => vue.VNode[];
        append: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {} & {
        color?: string | undefined;
        transition?: string | undefined;
        header?: string | undefined;
        onClick?: ((args_0: MouseEvent) => void) | undefined;
        appendIcon?: IconValue | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            prepend?: (() => vue.VNodeChild) | undefined;
            default?: (() => vue.VNodeChild) | undefined;
            append?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            prepend?: false | (() => vue.VNodeChild) | undefined;
            default?: false | (() => vue.VNodeChild) | undefined;
            append?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        onClick?: (() => any) | undefined;
        "onClick:append"?: (() => any) | undefined;
    }, {}, {}, {}, {}, {}>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{} & {
    color?: string | undefined;
    transition?: string | undefined;
    header?: string | undefined;
    onClick?: ((args_0: MouseEvent) => void) | undefined;
    appendIcon?: IconValue | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        prepend?: (() => vue.VNodeChild) | undefined;
        default?: (() => vue.VNodeChild) | undefined;
        append?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        prepend?: false | (() => vue.VNodeChild) | undefined;
        default?: false | (() => vue.VNodeChild) | undefined;
        append?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:prepend"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:append"?: false | (() => vue.VNodeChild) | undefined;
} & {
    onClick?: (() => any) | undefined;
    "onClick:append"?: (() => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    click: () => true;
    'click:append': () => true;
}, string, {}, {}, string, vue.SlotsType<Partial<{
    prepend: () => vue.VNode[];
    default: () => vue.VNode[];
    append: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    appendIcon: vue.PropType<IconValue>;
    color: StringConstructor;
    header: StringConstructor;
    transition: StringConstructor;
    onClick: vue.PropType<(args_0: MouseEvent) => void>;
}, vue.ExtractPropTypes<{
    appendIcon: vue.PropType<IconValue>;
    color: StringConstructor;
    header: StringConstructor;
    transition: StringConstructor;
    onClick: vue.PropType<(args_0: MouseEvent) => void>;
}>>;
type VDatePickerHeader = InstanceType<typeof VDatePickerHeader>;

declare const VDatePickerMonth: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        transition: string;
        disabled: boolean;
        reverseTransition: string;
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
    } & {
        max?: unknown;
        min?: unknown;
        color?: string | undefined;
        multiple?: number | boolean | "range" | (string & {}) | undefined;
        month?: string | number | undefined;
        year?: string | number | undefined;
        modelValue?: unknown[] | undefined;
        firstDayOfWeek?: string | number | undefined;
        allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
        displayValue?: unknown;
    } & {
        $children?: {} | vue.VNodeChild | {
            day?: ((arg: {
                props: {
                    onClick: () => void;
                };
                item: any;
                i: number;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            day?: false | ((arg: {
                props: {
                    onClick: () => void;
                };
                item: any;
                i: number;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:day"?: false | ((arg: {
            props: {
                onClick: () => void;
            };
            item: any;
            i: number;
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((date: unknown) => any) | undefined;
        "onUpdate:month"?: ((date: number) => any) | undefined;
        "onUpdate:year"?: ((date: number) => any) | undefined;
    }, () => JSX.Element, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (date: unknown) => true;
        'update:month': (date: number) => true;
        'update:year': (date: number) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        transition: string;
        disabled: boolean;
        reverseTransition: string;
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
    } & {
        max?: unknown;
        min?: unknown;
        color?: string | undefined;
        multiple?: number | boolean | "range" | (string & {}) | undefined;
        month?: string | number | undefined;
        year?: string | number | undefined;
        modelValue?: unknown[] | undefined;
        firstDayOfWeek?: string | number | undefined;
        allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
        displayValue?: unknown;
    } & {
        $children?: {} | vue.VNodeChild | {
            day?: ((arg: {
                props: {
                    onClick: () => void;
                };
                item: any;
                i: number;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            day?: false | ((arg: {
                props: {
                    onClick: () => void;
                };
                item: any;
                i: number;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:day"?: false | ((arg: {
            props: {
                onClick: () => void;
            };
            item: any;
            i: number;
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((date: unknown) => any) | undefined;
        "onUpdate:month"?: ((date: number) => any) | undefined;
        "onUpdate:year"?: ((date: number) => any) | undefined;
    }, {
        transition: string;
        disabled: boolean;
        reverseTransition: string;
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        day: (arg: {
            props: {
                onClick: () => void;
            };
            item: any;
            i: number;
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        transition: string;
        disabled: boolean;
        reverseTransition: string;
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
    } & {
        max?: unknown;
        min?: unknown;
        color?: string | undefined;
        multiple?: number | boolean | "range" | (string & {}) | undefined;
        month?: string | number | undefined;
        year?: string | number | undefined;
        modelValue?: unknown[] | undefined;
        firstDayOfWeek?: string | number | undefined;
        allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
        displayValue?: unknown;
    } & {
        $children?: {} | vue.VNodeChild | {
            day?: ((arg: {
                props: {
                    onClick: () => void;
                };
                item: any;
                i: number;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            day?: false | ((arg: {
                props: {
                    onClick: () => void;
                };
                item: any;
                i: number;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:day"?: false | ((arg: {
            props: {
                onClick: () => void;
            };
            item: any;
            i: number;
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((date: unknown) => any) | undefined;
        "onUpdate:month"?: ((date: number) => any) | undefined;
        "onUpdate:year"?: ((date: number) => any) | undefined;
    }, () => JSX.Element, {}, {}, {}, {
        transition: string;
        disabled: boolean;
        reverseTransition: string;
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    transition: string;
    disabled: boolean;
    reverseTransition: string;
    showAdjacentMonths: boolean;
    weekdays: number[];
    weeksInMonth: "static" | "dynamic";
    hideWeekdays: boolean;
    showWeek: boolean;
} & {
    max?: unknown;
    min?: unknown;
    color?: string | undefined;
    multiple?: number | boolean | "range" | (string & {}) | undefined;
    month?: string | number | undefined;
    year?: string | number | undefined;
    modelValue?: unknown[] | undefined;
    firstDayOfWeek?: string | number | undefined;
    allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
    displayValue?: unknown;
} & {
    $children?: {} | vue.VNodeChild | {
        day?: ((arg: {
            props: {
                onClick: () => void;
            };
            item: any;
            i: number;
        }) => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        day?: false | ((arg: {
            props: {
                onClick: () => void;
            };
            item: any;
            i: number;
        }) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:day"?: false | ((arg: {
        props: {
            onClick: () => void;
        };
        item: any;
        i: number;
    }) => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((date: unknown) => any) | undefined;
    "onUpdate:month"?: ((date: number) => any) | undefined;
    "onUpdate:year"?: ((date: number) => any) | undefined;
}, () => JSX.Element, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (date: unknown) => true;
    'update:month': (date: number) => true;
    'update:year': (date: number) => true;
}, string, {
    transition: string;
    disabled: boolean;
    reverseTransition: string;
    showAdjacentMonths: boolean;
    weekdays: number[];
    weeksInMonth: "static" | "dynamic";
    hideWeekdays: boolean;
    showWeek: boolean;
}, {}, string, vue.SlotsType<Partial<{
    day: (arg: {
        props: {
            onClick: () => void;
        };
        item: any;
        i: number;
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    allowedDates: PropType<unknown[] | ((date: unknown) => boolean)>;
    disabled: BooleanConstructor;
    displayValue: PropType<unknown>;
    modelValue: PropType<unknown[]>;
    month: (StringConstructor | NumberConstructor)[];
    max: PropType<unknown>;
    min: PropType<unknown>;
    showAdjacentMonths: BooleanConstructor;
    year: (StringConstructor | NumberConstructor)[];
    weekdays: {
        type: {
            (arrayLength: number): number[];
            (...items: number[]): number[];
            new (arrayLength: number): number[];
            new (...items: number[]): number[];
            isArray(arg: any): arg is any[];
            readonly prototype: any[];
            from<T>(arrayLike: ArrayLike<T>): T[];
            from<T, U>(arrayLike: ArrayLike<T>, mapfn: (v: T, k: number) => U, thisArg?: any): U[];
            from<T>(iterable: Iterable<T> | ArrayLike<T>): T[];
            from<T, U>(iterable: Iterable<T> | ArrayLike<T>, mapfn: (v: T, k: number) => U, thisArg?: any): U[];
            of<T>(...items: T[]): T[];
            fromAsync<T>(iterableOrArrayLike: AsyncIterable<T> | Iterable<T | PromiseLike<T>> | ArrayLike<T | PromiseLike<T>>): Promise<T[]>;
            fromAsync<T, U>(iterableOrArrayLike: AsyncIterable<T> | Iterable<T> | ArrayLike<T>, mapFn: (value: Awaited<T>) => U, thisArg?: any): Promise<Awaited<U>[]>;
            readonly [Symbol.species]: ArrayConstructor;
        };
        default: () => number[];
    };
    weeksInMonth: {
        type: PropType<"dynamic" | "static">;
        default: string;
    };
    firstDayOfWeek: (StringConstructor | NumberConstructor)[];
    color: StringConstructor;
    hideWeekdays: BooleanConstructor;
    multiple: PropType<boolean | "range" | number | (string & {})>;
    showWeek: BooleanConstructor;
    transition: {
        type: StringConstructor;
        default: string;
    };
    reverseTransition: {
        type: StringConstructor;
        default: string;
    };
}, vue.ExtractPropTypes<{
    allowedDates: PropType<unknown[] | ((date: unknown) => boolean)>;
    disabled: BooleanConstructor;
    displayValue: PropType<unknown>;
    modelValue: PropType<unknown[]>;
    month: (StringConstructor | NumberConstructor)[];
    max: PropType<unknown>;
    min: PropType<unknown>;
    showAdjacentMonths: BooleanConstructor;
    year: (StringConstructor | NumberConstructor)[];
    weekdays: {
        type: {
            (arrayLength: number): number[];
            (...items: number[]): number[];
            new (arrayLength: number): number[];
            new (...items: number[]): number[];
            isArray(arg: any): arg is any[];
            readonly prototype: any[];
            from<T>(arrayLike: ArrayLike<T>): T[];
            from<T, U>(arrayLike: ArrayLike<T>, mapfn: (v: T, k: number) => U, thisArg?: any): U[];
            from<T>(iterable: Iterable<T> | ArrayLike<T>): T[];
            from<T, U>(iterable: Iterable<T> | ArrayLike<T>, mapfn: (v: T, k: number) => U, thisArg?: any): U[];
            of<T>(...items: T[]): T[];
            fromAsync<T>(iterableOrArrayLike: AsyncIterable<T> | Iterable<T | PromiseLike<T>> | ArrayLike<T | PromiseLike<T>>): Promise<T[]>;
            fromAsync<T, U>(iterableOrArrayLike: AsyncIterable<T> | Iterable<T> | ArrayLike<T>, mapFn: (value: Awaited<T>) => U, thisArg?: any): Promise<Awaited<U>[]>;
            readonly [Symbol.species]: ArrayConstructor;
        };
        default: () => number[];
    };
    weeksInMonth: {
        type: PropType<"dynamic" | "static">;
        default: string;
    };
    firstDayOfWeek: (StringConstructor | NumberConstructor)[];
    color: StringConstructor;
    hideWeekdays: BooleanConstructor;
    multiple: PropType<boolean | "range" | number | (string & {})>;
    showWeek: BooleanConstructor;
    transition: {
        type: StringConstructor;
        default: string;
    };
    reverseTransition: {
        type: StringConstructor;
        default: string;
    };
}>>;
type VDatePickerMonth = InstanceType<typeof VDatePickerMonth>;

declare const VDatePickerMonths: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{} & {
        max?: unknown;
        height?: string | number | undefined;
        min?: unknown;
        color?: string | undefined;
        year?: number | undefined;
        modelValue?: number | undefined;
    } & {
        $children?: {} | vue.VNodeChild | {
            month?: ((arg: {
                month: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            month?: false | ((arg: {
                month: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:month"?: false | ((arg: {
            month: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((date: any) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (date: any) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {} & {
        max?: unknown;
        height?: string | number | undefined;
        min?: unknown;
        color?: string | undefined;
        year?: number | undefined;
        modelValue?: number | undefined;
    } & {
        $children?: {} | vue.VNodeChild | {
            month?: ((arg: {
                month: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            month?: false | ((arg: {
                month: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:month"?: false | ((arg: {
            month: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((date: any) => any) | undefined;
    }, {}, true, {}, vue.SlotsType<Partial<{
        month: (arg: {
            month: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                onClick: () => void;
            };
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {} & {
        max?: unknown;
        height?: string | number | undefined;
        min?: unknown;
        color?: string | undefined;
        year?: number | undefined;
        modelValue?: number | undefined;
    } & {
        $children?: {} | vue.VNodeChild | {
            month?: ((arg: {
                month: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            month?: false | ((arg: {
                month: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:month"?: false | ((arg: {
            month: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((date: any) => any) | undefined;
    }, {}, {}, {}, {}, {}>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{} & {
    max?: unknown;
    height?: string | number | undefined;
    min?: unknown;
    color?: string | undefined;
    year?: number | undefined;
    modelValue?: number | undefined;
} & {
    $children?: {} | vue.VNodeChild | {
        month?: ((arg: {
            month: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        month?: false | ((arg: {
            month: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:month"?: false | ((arg: {
        month: {
            text: string;
            value: number;
        };
        i: number;
        props: {
            onClick: () => void;
        };
    }) => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((date: any) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (date: any) => true;
}, string, {}, {}, string, vue.SlotsType<Partial<{
    month: (arg: {
        month: {
            text: string;
            value: number;
        };
        i: number;
        props: {
            onClick: () => void;
        };
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    color: StringConstructor;
    height: (StringConstructor | NumberConstructor)[];
    min: PropType<unknown>;
    max: PropType<unknown>;
    modelValue: NumberConstructor;
    year: NumberConstructor;
}, vue.ExtractPropTypes<{
    color: StringConstructor;
    height: (StringConstructor | NumberConstructor)[];
    min: PropType<unknown>;
    max: PropType<unknown>;
    modelValue: NumberConstructor;
    year: NumberConstructor;
}>>;
type VDatePickerMonths = InstanceType<typeof VDatePickerMonths>;

declare const VDatePickerYears: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{} & {
        max?: unknown;
        height?: string | number | undefined;
        min?: unknown;
        color?: string | undefined;
        modelValue?: number | undefined;
    } & {
        $children?: {} | vue.VNodeChild | {
            year?: ((arg: {
                year: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    active: boolean;
                    color?: string;
                    rounded: boolean;
                    text: string;
                    variant: "flat" | "text";
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            year?: false | ((arg: {
                year: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    active: boolean;
                    color?: string;
                    rounded: boolean;
                    text: string;
                    variant: "flat" | "text";
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:year"?: false | ((arg: {
            year: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                active: boolean;
                color?: string;
                rounded: boolean;
                text: string;
                variant: "flat" | "text";
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((year: number) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (year: number) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {} & {
        max?: unknown;
        height?: string | number | undefined;
        min?: unknown;
        color?: string | undefined;
        modelValue?: number | undefined;
    } & {
        $children?: {} | vue.VNodeChild | {
            year?: ((arg: {
                year: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    active: boolean;
                    color?: string;
                    rounded: boolean;
                    text: string;
                    variant: "flat" | "text";
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            year?: false | ((arg: {
                year: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    active: boolean;
                    color?: string;
                    rounded: boolean;
                    text: string;
                    variant: "flat" | "text";
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:year"?: false | ((arg: {
            year: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                active: boolean;
                color?: string;
                rounded: boolean;
                text: string;
                variant: "flat" | "text";
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((year: number) => any) | undefined;
    }, {}, true, {}, vue.SlotsType<Partial<{
        year: (arg: {
            year: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                active: boolean;
                color?: string;
                rounded: boolean;
                text: string;
                variant: "flat" | "text";
                onClick: () => void;
            };
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {} & {
        max?: unknown;
        height?: string | number | undefined;
        min?: unknown;
        color?: string | undefined;
        modelValue?: number | undefined;
    } & {
        $children?: {} | vue.VNodeChild | {
            year?: ((arg: {
                year: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    active: boolean;
                    color?: string;
                    rounded: boolean;
                    text: string;
                    variant: "flat" | "text";
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            year?: false | ((arg: {
                year: {
                    text: string;
                    value: number;
                };
                i: number;
                props: {
                    active: boolean;
                    color?: string;
                    rounded: boolean;
                    text: string;
                    variant: "flat" | "text";
                    onClick: () => void;
                };
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:year"?: false | ((arg: {
            year: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                active: boolean;
                color?: string;
                rounded: boolean;
                text: string;
                variant: "flat" | "text";
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((year: number) => any) | undefined;
    }, {}, {}, {}, {}, {}>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{} & {
    max?: unknown;
    height?: string | number | undefined;
    min?: unknown;
    color?: string | undefined;
    modelValue?: number | undefined;
} & {
    $children?: {} | vue.VNodeChild | {
        year?: ((arg: {
            year: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                active: boolean;
                color?: string;
                rounded: boolean;
                text: string;
                variant: "flat" | "text";
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        year?: false | ((arg: {
            year: {
                text: string;
                value: number;
            };
            i: number;
            props: {
                active: boolean;
                color?: string;
                rounded: boolean;
                text: string;
                variant: "flat" | "text";
                onClick: () => void;
            };
        }) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:year"?: false | ((arg: {
        year: {
            text: string;
            value: number;
        };
        i: number;
        props: {
            active: boolean;
            color?: string;
            rounded: boolean;
            text: string;
            variant: "flat" | "text";
            onClick: () => void;
        };
    }) => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((year: number) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (year: number) => true;
}, string, {}, {}, string, vue.SlotsType<Partial<{
    year: (arg: {
        year: {
            text: string;
            value: number;
        };
        i: number;
        props: {
            active: boolean;
            color?: string;
            rounded: boolean;
            text: string;
            variant: "flat" | "text";
            onClick: () => void;
        };
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    color: StringConstructor;
    height: (StringConstructor | NumberConstructor)[];
    min: PropType<unknown>;
    max: PropType<unknown>;
    modelValue: NumberConstructor;
}, vue.ExtractPropTypes<{
    color: StringConstructor;
    height: (StringConstructor | NumberConstructor)[];
    min: PropType<unknown>;
    max: PropType<unknown>;
    modelValue: NumberConstructor;
}>>;
type VDatePickerYears = InstanceType<typeof VDatePickerYears>;

export { VDatePicker, VDatePickerControls, VDatePickerHeader, VDatePickerMonth, VDatePickerMonths, VDatePickerYears };
