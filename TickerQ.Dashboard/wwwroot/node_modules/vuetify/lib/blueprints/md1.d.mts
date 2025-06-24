import { Ref, ComponentPublicInstance, PropType, FunctionalComponent } from 'vue';

interface LocaleMessages {
    [key: string]: LocaleMessages | string;
}
interface LocaleOptions {
    messages?: LocaleMessages;
    locale?: string;
    fallback?: string;
    adapter?: LocaleInstance;
}
interface LocaleInstance {
    name: string;
    messages: Ref<LocaleMessages>;
    current: Ref<string>;
    fallback: Ref<string>;
    t: (key: string, ...params: unknown[]) => string;
    n: (value: number) => string;
    provide: (props: LocaleOptions) => LocaleInstance;
}
interface RtlOptions {
    rtl?: Record<string, boolean>;
}

interface InternalGoToOptions {
    container: ComponentPublicInstance | HTMLElement | string;
    duration: number;
    layout: boolean;
    offset: number;
    easing: string | ((t: number) => number);
    patterns: Record<string, (t: number) => number>;
}
type GoToOptions = Partial<InternalGoToOptions>;

interface DateAdapter<T = unknown> {
    date(value?: any): T | null;
    format(date: T, formatString: string): string;
    toJsDate(value: T): Date;
    parseISO(date: string): T;
    toISO(date: T): string;
    startOfDay(date: T): T;
    endOfDay(date: T): T;
    startOfWeek(date: T, firstDayOfWeek?: number | string): T;
    endOfWeek(date: T): T;
    startOfMonth(date: T): T;
    endOfMonth(date: T): T;
    startOfYear(date: T): T;
    endOfYear(date: T): T;
    isAfter(date: T, comparing: T): boolean;
    isAfterDay(value: T, comparing: T): boolean;
    isSameDay(date: T, comparing: T): boolean;
    isSameMonth(date: T, comparing: T): boolean;
    isSameYear(value: T, comparing: T): boolean;
    isBefore(date: T, comparing: T): boolean;
    isEqual(date: T, comparing: T): boolean;
    isValid(date: any): boolean;
    isWithinRange(date: T, range: [T, T]): boolean;
    addMinutes(date: T, amount: number): T;
    addHours(date: T, amount: number): T;
    addDays(date: T, amount: number): T;
    addWeeks(date: T, amount: number): T;
    addMonths(date: T, amount: number): T;
    getYear(date: T): number;
    setYear(date: T, year: number): T;
    getDiff(date: T, comparing: T | string, unit?: string): number;
    getWeekArray(date: T, firstDayOfWeek?: number | string): T[][];
    getWeekdays(firstDayOfWeek?: number | string): string[];
    getMonth(date: T): number;
    setMonth(date: T, month: number): T;
    getDate(date: T): number;
    setDate(date: T, day: number): T;
    getNextMonth(date: T): T;
    getPreviousMonth(date: T): T;
    getHours(date: T): number;
    setHours(date: T, hours: number): T;
    getMinutes(date: T): number;
    setMinutes(date: T, minutes: number): T;
}

interface DateInstance extends DateModule.InternalAdapter {
    locale?: any;
}
/** Supports module augmentation to specify date adapter types */
declare namespace DateModule {
    interface Adapter {
    }
    export type InternalAdapter = {} extends Adapter ? DateAdapter : Adapter;

}
type InternalDateOptions = {
    adapter: (new (options: {
        locale: any;
        formats?: any;
    }) => DateInstance) | DateInstance;
    formats?: Record<string, any>;
    locale: Record<string, any>;
};
type DateOptions = Partial<InternalDateOptions>;

type DeepPartial<T> = T extends object ? {
    [P in keyof T]?: DeepPartial<T[P]>;
} : T;
type ThemeOptions = false | {
    cspNonce?: string;
    defaultTheme?: string;
    variations?: false | VariationsOptions;
    themes?: Record<string, ThemeDefinition>;
};
type ThemeDefinition = DeepPartial<InternalThemeDefinition>;
interface VariationsOptions {
    colors: string[];
    lighten: number;
    darken: number;
}
interface InternalThemeDefinition {
    dark: boolean;
    colors: Colors;
    variables: Record<string, string | number>;
}
interface Colors extends BaseColors, OnColors {
    [key: string]: string;
}
interface BaseColors {
    background: string;
    surface: string;
    primary: string;
    secondary: string;
    success: string;
    warning: string;
    error: string;
    info: string;
}
interface OnColors {
    'on-background': string;
    'on-surface': string;
    'on-primary': string;
    'on-secondary': string;
    'on-success': string;
    'on-warning': string;
    'on-error': string;
    'on-info': string;
}

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;
interface IconAliases {
    [name: string]: IconValue;
    complete: IconValue;
    cancel: IconValue;
    close: IconValue;
    delete: IconValue;
    clear: IconValue;
    success: IconValue;
    info: IconValue;
    warning: IconValue;
    error: IconValue;
    prev: IconValue;
    next: IconValue;
    checkboxOn: IconValue;
    checkboxOff: IconValue;
    checkboxIndeterminate: IconValue;
    delimiter: IconValue;
    sortAsc: IconValue;
    sortDesc: IconValue;
    expand: IconValue;
    menu: IconValue;
    subgroup: IconValue;
    dropdown: IconValue;
    radioOn: IconValue;
    radioOff: IconValue;
    edit: IconValue;
    ratingEmpty: IconValue;
    ratingFull: IconValue;
    ratingHalf: IconValue;
    loading: IconValue;
    first: IconValue;
    last: IconValue;
    unfold: IconValue;
    file: IconValue;
    plus: IconValue;
    minus: IconValue;
    calendar: IconValue;
}
interface IconProps {
    tag: string;
    icon?: IconValue;
    disabled?: Boolean;
}
type IconComponent = JSXComponent<IconProps>;
interface IconSet {
    component: IconComponent;
}
type InternalIconOptions = {
    defaultSet: string;
    aliases: Partial<IconAliases>;
    sets: Record<string, IconSet>;
};
type IconOptions = Partial<InternalIconOptions>;

declare const breakpoints: readonly ["sm", "md", "lg", "xl", "xxl"];
type Breakpoint = typeof breakpoints[number];
type DisplayBreakpoint = 'xs' | Breakpoint;
type DisplayThresholds = {
    [key in DisplayBreakpoint]: number;
};
interface DisplayOptions {
    mobileBreakpoint?: number | DisplayBreakpoint;
    thresholds?: Partial<DisplayThresholds>;
}
type SSROptions = boolean | {
    clientWidth: number;
    clientHeight?: number;
};

type DefaultsInstance = undefined | {
    [key: string]: undefined | Record<string, unknown>;
    global?: Record<string, unknown>;
};
type DefaultsOptions = Partial<DefaultsInstance>;

interface VuetifyOptions {
    aliases?: Record<string, any>;
    blueprint?: Blueprint;
    components?: Record<string, any>;
    date?: DateOptions;
    directives?: Record<string, any>;
    defaults?: DefaultsOptions;
    display?: DisplayOptions;
    goTo?: GoToOptions;
    theme?: ThemeOptions;
    icons?: IconOptions;
    locale?: LocaleOptions & RtlOptions;
    ssr?: SSROptions;
}
interface Blueprint extends Omit<VuetifyOptions, 'blueprint'> {
}

declare const md1: Blueprint;

export { md1 };
