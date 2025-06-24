import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, Prop, PropType, ComponentPublicInstance, FunctionalComponent } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

type Density = null | 'default' | 'comfortable' | 'compact';

type TimelineDirection = 'vertical' | 'horizontal';
type TimelineSide = 'start' | 'end' | undefined;
type TimelineAlign = 'center' | 'start';
type TimelineTruncateLine = 'start' | 'end' | 'both' | undefined;
declare const VTimeline: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        justify: string;
        density: Density;
        fillDot: boolean;
        lineInset: string | number;
        lineThickness: string | number;
    } & {
        direction?: TimelineDirection | undefined;
        class?: any;
        theme?: string | undefined;
        align?: TimelineAlign | undefined;
        side?: TimelineSide;
        dotColor?: string | undefined;
        iconColor?: string | undefined;
        lineColor?: string | undefined;
        hideOpposite?: boolean | undefined;
        truncateLine?: TimelineTruncateLine;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        justify: string;
        density: Density;
        fillDot: boolean;
        lineInset: string | number;
        lineThickness: string | number;
    } & {
        direction?: TimelineDirection | undefined;
        class?: any;
        theme?: string | undefined;
        align?: TimelineAlign | undefined;
        side?: TimelineSide;
        dotColor?: string | undefined;
        iconColor?: string | undefined;
        lineColor?: string | undefined;
        hideOpposite?: boolean | undefined;
        truncateLine?: TimelineTruncateLine;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        justify: string;
        density: Density;
        fillDot: boolean;
        hideOpposite: boolean;
        lineInset: string | number;
        lineThickness: string | number;
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
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        justify: string;
        density: Density;
        fillDot: boolean;
        lineInset: string | number;
        lineThickness: string | number;
    } & {
        direction?: TimelineDirection | undefined;
        class?: any;
        theme?: string | undefined;
        align?: TimelineAlign | undefined;
        side?: TimelineSide;
        dotColor?: string | undefined;
        iconColor?: string | undefined;
        lineColor?: string | undefined;
        hideOpposite?: boolean | undefined;
        truncateLine?: TimelineTruncateLine;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        justify: string;
        density: Density;
        fillDot: boolean;
        hideOpposite: boolean;
        lineInset: string | number;
        lineThickness: string | number;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    size: string | number;
    tag: string;
    justify: string;
    density: Density;
    fillDot: boolean;
    lineInset: string | number;
    lineThickness: string | number;
} & {
    direction?: TimelineDirection | undefined;
    class?: any;
    theme?: string | undefined;
    align?: TimelineAlign | undefined;
    side?: TimelineSide;
    dotColor?: string | undefined;
    iconColor?: string | undefined;
    lineColor?: string | undefined;
    hideOpposite?: boolean | undefined;
    truncateLine?: TimelineTruncateLine;
} & {
    $children?: vue.VNodeChild | {
        default?: (() => vue.VNodeChild) | undefined;
    } | (() => vue.VNodeChild);
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    style: vue.StyleValue;
    size: string | number;
    tag: string;
    justify: string;
    density: Density;
    fillDot: boolean;
    hideOpposite: boolean;
    lineInset: string | number;
    lineThickness: string | number;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
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
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
    dotColor: StringConstructor;
    fillDot: BooleanConstructor;
    iconColor: StringConstructor;
    hideOpposite: {
        type: BooleanConstructor;
        default: undefined;
    };
    lineInset: {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    align: Prop<TimelineAlign>;
    direction: Prop<TimelineDirection>;
    justify: {
        type: StringConstructor;
        default: string;
        validator: (v: any) => boolean;
    };
    side: Prop<TimelineSide>;
    lineThickness: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    lineColor: StringConstructor;
    truncateLine: Prop<TimelineTruncateLine>;
}, vue.ExtractPropTypes<{
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
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
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
    dotColor: StringConstructor;
    fillDot: BooleanConstructor;
    iconColor: StringConstructor;
    hideOpposite: {
        type: BooleanConstructor;
        default: undefined;
    };
    lineInset: {
        type: vue.PropType<string | number>;
        default: NonNullable<string | number>;
    };
    align: Prop<TimelineAlign>;
    direction: Prop<TimelineDirection>;
    justify: {
        type: StringConstructor;
        default: string;
        validator: (v: any) => boolean;
    };
    side: Prop<TimelineSide>;
    lineThickness: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    lineColor: StringConstructor;
    truncateLine: Prop<TimelineTruncateLine>;
}>>;
type VTimeline = InstanceType<typeof VTimeline>;

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

declare const VTimelineItem: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        tile: boolean;
        fillDot: boolean;
        hideDot: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
        icon?: IconValue | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        density?: "default" | "compact" | undefined;
        dotColor?: string | undefined;
        iconColor?: string | undefined;
        hideOpposite?: boolean | undefined;
        lineInset?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            icon?: (() => vue.VNodeChild) | undefined;
            opposite?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            icon?: false | (() => vue.VNodeChild) | undefined;
            opposite?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:icon"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:opposite"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        tile: boolean;
        fillDot: boolean;
        hideDot: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
        icon?: IconValue | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        density?: "default" | "compact" | undefined;
        dotColor?: string | undefined;
        iconColor?: string | undefined;
        hideOpposite?: boolean | undefined;
        lineInset?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            icon?: (() => vue.VNodeChild) | undefined;
            opposite?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            icon?: false | (() => vue.VNodeChild) | undefined;
            opposite?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:icon"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:opposite"?: false | (() => vue.VNodeChild) | undefined;
    }, {
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        fillDot: boolean;
        hideDot: boolean;
        hideOpposite: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        icon: () => vue.VNode[];
        opposite: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        tile: boolean;
        fillDot: boolean;
        hideDot: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
        icon?: IconValue | undefined;
        elevation?: string | number | undefined;
        rounded?: string | number | boolean | undefined;
        density?: "default" | "compact" | undefined;
        dotColor?: string | undefined;
        iconColor?: string | undefined;
        hideOpposite?: boolean | undefined;
        lineInset?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            icon?: (() => vue.VNodeChild) | undefined;
            opposite?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            icon?: false | (() => vue.VNodeChild) | undefined;
            opposite?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:icon"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:opposite"?: false | (() => vue.VNodeChild) | undefined;
    }, {}, {}, {}, {}, {
        style: vue.StyleValue;
        size: string | number;
        tag: string;
        rounded: string | number | boolean;
        tile: boolean;
        fillDot: boolean;
        hideDot: boolean;
        hideOpposite: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    size: string | number;
    tag: string;
    tile: boolean;
    fillDot: boolean;
    hideDot: boolean;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    class?: any;
    icon?: IconValue | undefined;
    elevation?: string | number | undefined;
    rounded?: string | number | boolean | undefined;
    density?: "default" | "compact" | undefined;
    dotColor?: string | undefined;
    iconColor?: string | undefined;
    hideOpposite?: boolean | undefined;
    lineInset?: string | number | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        icon?: (() => vue.VNodeChild) | undefined;
        opposite?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        icon?: false | (() => vue.VNodeChild) | undefined;
        opposite?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:icon"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:opposite"?: false | (() => vue.VNodeChild) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    style: vue.StyleValue;
    size: string | number;
    tag: string;
    rounded: string | number | boolean;
    tile: boolean;
    fillDot: boolean;
    hideDot: boolean;
    hideOpposite: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    icon: () => vue.VNode[];
    opposite: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
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
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    density: PropType<"default" | "compact">;
    dotColor: StringConstructor;
    fillDot: BooleanConstructor;
    hideDot: BooleanConstructor;
    hideOpposite: {
        type: BooleanConstructor;
        default: undefined;
    };
    icon: PropType<IconValue>;
    iconColor: StringConstructor;
    lineInset: (StringConstructor | NumberConstructor)[];
}, vue.ExtractPropTypes<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    size: {
        type: (StringConstructor | NumberConstructor)[];
        default: string;
    };
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
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    density: PropType<"default" | "compact">;
    dotColor: StringConstructor;
    fillDot: BooleanConstructor;
    hideDot: BooleanConstructor;
    hideOpposite: {
        type: BooleanConstructor;
        default: undefined;
    };
    icon: PropType<IconValue>;
    iconColor: StringConstructor;
    lineInset: (StringConstructor | NumberConstructor)[];
}>>;
type VTimelineItem = InstanceType<typeof VTimelineItem>;

export { VTimeline, VTimelineItem };
