import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType } from 'vue';

type ClassValue = any;

declare const block: readonly ["top", "bottom"];
declare const inline: readonly ["start", "end", "left", "right"];
type Tblock = typeof block[number];
type Tinline = typeof inline[number];
type Anchor = Tblock | Tinline | 'center' | 'center center' | `${Tblock} ${Tinline | 'center'}` | `${Tinline} ${Tblock | 'center'}`;

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type Period$1 = 'am' | 'pm';
type AllowFunction = (val: number) => boolean;

declare const VTimePicker: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        readonly: boolean;
        format: "ampm" | "24hr";
        tag: string;
        tile: boolean;
        scrollable: boolean;
        hideHeader: boolean;
        ampmInTitle: boolean;
        useSeconds: boolean;
    } & {
        max?: string | undefined;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        min?: string | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        modelValue?: any;
        rounded?: string | number | boolean | undefined;
        bgColor?: string | undefined;
        allowedHours?: number[] | AllowFunction | undefined;
        allowedMinutes?: number[] | AllowFunction | undefined;
        allowedSeconds?: number[] | AllowFunction | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            actions?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            actions?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:actions"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: string) => any) | undefined;
        "onUpdate:period"?: ((val: Period$1) => any) | undefined;
        "onUpdate:hour"?: ((val: number) => any) | undefined;
        "onUpdate:minute"?: ((val: number) => any) | undefined;
        "onUpdate:second"?: ((val: number) => any) | undefined;
    }, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:hour': (val: number) => true;
        'update:minute': (val: number) => true;
        'update:period': (val: Period$1) => true;
        'update:second': (val: number) => true;
        'update:modelValue': (val: string) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        readonly: boolean;
        format: "ampm" | "24hr";
        tag: string;
        tile: boolean;
        scrollable: boolean;
        hideHeader: boolean;
        ampmInTitle: boolean;
        useSeconds: boolean;
    } & {
        max?: string | undefined;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        min?: string | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        modelValue?: any;
        rounded?: string | number | boolean | undefined;
        bgColor?: string | undefined;
        allowedHours?: number[] | AllowFunction | undefined;
        allowedMinutes?: number[] | AllowFunction | undefined;
        allowedSeconds?: number[] | AllowFunction | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            actions?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            actions?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:actions"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: string) => any) | undefined;
        "onUpdate:period"?: ((val: Period$1) => any) | undefined;
        "onUpdate:hour"?: ((val: number) => any) | undefined;
        "onUpdate:minute"?: ((val: number) => any) | undefined;
        "onUpdate:second"?: ((val: number) => any) | undefined;
    }, {
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        readonly: boolean;
        format: "ampm" | "24hr";
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        scrollable: boolean;
        hideHeader: boolean;
        ampmInTitle: boolean;
        useSeconds: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        title: () => vue.VNode[];
        actions: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        readonly: boolean;
        format: "ampm" | "24hr";
        tag: string;
        tile: boolean;
        scrollable: boolean;
        hideHeader: boolean;
        ampmInTitle: boolean;
        useSeconds: boolean;
    } & {
        max?: string | undefined;
        location?: Anchor | null | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        min?: string | undefined;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        modelValue?: any;
        rounded?: string | number | boolean | undefined;
        bgColor?: string | undefined;
        allowedHours?: number[] | AllowFunction | undefined;
        allowedMinutes?: number[] | AllowFunction | undefined;
        allowedSeconds?: number[] | AllowFunction | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            title?: (() => vue.VNodeChild) | undefined;
            actions?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            title?: false | (() => vue.VNodeChild) | undefined;
            actions?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:actions"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((val: string) => any) | undefined;
        "onUpdate:period"?: ((val: Period$1) => any) | undefined;
        "onUpdate:hour"?: ((val: number) => any) | undefined;
        "onUpdate:minute"?: ((val: number) => any) | undefined;
        "onUpdate:second"?: ((val: number) => any) | undefined;
    }, {}, {}, {}, {}, {
        style: vue.StyleValue;
        title: string;
        disabled: boolean;
        readonly: boolean;
        format: "ampm" | "24hr";
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        scrollable: boolean;
        hideHeader: boolean;
        ampmInTitle: boolean;
        useSeconds: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    title: string;
    disabled: boolean;
    readonly: boolean;
    format: "ampm" | "24hr";
    tag: string;
    tile: boolean;
    scrollable: boolean;
    hideHeader: boolean;
    ampmInTitle: boolean;
    useSeconds: boolean;
} & {
    max?: string | undefined;
    location?: Anchor | null | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    min?: string | undefined;
    border?: string | number | boolean | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
    class?: any;
    theme?: string | undefined;
    elevation?: string | number | undefined;
    modelValue?: any;
    rounded?: string | number | boolean | undefined;
    bgColor?: string | undefined;
    allowedHours?: number[] | AllowFunction | undefined;
    allowedMinutes?: number[] | AllowFunction | undefined;
    allowedSeconds?: number[] | AllowFunction | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        title?: (() => vue.VNodeChild) | undefined;
        actions?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        title?: false | (() => vue.VNodeChild) | undefined;
        actions?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:title"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:actions"?: false | (() => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((val: string) => any) | undefined;
    "onUpdate:period"?: ((val: Period$1) => any) | undefined;
    "onUpdate:hour"?: ((val: number) => any) | undefined;
    "onUpdate:minute"?: ((val: number) => any) | undefined;
    "onUpdate:second"?: ((val: number) => any) | undefined;
}, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:hour': (val: number) => true;
    'update:minute': (val: number) => true;
    'update:period': (val: Period$1) => true;
    'update:second': (val: number) => true;
    'update:modelValue': (val: string) => true;
}, string, {
    style: vue.StyleValue;
    title: string;
    disabled: boolean;
    readonly: boolean;
    format: "ampm" | "24hr";
    tag: string;
    rounded: string | number | boolean;
    tile: boolean;
    scrollable: boolean;
    hideHeader: boolean;
    ampmInTitle: boolean;
    useSeconds: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    title: () => vue.VNode[];
    actions: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    location: PropType<Anchor | null>;
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    position: {
        type: PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    title: {
        type: PropType<string>;
        default: string;
    };
    class: PropType<ClassValue>;
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    bgColor: StringConstructor;
    hideHeader: BooleanConstructor;
    allowedHours: PropType<AllowFunction | number[]>;
    allowedMinutes: PropType<AllowFunction | number[]>;
    allowedSeconds: PropType<AllowFunction | number[]>;
    ampmInTitle: BooleanConstructor;
    disabled: BooleanConstructor;
    format: {
        type: PropType<"ampm" | "24hr">;
        default: string;
    };
    max: StringConstructor;
    min: StringConstructor;
    modelValue: PropType<any>;
    readonly: BooleanConstructor;
    scrollable: BooleanConstructor;
    useSeconds: BooleanConstructor;
}, vue.ExtractPropTypes<{
    location: PropType<Anchor | null>;
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    position: {
        type: PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    title: {
        type: PropType<string>;
        default: string;
    };
    class: PropType<ClassValue>;
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    bgColor: StringConstructor;
    hideHeader: BooleanConstructor;
    allowedHours: PropType<AllowFunction | number[]>;
    allowedMinutes: PropType<AllowFunction | number[]>;
    allowedSeconds: PropType<AllowFunction | number[]>;
    ampmInTitle: BooleanConstructor;
    disabled: BooleanConstructor;
    format: {
        type: PropType<"ampm" | "24hr">;
        default: string;
    };
    max: StringConstructor;
    min: StringConstructor;
    modelValue: PropType<any>;
    readonly: BooleanConstructor;
    scrollable: BooleanConstructor;
    useSeconds: BooleanConstructor;
}>>;
type VTimePicker = InstanceType<typeof VTimePicker>;

declare const VTimePickerClock: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        max: number;
        min: number;
        rotate: number;
        disabled: boolean;
        readonly: boolean;
        format: Function;
        double: boolean;
        step: number;
        scrollable: boolean;
        ampm: boolean;
    } & {
        color?: string | undefined;
        modelValue?: number | undefined;
        displayedValue?: any;
        allowedValues?: ((value: number) => boolean) | undefined;
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
        onChange?: ((val: number) => any) | undefined;
        onInput?: ((val: number) => any) | undefined;
    }, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        change: (val: number) => true;
        input: (val: number) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        max: number;
        min: number;
        rotate: number;
        disabled: boolean;
        readonly: boolean;
        format: Function;
        double: boolean;
        step: number;
        scrollable: boolean;
        ampm: boolean;
    } & {
        color?: string | undefined;
        modelValue?: number | undefined;
        displayedValue?: any;
        allowedValues?: ((value: number) => boolean) | undefined;
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
        onChange?: ((val: number) => any) | undefined;
        onInput?: ((val: number) => any) | undefined;
    }, {
        rotate: number;
        disabled: boolean;
        readonly: boolean;
        format: Function;
        double: boolean;
        step: number;
        scrollable: boolean;
        ampm: boolean;
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
        max: number;
        min: number;
        rotate: number;
        disabled: boolean;
        readonly: boolean;
        format: Function;
        double: boolean;
        step: number;
        scrollable: boolean;
        ampm: boolean;
    } & {
        color?: string | undefined;
        modelValue?: number | undefined;
        displayedValue?: any;
        allowedValues?: ((value: number) => boolean) | undefined;
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
        onChange?: ((val: number) => any) | undefined;
        onInput?: ((val: number) => any) | undefined;
    }, {}, {}, {}, {}, {
        rotate: number;
        disabled: boolean;
        readonly: boolean;
        format: Function;
        double: boolean;
        step: number;
        scrollable: boolean;
        ampm: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    max: number;
    min: number;
    rotate: number;
    disabled: boolean;
    readonly: boolean;
    format: Function;
    double: boolean;
    step: number;
    scrollable: boolean;
    ampm: boolean;
} & {
    color?: string | undefined;
    modelValue?: number | undefined;
    displayedValue?: any;
    allowedValues?: ((value: number) => boolean) | undefined;
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
    onChange?: ((val: number) => any) | undefined;
    onInput?: ((val: number) => any) | undefined;
}, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    change: (val: number) => true;
    input: (val: number) => true;
}, string, {
    rotate: number;
    disabled: boolean;
    readonly: boolean;
    format: Function;
    double: boolean;
    step: number;
    scrollable: boolean;
    ampm: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    allowedValues: PropType<(value: number) => boolean>;
    ampm: BooleanConstructor;
    color: StringConstructor;
    disabled: BooleanConstructor;
    displayedValue: null;
    double: BooleanConstructor;
    format: {
        type: FunctionConstructor;
        default: (val: string | number) => string | number;
    };
    max: {
        type: NumberConstructor;
        required: true;
    };
    min: {
        type: NumberConstructor;
        required: true;
    };
    scrollable: BooleanConstructor;
    readonly: BooleanConstructor;
    rotate: {
        type: NumberConstructor;
        default: number;
    };
    step: {
        type: NumberConstructor;
        default: number;
    };
    modelValue: {
        type: NumberConstructor;
    };
}, vue.ExtractPropTypes<{
    allowedValues: PropType<(value: number) => boolean>;
    ampm: BooleanConstructor;
    color: StringConstructor;
    disabled: BooleanConstructor;
    displayedValue: null;
    double: BooleanConstructor;
    format: {
        type: FunctionConstructor;
        default: (val: string | number) => string | number;
    };
    max: {
        type: NumberConstructor;
        required: true;
    };
    min: {
        type: NumberConstructor;
        required: true;
    };
    scrollable: BooleanConstructor;
    readonly: BooleanConstructor;
    rotate: {
        type: NumberConstructor;
        default: number;
    };
    step: {
        type: NumberConstructor;
        default: number;
    };
    modelValue: {
        type: NumberConstructor;
    };
}>>;
type VTimePickerClock = InstanceType<typeof VTimePickerClock>;

