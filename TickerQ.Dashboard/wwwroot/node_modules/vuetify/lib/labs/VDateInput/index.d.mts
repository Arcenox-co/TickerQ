import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType, ComponentPublicInstance, FunctionalComponent } from 'vue';

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

type EventProp<T extends any[] = any[], F = (...args: T) => void> = F;
declare const EventProp: <T extends any[] = any[]>() => PropType<EventProp<T>>;

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

declare const VDateInput: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        flat: boolean;
        reverse: boolean;
        variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
        type: string;
        error: boolean;
        active: boolean;
        direction: "horizontal" | "vertical";
        transition: string;
        header: string;
        style: vue.StyleValue;
        title: string;
        autofocus: boolean;
        disabled: boolean;
        readonly: boolean | null;
        tag: string;
        landscape: boolean;
        placeholder: string;
        messages: string | readonly string[];
        focused: boolean;
        errorMessages: string | readonly string[] | null;
        maxErrors: string | number;
        rules: readonly ValidationRule[];
        tile: boolean;
        density: Density;
        prependIcon: IconValue;
        clearIcon: IconValue;
        hideSpinButtons: boolean;
        persistentHint: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        clearable: boolean;
        dirty: boolean;
        persistentClear: boolean;
        singleLine: boolean;
        persistentPlaceholder: boolean;
        persistentCounter: boolean;
        reverseTransition: string;
        cancelText: string;
        okText: string;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
        hideHeader: boolean;
        hideActions: boolean;
    } & {
        name?: string | undefined;
        max?: unknown;
        location?: Anchor | null | undefined;
        id?: string | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        min?: unknown;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        loading?: string | boolean | undefined;
        label?: string | undefined;
        text?: string | undefined;
        prefix?: string | undefined;
        role?: string | undefined;
        multiple?: number | boolean | "range" | (string & {}) | undefined;
        month?: string | number | undefined;
        year?: number | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        counter?: string | number | boolean | undefined;
        'onUpdate:focused'?: ((args_0: boolean) => void) | undefined;
        modelValue?: any;
        validateOn?: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
        validationValue?: any;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        appendIcon?: IconValue | undefined;
        appendInnerIcon?: IconValue | undefined;
        prependInnerIcon?: IconValue | undefined;
        'onClick:clear'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:append'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prepend'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:appendInner'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prependInner'?: ((args_0: MouseEvent) => void) | undefined;
        centerAffix?: boolean | undefined;
        hint?: string | undefined;
        hideDetails?: boolean | "auto" | undefined;
        suffix?: string | undefined;
        counterValue?: number | ((value: any) => number) | undefined;
        modelModifiers?: Record<string, boolean> | undefined;
        firstDayOfWeek?: string | number | undefined;
        allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
        displayValue?: unknown;
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
        "onUpdate:modelValue"?: ((val: string) => any) | undefined;
    }, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (val: string) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        flat: boolean;
        reverse: boolean;
        variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
        type: string;
        error: boolean;
        active: boolean;
        direction: "horizontal" | "vertical";
        transition: string;
        header: string;
        style: vue.StyleValue;
        title: string;
        autofocus: boolean;
        disabled: boolean;
        readonly: boolean | null;
        tag: string;
        landscape: boolean;
        placeholder: string;
        messages: string | readonly string[];
        focused: boolean;
        errorMessages: string | readonly string[] | null;
        maxErrors: string | number;
        rules: readonly ValidationRule[];
        tile: boolean;
        density: Density;
        prependIcon: IconValue;
        clearIcon: IconValue;
        hideSpinButtons: boolean;
        persistentHint: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        clearable: boolean;
        dirty: boolean;
        persistentClear: boolean;
        singleLine: boolean;
        persistentPlaceholder: boolean;
        persistentCounter: boolean;
        reverseTransition: string;
        cancelText: string;
        okText: string;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
        hideHeader: boolean;
        hideActions: boolean;
    } & {
        name?: string | undefined;
        max?: unknown;
        location?: Anchor | null | undefined;
        id?: string | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        min?: unknown;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        loading?: string | boolean | undefined;
        label?: string | undefined;
        text?: string | undefined;
        prefix?: string | undefined;
        role?: string | undefined;
        multiple?: number | boolean | "range" | (string & {}) | undefined;
        month?: string | number | undefined;
        year?: number | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        counter?: string | number | boolean | undefined;
        'onUpdate:focused'?: ((args_0: boolean) => void) | undefined;
        modelValue?: any;
        validateOn?: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
        validationValue?: any;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        appendIcon?: IconValue | undefined;
        appendInnerIcon?: IconValue | undefined;
        prependInnerIcon?: IconValue | undefined;
        'onClick:clear'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:append'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prepend'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:appendInner'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prependInner'?: ((args_0: MouseEvent) => void) | undefined;
        centerAffix?: boolean | undefined;
        hint?: string | undefined;
        hideDetails?: boolean | "auto" | undefined;
        suffix?: string | undefined;
        counterValue?: number | ((value: any) => number) | undefined;
        modelModifiers?: Record<string, boolean> | undefined;
        firstDayOfWeek?: string | number | undefined;
        allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
        displayValue?: unknown;
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
        "onUpdate:modelValue"?: ((val: string) => any) | undefined;
    }, {
        flat: boolean;
        reverse: boolean;
        variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
        type: string;
        error: boolean;
        active: boolean;
        direction: "horizontal" | "vertical";
        transition: string;
        header: string;
        style: vue.StyleValue;
        title: string;
        autofocus: boolean;
        disabled: boolean;
        readonly: boolean | null;
        tag: string;
        landscape: boolean;
        placeholder: string;
        messages: string | readonly string[];
        focused: boolean;
        errorMessages: string | readonly string[] | null;
        maxErrors: string | number;
        rules: readonly ValidationRule[];
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        prependIcon: IconValue;
        clearIcon: IconValue;
        centerAffix: boolean;
        hideSpinButtons: boolean;
        persistentHint: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        clearable: boolean;
        dirty: boolean;
        persistentClear: boolean;
        singleLine: boolean;
        persistentPlaceholder: boolean;
        persistentCounter: boolean;
        reverseTransition: string;
        cancelText: string;
        okText: string;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
        hideHeader: boolean;
        hideActions: boolean;
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
        flat: boolean;
        reverse: boolean;
        variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
        type: string;
        error: boolean;
        active: boolean;
        direction: "horizontal" | "vertical";
        transition: string;
        header: string;
        style: vue.StyleValue;
        title: string;
        autofocus: boolean;
        disabled: boolean;
        readonly: boolean | null;
        tag: string;
        landscape: boolean;
        placeholder: string;
        messages: string | readonly string[];
        focused: boolean;
        errorMessages: string | readonly string[] | null;
        maxErrors: string | number;
        rules: readonly ValidationRule[];
        tile: boolean;
        density: Density;
        prependIcon: IconValue;
        clearIcon: IconValue;
        hideSpinButtons: boolean;
        persistentHint: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        clearable: boolean;
        dirty: boolean;
        persistentClear: boolean;
        singleLine: boolean;
        persistentPlaceholder: boolean;
        persistentCounter: boolean;
        reverseTransition: string;
        cancelText: string;
        okText: string;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
        hideHeader: boolean;
        hideActions: boolean;
    } & {
        name?: string | undefined;
        max?: unknown;
        location?: Anchor | null | undefined;
        id?: string | undefined;
        height?: string | number | undefined;
        width?: string | number | undefined;
        min?: unknown;
        border?: string | number | boolean | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
        loading?: string | boolean | undefined;
        label?: string | undefined;
        text?: string | undefined;
        prefix?: string | undefined;
        role?: string | undefined;
        multiple?: number | boolean | "range" | (string & {}) | undefined;
        month?: string | number | undefined;
        year?: number | undefined;
        class?: any;
        theme?: string | undefined;
        elevation?: string | number | undefined;
        counter?: string | number | boolean | undefined;
        'onUpdate:focused'?: ((args_0: boolean) => void) | undefined;
        modelValue?: any;
        validateOn?: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
        validationValue?: any;
        rounded?: string | number | boolean | undefined;
        baseColor?: string | undefined;
        bgColor?: string | undefined;
        appendIcon?: IconValue | undefined;
        appendInnerIcon?: IconValue | undefined;
        prependInnerIcon?: IconValue | undefined;
        'onClick:clear'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:append'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prepend'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:appendInner'?: ((args_0: MouseEvent) => void) | undefined;
        'onClick:prependInner'?: ((args_0: MouseEvent) => void) | undefined;
        centerAffix?: boolean | undefined;
        hint?: string | undefined;
        hideDetails?: boolean | "auto" | undefined;
        suffix?: string | undefined;
        counterValue?: number | ((value: any) => number) | undefined;
        modelModifiers?: Record<string, boolean> | undefined;
        firstDayOfWeek?: string | number | undefined;
        allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
        displayValue?: unknown;
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
        "onUpdate:modelValue"?: ((val: string) => any) | undefined;
    }, {}, {}, {}, {}, {
        flat: boolean;
        reverse: boolean;
        variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
        type: string;
        error: boolean;
        active: boolean;
        direction: "horizontal" | "vertical";
        transition: string;
        header: string;
        style: vue.StyleValue;
        title: string;
        autofocus: boolean;
        disabled: boolean;
        readonly: boolean | null;
        tag: string;
        landscape: boolean;
        placeholder: string;
        messages: string | readonly string[];
        focused: boolean;
        errorMessages: string | readonly string[] | null;
        maxErrors: string | number;
        rules: readonly ValidationRule[];
        rounded: string | number | boolean;
        tile: boolean;
        density: Density;
        prependIcon: IconValue;
        clearIcon: IconValue;
        centerAffix: boolean;
        hideSpinButtons: boolean;
        persistentHint: boolean;
        nextIcon: IconValue;
        prevIcon: IconValue;
        clearable: boolean;
        dirty: boolean;
        persistentClear: boolean;
        singleLine: boolean;
        persistentPlaceholder: boolean;
        persistentCounter: boolean;
        reverseTransition: string;
        cancelText: string;
        okText: string;
        modeIcon: IconValue;
        viewMode: "month" | "year" | "months";
        showAdjacentMonths: boolean;
        weekdays: number[];
        weeksInMonth: "static" | "dynamic";
        hideWeekdays: boolean;
        showWeek: boolean;
        hideHeader: boolean;
        hideActions: boolean;
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
    transition: string;
    header: string;
    style: vue.StyleValue;
    title: string;
    autofocus: boolean;
    disabled: boolean;
    readonly: boolean | null;
    tag: string;
    landscape: boolean;
    placeholder: string;
    messages: string | readonly string[];
    focused: boolean;
    errorMessages: string | readonly string[] | null;
    maxErrors: string | number;
    rules: readonly ValidationRule[];
    tile: boolean;
    density: Density;
    prependIcon: IconValue;
    clearIcon: IconValue;
    hideSpinButtons: boolean;
    persistentHint: boolean;
    nextIcon: IconValue;
    prevIcon: IconValue;
    clearable: boolean;
    dirty: boolean;
    persistentClear: boolean;
    singleLine: boolean;
    persistentPlaceholder: boolean;
    persistentCounter: boolean;
    reverseTransition: string;
    cancelText: string;
    okText: string;
    modeIcon: IconValue;
    viewMode: "month" | "year" | "months";
    showAdjacentMonths: boolean;
    weekdays: number[];
    weeksInMonth: "static" | "dynamic";
    hideWeekdays: boolean;
    showWeek: boolean;
    hideHeader: boolean;
    hideActions: boolean;
} & {
    name?: string | undefined;
    max?: unknown;
    location?: Anchor | null | undefined;
    id?: string | undefined;
    height?: string | number | undefined;
    width?: string | number | undefined;
    min?: unknown;
    border?: string | number | boolean | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    position?: "fixed" | "absolute" | "relative" | "static" | "sticky" | undefined;
    loading?: string | boolean | undefined;
    label?: string | undefined;
    text?: string | undefined;
    prefix?: string | undefined;
    role?: string | undefined;
    multiple?: number | boolean | "range" | (string & {}) | undefined;
    month?: string | number | undefined;
    year?: number | undefined;
    class?: any;
    theme?: string | undefined;
    elevation?: string | number | undefined;
    counter?: string | number | boolean | undefined;
    'onUpdate:focused'?: ((args_0: boolean) => void) | undefined;
    modelValue?: any;
    validateOn?: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
    validationValue?: any;
    rounded?: string | number | boolean | undefined;
    baseColor?: string | undefined;
    bgColor?: string | undefined;
    appendIcon?: IconValue | undefined;
    appendInnerIcon?: IconValue | undefined;
    prependInnerIcon?: IconValue | undefined;
    'onClick:clear'?: ((args_0: MouseEvent) => void) | undefined;
    'onClick:append'?: ((args_0: MouseEvent) => void) | undefined;
    'onClick:prepend'?: ((args_0: MouseEvent) => void) | undefined;
    'onClick:appendInner'?: ((args_0: MouseEvent) => void) | undefined;
    'onClick:prependInner'?: ((args_0: MouseEvent) => void) | undefined;
    centerAffix?: boolean | undefined;
    hint?: string | undefined;
    hideDetails?: boolean | "auto" | undefined;
    suffix?: string | undefined;
    counterValue?: number | ((value: any) => number) | undefined;
    modelModifiers?: Record<string, boolean> | undefined;
    firstDayOfWeek?: string | number | undefined;
    allowedDates?: unknown[] | ((date: unknown) => boolean) | undefined;
    displayValue?: unknown;
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
    "onUpdate:modelValue"?: ((val: string) => any) | undefined;
}, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (val: string) => true;
}, string, {
    flat: boolean;
    reverse: boolean;
    variant: "filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled";
    type: string;
    error: boolean;
    active: boolean;
    direction: "horizontal" | "vertical";
    transition: string;
    header: string;
    style: vue.StyleValue;
    title: string;
    autofocus: boolean;
    disabled: boolean;
    readonly: boolean | null;
    tag: string;
    landscape: boolean;
    placeholder: string;
    messages: string | readonly string[];
    focused: boolean;
    errorMessages: string | readonly string[] | null;
    maxErrors: string | number;
    rules: readonly ValidationRule[];
    rounded: string | number | boolean;
    tile: boolean;
    density: Density;
    prependIcon: IconValue;
    clearIcon: IconValue;
    centerAffix: boolean;
    hideSpinButtons: boolean;
    persistentHint: boolean;
    nextIcon: IconValue;
    prevIcon: IconValue;
    clearable: boolean;
    dirty: boolean;
    persistentClear: boolean;
    singleLine: boolean;
    persistentPlaceholder: boolean;
    persistentCounter: boolean;
    reverseTransition: string;
    cancelText: string;
    okText: string;
    modeIcon: IconValue;
    viewMode: "month" | "year" | "months";
    showAdjacentMonths: boolean;
    weekdays: number[];
    weeksInMonth: "static" | "dynamic";
    hideWeekdays: boolean;
    showWeek: boolean;
    hideHeader: boolean;
    hideActions: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    max: vue.PropType<unknown>;
    location: vue.PropType<Anchor | null>;
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    min: vue.PropType<unknown>;
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    position: {
        type: vue.PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    transition: {
        type: StringConstructor;
        default: string;
    };
    header: {
        type: StringConstructor;
        default: string;
    };
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    title: {
        type: vue.PropType<string>;
        default: string;
    };
    text: StringConstructor;
    disabled: BooleanConstructor;
    multiple: vue.PropType<boolean | "range" | number | (string & {})>;
    month: (StringConstructor | NumberConstructor)[];
    year: NumberConstructor;
    class: vue.PropType<ClassValue>;
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    landscape: BooleanConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    modelValue: null;
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    bgColor: StringConstructor;
    nextIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    prevIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    reverseTransition: {
        type: StringConstructor;
        default: string;
    };
    modeIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    viewMode: {
        type: vue.PropType<"month" | "months" | "year">;
        default: string;
    };
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
    weeksInMonth: Omit<Omit<{
        type: vue.PropType<"dynamic" | "static">;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<"static" | "dynamic">;
        default: NonNullable<"static" | "dynamic">;
    }, "type" | "default"> & {
        type: vue.PropType<"static" | "dynamic">;
        default: NonNullable<"static" | "dynamic">;
    };
    firstDayOfWeek: (StringConstructor | NumberConstructor)[];
    allowedDates: vue.PropType<unknown[] | ((date: unknown) => boolean)>;
    displayValue: vue.PropType<unknown>;
    hideWeekdays: BooleanConstructor;
    showWeek: BooleanConstructor;
    hideHeader: {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    loading: (StringConstructor | BooleanConstructor)[];
    appendInnerIcon: vue.PropType<IconValue>;
    clearable: BooleanConstructor;
    clearIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    active: BooleanConstructor;
    centerAffix: {
        type: BooleanConstructor;
        default: undefined;
    };
    baseColor: StringConstructor;
    dirty: BooleanConstructor;
    error: BooleanConstructor;
    flat: BooleanConstructor;
    label: StringConstructor;
    persistentClear: BooleanConstructor;
    prependInnerIcon: vue.PropType<IconValue>;
    reverse: BooleanConstructor;
    singleLine: BooleanConstructor;
    variant: {
        type: vue.PropType<"filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled">;
        default: string;
        validator: (v: any) => boolean;
    };
    'onClick:clear': vue.PropType<(args_0: MouseEvent) => void>;
    'onClick:appendInner': vue.PropType<(args_0: MouseEvent) => void>;
    'onClick:prependInner': vue.PropType<(args_0: MouseEvent) => void>;
    focused: BooleanConstructor;
    'onUpdate:focused': vue.PropType<(args_0: boolean) => void>;
    errorMessages: {
        type: vue.PropType<string | readonly string[] | null>;
        default: () => never[];
    };
    maxErrors: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    name: StringConstructor;
    readonly: {
        type: vue.PropType<boolean | null>;
        default: null;
    };
    rules: {
        type: vue.PropType<readonly ValidationRule[]>;
        default: () => never[];
    };
    validateOn: vue.PropType<ValidationProps["validateOn"]>;
    validationValue: null;
    density: {
        type: vue.PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    id: StringConstructor;
    appendIcon: vue.PropType<IconValue>;
    prependIcon: {
        type: vue.PropType<IconValue>;
        default: NonNullable<IconValue>;
    };
    hideDetails: vue.PropType<boolean | "auto">;
    hideSpinButtons: BooleanConstructor;
    hint: StringConstructor;
    persistentHint: BooleanConstructor;
    messages: {
        type: vue.PropType<string | readonly string[]>;
        default: () => never[];
    };
    direction: {
        type: vue.PropType<"horizontal" | "vertical">;
        default: string;
        validator: (v: any) => boolean;
    };
    'onClick:prepend': vue.PropType<(args_0: MouseEvent) => void>;
    'onClick:append': vue.PropType<(args_0: MouseEvent) => void>;
    autofocus: BooleanConstructor;
    counter: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    counterValue: vue.PropType<number | ((value: any) => number)>;
    prefix: StringConstructor;
    placeholder: {
        type: vue.PropType<string>;
        default: string;
    };
    persistentPlaceholder: BooleanConstructor;
    persistentCounter: BooleanConstructor;
    suffix: StringConstructor;
    role: StringConstructor;
    type: {
        type: StringConstructor;
        default: string;
    };
    modelModifiers: vue.PropType<Record<string, boolean>>;
    cancelText: {
        type: StringConstructor;
        default: string;
    };
    okText: {
        type: StringConstructor;
        default: string;
    };
    hideActions: BooleanConstructor;
}, vue.ExtractPropTypes<{
    max: vue.PropType<unknown>;
    location: vue.PropType<Anchor | null>;
    height: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    min: vue.PropType<unknown>;
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    position: {
        type: vue.PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    transition: {
        type: StringConstructor;
        default: string;
    };
    header: {
        type: StringConstructor;
        default: string;
    };
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    title: {
        type: vue.PropType<string>;
        default: string;
    };
    text: StringConstructor;
    disabled: BooleanConstructor;
    multiple: vue.PropType<boolean | "range" | number | (string & {})>;
    month: (StringConstructor | NumberConstructor)[];
    year: NumberConstructor;
    class: vue.PropType<ClassValue>;
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    landscape: BooleanConstructor;
    elevation: {
        type: (StringConstructor | NumberConstructor)[];
        validator(v: any): boolean;
    };
    modelValue: null;
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    bgColor: StringConstructor;
    nextIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    prevIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    reverseTransition: {
        type: StringConstructor;
        default: string;
    };
    modeIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    viewMode: {
        type: vue.PropType<"month" | "months" | "year">;
        default: string;
    };
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
    weeksInMonth: Omit<Omit<{
        type: vue.PropType<"dynamic" | "static">;
        default: string;
    }, "type" | "default"> & {
        type: vue.PropType<"static" | "dynamic">;
        default: NonNullable<"static" | "dynamic">;
    }, "type" | "default"> & {
        type: vue.PropType<"static" | "dynamic">;
        default: NonNullable<"static" | "dynamic">;
    };
    firstDayOfWeek: (StringConstructor | NumberConstructor)[];
    allowedDates: vue.PropType<unknown[] | ((date: unknown) => boolean)>;
    displayValue: vue.PropType<unknown>;
    hideWeekdays: BooleanConstructor;
    showWeek: BooleanConstructor;
    hideHeader: {
        type: vue.PropType<boolean>;
        default: boolean;
    };
    loading: (StringConstructor | BooleanConstructor)[];
    appendInnerIcon: vue.PropType<IconValue>;
    clearable: BooleanConstructor;
    clearIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    active: BooleanConstructor;
    centerAffix: {
        type: BooleanConstructor;
        default: undefined;
    };
    baseColor: StringConstructor;
    dirty: BooleanConstructor;
    error: BooleanConstructor;
    flat: BooleanConstructor;
    label: StringConstructor;
    persistentClear: BooleanConstructor;
    prependInnerIcon: vue.PropType<IconValue>;
    reverse: BooleanConstructor;
    singleLine: BooleanConstructor;
    variant: {
        type: vue.PropType<"filled" | "underlined" | "outlined" | "plain" | "solo" | "solo-inverted" | "solo-filled">;
        default: string;
        validator: (v: any) => boolean;
    };
    'onClick:clear': vue.PropType<(args_0: MouseEvent) => void>;
    'onClick:appendInner': vue.PropType<(args_0: MouseEvent) => void>;
    'onClick:prependInner': vue.PropType<(args_0: MouseEvent) => void>;
    focused: BooleanConstructor;
    'onUpdate:focused': vue.PropType<(args_0: boolean) => void>;
    errorMessages: {
        type: vue.PropType<string | readonly string[] | null>;
        default: () => never[];
    };
    maxErrors: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    name: StringConstructor;
    readonly: {
        type: vue.PropType<boolean | null>;
        default: null;
    };
    rules: {
        type: vue.PropType<readonly ValidationRule[]>;
        default: () => never[];
    };
    validateOn: vue.PropType<ValidationProps["validateOn"]>;
    validationValue: null;
    density: {
        type: vue.PropType<Density>;
        default: string;
        validator: (v: any) => boolean;
    };
    id: StringConstructor;
    appendIcon: vue.PropType<IconValue>;
    prependIcon: {
        type: vue.PropType<IconValue>;
        default: NonNullable<IconValue>;
    };
    hideDetails: vue.PropType<boolean | "auto">;
    hideSpinButtons: BooleanConstructor;
    hint: StringConstructor;
    persistentHint: BooleanConstructor;
    messages: {
        type: vue.PropType<string | readonly string[]>;
        default: () => never[];
    };
    direction: {
        type: vue.PropType<"horizontal" | "vertical">;
        default: string;
        validator: (v: any) => boolean;
    };
    'onClick:prepend': vue.PropType<(args_0: MouseEvent) => void>;
    'onClick:append': vue.PropType<(args_0: MouseEvent) => void>;
    autofocus: BooleanConstructor;
    counter: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    counterValue: vue.PropType<number | ((value: any) => number)>;
    prefix: StringConstructor;
    placeholder: {
        type: vue.PropType<string>;
        default: string;
    };
    persistentPlaceholder: BooleanConstructor;
    persistentCounter: BooleanConstructor;
    suffix: StringConstructor;
    role: StringConstructor;
    type: {
        type: StringConstructor;
        default: string;
    };
    modelModifiers: vue.PropType<Record<string, boolean>>;
    cancelText: {
        type: StringConstructor;
        default: string;
    };
    okText: {
        type: StringConstructor;
        default: string;
    };
    hideActions: BooleanConstructor;
}>>;
type VDateInput = InstanceType<typeof VDateInput>;

export { VDateInput };