type Period = 'am' | 'pm';
declare const VTimePickerControls: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        disabled: boolean;
        readonly: boolean;
        ampm: boolean;
        ampmInTitle: boolean;
        ampmReadonly: boolean;
        useSeconds: boolean;
    } & {
        color?: string | undefined;
        value?: number | undefined;
        hour?: number | undefined;
        minute?: number | undefined;
        second?: number | undefined;
        period?: string | undefined;
        selecting?: number | undefined;
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
        "onUpdate:period"?: ((data: Period) => any) | undefined;
        "onUpdate:selecting"?: ((data: 2 | 1 | 3) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:period': (data: Period) => true;
        'update:selecting': (data: 1 | 2 | 3) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        disabled: boolean;
        readonly: boolean;
        ampm: boolean;
        ampmInTitle: boolean;
        ampmReadonly: boolean;
        useSeconds: boolean;
    } & {
        color?: string | undefined;
        value?: number | undefined;
        hour?: number | undefined;
        minute?: number | undefined;
        second?: number | undefined;
        period?: string | undefined;
        selecting?: number | undefined;
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
        "onUpdate:period"?: ((data: Period) => any) | undefined;
        "onUpdate:selecting"?: ((data: 2 | 1 | 3) => any) | undefined;
    }, {
        disabled: boolean;
        readonly: boolean;
        ampm: boolean;
        ampmInTitle: boolean;
        ampmReadonly: boolean;
        useSeconds: boolean;
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
        readonly: boolean;
        ampm: boolean;
        ampmInTitle: boolean;
        ampmReadonly: boolean;
        useSeconds: boolean;
    } & {
        color?: string | undefined;
        value?: number | undefined;
        hour?: number | undefined;
        minute?: number | undefined;
        second?: number | undefined;
        period?: string | undefined;
        selecting?: number | undefined;
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
        "onUpdate:period"?: ((data: Period) => any) | undefined;
        "onUpdate:selecting"?: ((data: 2 | 1 | 3) => any) | undefined;
    }, {}, {}, {}, {}, {
        disabled: boolean;
        readonly: boolean;
        ampm: boolean;
        ampmInTitle: boolean;
        ampmReadonly: boolean;
        useSeconds: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    disabled: boolean;
    readonly: boolean;
    ampm: boolean;
    ampmInTitle: boolean;
    ampmReadonly: boolean;
    useSeconds: boolean;
} & {
    color?: string | undefined;
    value?: number | undefined;
    hour?: number | undefined;
    minute?: number | undefined;
    second?: number | undefined;
    period?: string | undefined;
    selecting?: number | undefined;
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
    "onUpdate:period"?: ((data: Period) => any) | undefined;
    "onUpdate:selecting"?: ((data: 2 | 1 | 3) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:period': (data: Period) => true;
    'update:selecting': (data: 1 | 2 | 3) => true;
}, string, {
    disabled: boolean;
    readonly: boolean;
    ampm: boolean;
    ampmInTitle: boolean;
    ampmReadonly: boolean;
    useSeconds: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    ampm: BooleanConstructor;
    ampmInTitle: BooleanConstructor;
    ampmReadonly: BooleanConstructor;
    color: StringConstructor;
    disabled: BooleanConstructor;
    hour: NumberConstructor;
    minute: NumberConstructor;
    second: NumberConstructor;
    period: StringConstructor;
    readonly: BooleanConstructor;
    useSeconds: BooleanConstructor;
    selecting: NumberConstructor;
    value: NumberConstructor;
}, vue.ExtractPropTypes<{
    ampm: BooleanConstructor;
    ampmInTitle: BooleanConstructor;
    ampmReadonly: BooleanConstructor;
    color: StringConstructor;
    disabled: BooleanConstructor;
    hour: NumberConstructor;
    minute: NumberConstructor;
    second: NumberConstructor;
    period: StringConstructor;
    readonly: BooleanConstructor;
    useSeconds: BooleanConstructor;
    selecting: NumberConstructor;
    value: NumberConstructor;
}>>;
type VTimePickerControls = InstanceType<typeof VTimePickerControls>;

export { VTimePicker, VTimePickerClock, VTimePickerControls };
