import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType, Prop } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

declare const VContainer: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        tag: string;
        fluid: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
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
        tag: string;
        fluid: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
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
        tag: string;
        fluid: boolean;
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
        tag: string;
        fluid: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        class?: any;
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
        tag: string;
        fluid: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    tag: string;
    fluid: boolean;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    class?: any;
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
    tag: string;
    fluid: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    tag: {
        type: StringConstructor;
        default: string;
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
    fluid: {
        type: BooleanConstructor;
        default: boolean;
    };
}, vue.ExtractPropTypes<{
    tag: {
        type: StringConstructor;
        default: string;
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
    fluid: {
        type: BooleanConstructor;
        default: boolean;
    };
}>>;
type VContainer = InstanceType<typeof VContainer>;

declare const ALIGN_SELF_VALUES: readonly ["auto", "start", "end", "center", "baseline", "stretch"];
declare const VCol: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        offset: string | number;
        alignSelf: "auto" | "center" | "end" | "start" | "stretch" | "baseline";
        order: string | number;
        style: vue.StyleValue;
        tag: string;
        cols: string | number | boolean;
    } & {
        class?: any;
        sm?: string | number | boolean | undefined;
        md?: string | number | boolean | undefined;
        lg?: string | number | boolean | undefined;
        xl?: string | number | boolean | undefined;
        xxl?: string | number | boolean | undefined;
        offsetSm?: string | number | undefined;
        offsetMd?: string | number | undefined;
        offsetLg?: string | number | undefined;
        offsetXl?: string | number | undefined;
        offsetXxl?: string | number | undefined;
        orderSm?: string | number | undefined;
        orderMd?: string | number | undefined;
        orderLg?: string | number | undefined;
        orderXl?: string | number | undefined;
        orderXxl?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        offset: string | number;
        alignSelf: "auto" | "center" | "end" | "start" | "stretch" | "baseline";
        order: string | number;
        style: vue.StyleValue;
        tag: string;
        cols: string | number | boolean;
    } & {
        class?: any;
        sm?: string | number | boolean | undefined;
        md?: string | number | boolean | undefined;
        lg?: string | number | boolean | undefined;
        xl?: string | number | boolean | undefined;
        xxl?: string | number | boolean | undefined;
        offsetSm?: string | number | undefined;
        offsetMd?: string | number | undefined;
        offsetLg?: string | number | undefined;
        offsetXl?: string | number | undefined;
        offsetXxl?: string | number | undefined;
        orderSm?: string | number | undefined;
        orderMd?: string | number | undefined;
        orderLg?: string | number | undefined;
        orderXl?: string | number | undefined;
        orderXxl?: string | number | undefined;
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
        offset: string | number;
        alignSelf: "auto" | "center" | "end" | "start" | "stretch" | "baseline";
        order: string | number;
        style: vue.StyleValue;
        tag: string;
        cols: string | number | boolean;
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
        offset: string | number;
        alignSelf: "auto" | "center" | "end" | "start" | "stretch" | "baseline";
        order: string | number;
        style: vue.StyleValue;
        tag: string;
        cols: string | number | boolean;
    } & {
        class?: any;
        sm?: string | number | boolean | undefined;
        md?: string | number | boolean | undefined;
        lg?: string | number | boolean | undefined;
        xl?: string | number | boolean | undefined;
        xxl?: string | number | boolean | undefined;
        offsetSm?: string | number | undefined;
        offsetMd?: string | number | undefined;
        offsetLg?: string | number | undefined;
        offsetXl?: string | number | undefined;
        offsetXxl?: string | number | undefined;
        orderSm?: string | number | undefined;
        orderMd?: string | number | undefined;
        orderLg?: string | number | undefined;
        orderXl?: string | number | undefined;
        orderXxl?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>, {}, {}, {}, {
        offset: string | number;
        alignSelf: "auto" | "center" | "end" | "start" | "stretch" | "baseline";
        order: string | number;
        style: vue.StyleValue;
        tag: string;
        cols: string | number | boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    offset: string | number;
    alignSelf: "auto" | "center" | "end" | "start" | "stretch" | "baseline";
    order: string | number;
    style: vue.StyleValue;
    tag: string;
    cols: string | number | boolean;
} & {
    class?: any;
    sm?: string | number | boolean | undefined;
    md?: string | number | boolean | undefined;
    lg?: string | number | boolean | undefined;
    xl?: string | number | boolean | undefined;
    xxl?: string | number | boolean | undefined;
    offsetSm?: string | number | undefined;
    offsetMd?: string | number | undefined;
    offsetLg?: string | number | undefined;
    offsetXl?: string | number | undefined;
    offsetXxl?: string | number | undefined;
    orderSm?: string | number | undefined;
    orderMd?: string | number | undefined;
    orderLg?: string | number | undefined;
    orderXl?: string | number | undefined;
    orderXxl?: string | number | undefined;
} & {
    $children?: vue.VNodeChild | {
        default?: (() => vue.VNodeChild) | undefined;
    } | (() => vue.VNodeChild);
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
}, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
    [key: string]: any;
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    offset: string | number;
    alignSelf: "auto" | "center" | "end" | "start" | "stretch" | "baseline";
    order: string | number;
    style: vue.StyleValue;
    tag: string;
    cols: string | number | boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    alignSelf: {
        type: PropType<(typeof ALIGN_SELF_VALUES)[number]>;
        default: null;
        validator: (str: any) => boolean;
    };
    orderSm: Prop<string | number, null>;
    orderMd: Prop<string | number, null>;
    orderLg: Prop<string | number, null>;
    orderXl: Prop<string | number, null>;
    orderXxl: Prop<string | number, null>;
    order: {
        type: (StringConstructor | NumberConstructor)[];
        default: null;
    };
    offsetSm: Prop<string | number, null>;
    offsetMd: Prop<string | number, null>;
    offsetLg: Prop<string | number, null>;
    offsetXl: Prop<string | number, null>;
    offsetXxl: Prop<string | number, null>;
    offset: {
        type: (StringConstructor | NumberConstructor)[];
        default: null;
    };
    sm: Prop<string | number | boolean, false>;
    md: Prop<string | number | boolean, false>;
    lg: Prop<string | number | boolean, false>;
    xl: Prop<string | number | boolean, false>;
    xxl: Prop<string | number | boolean, false>;
    cols: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: boolean;
    };
}, vue.ExtractPropTypes<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    alignSelf: {
        type: PropType<(typeof ALIGN_SELF_VALUES)[number]>;
        default: null;
        validator: (str: any) => boolean;
    };
    orderSm: Prop<string | number, null>;
    orderMd: Prop<string | number, null>;
    orderLg: Prop<string | number, null>;
    orderXl: Prop<string | number, null>;
    orderXxl: Prop<string | number, null>;
    order: {
        type: (StringConstructor | NumberConstructor)[];
        default: null;
    };
    offsetSm: Prop<string | number, null>;
    offsetMd: Prop<string | number, null>;
    offsetLg: Prop<string | number, null>;
    offsetXl: Prop<string | number, null>;
    offsetXxl: Prop<string | number, null>;
    offset: {
        type: (StringConstructor | NumberConstructor)[];
        default: null;
    };
    sm: Prop<string | number | boolean, false>;
    md: Prop<string | number | boolean, false>;
    lg: Prop<string | number | boolean, false>;
    xl: Prop<string | number | boolean, false>;
    xxl: Prop<string | number | boolean, false>;
    cols: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: boolean;
    };
}>>;
type VCol = InstanceType<typeof VCol>;

declare const ALIGN_VALUES: readonly ["start", "end", "center", "baseline", "stretch"];
declare const ALIGN_CONTENT_VALUES: readonly ["start", "end", "center", "space-between", "space-around", "space-evenly", "stretch"];
declare const VRow: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        alignContent: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
        style: vue.StyleValue;
        tag: string;
        dense: boolean;
        justify: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
        align: "center" | "end" | "start" | "stretch" | "baseline";
        noGutters: boolean;
    } & {
        class?: any;
        alignSm?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignMd?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignLg?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignXl?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignXxl?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        justifySm?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyMd?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyLg?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyXl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyXxl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        alignContentSm?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentMd?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentLg?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentXl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentXxl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        alignContent: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
        style: vue.StyleValue;
        tag: string;
        dense: boolean;
        justify: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
        align: "center" | "end" | "start" | "stretch" | "baseline";
        noGutters: boolean;
    } & {
        class?: any;
        alignSm?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignMd?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignLg?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignXl?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignXxl?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        justifySm?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyMd?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyLg?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyXl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyXxl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        alignContentSm?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentMd?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentLg?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentXl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentXxl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
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
        alignContent: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
        style: vue.StyleValue;
        tag: string;
        dense: boolean;
        justify: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
        align: "center" | "end" | "start" | "stretch" | "baseline";
        noGutters: boolean;
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
        alignContent: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
        style: vue.StyleValue;
        tag: string;
        dense: boolean;
        justify: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
        align: "center" | "end" | "start" | "stretch" | "baseline";
        noGutters: boolean;
    } & {
        class?: any;
        alignSm?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignMd?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignLg?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignXl?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        alignXxl?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
        justifySm?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyMd?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyLg?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyXl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        justifyXxl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
        alignContentSm?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentMd?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentLg?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentXl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
        alignContentXxl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>, {}, {}, {}, {
        alignContent: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
        style: vue.StyleValue;
        tag: string;
        dense: boolean;
        justify: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
        align: "center" | "end" | "start" | "stretch" | "baseline";
        noGutters: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    alignContent: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
    style: vue.StyleValue;
    tag: string;
    dense: boolean;
    justify: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
    align: "center" | "end" | "start" | "stretch" | "baseline";
    noGutters: boolean;
} & {
    class?: any;
    alignSm?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
    alignMd?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
    alignLg?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
    alignXl?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
    alignXxl?: "center" | "end" | "start" | "stretch" | "baseline" | undefined;
    justifySm?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
    justifyMd?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
    justifyLg?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
    justifyXl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
    justifyXxl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | undefined;
    alignContentSm?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
    alignContentMd?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
    alignContentLg?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
    alignContentXl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
    alignContentXxl?: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch" | undefined;
} & {
    $children?: vue.VNodeChild | {
        default?: (() => vue.VNodeChild) | undefined;
    } | (() => vue.VNodeChild);
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
}, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
    [key: string]: any;
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    alignContent: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
    style: vue.StyleValue;
    tag: string;
    dense: boolean;
    justify: "center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch";
    align: "center" | "end" | "start" | "stretch" | "baseline";
    noGutters: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    alignContentSm: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch", null>;
    alignContentMd: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch", null>;
    alignContentLg: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch", null>;
    alignContentXl: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch", null>;
    alignContentXxl: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch", null>;
    alignContent: {
        type: PropType<(typeof ALIGN_CONTENT_VALUES)[number]>;
        default: null;
        validator: (str: any) => boolean;
    };
    justifySm: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly", null>;
    justifyMd: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly", null>;
    justifyLg: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly", null>;
    justifyXl: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly", null>;
    justifyXxl: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly", null>;
    justify: {
        type: PropType<(typeof ALIGN_CONTENT_VALUES)[number]>;
        default: null;
        validator: (str: any) => boolean;
    };
    alignSm: Prop<"center" | "end" | "start" | "stretch" | "baseline", null>;
    alignMd: Prop<"center" | "end" | "start" | "stretch" | "baseline", null>;
    alignLg: Prop<"center" | "end" | "start" | "stretch" | "baseline", null>;
    alignXl: Prop<"center" | "end" | "start" | "stretch" | "baseline", null>;
    alignXxl: Prop<"center" | "end" | "start" | "stretch" | "baseline", null>;
    dense: BooleanConstructor;
    noGutters: BooleanConstructor;
    align: {
        type: PropType<(typeof ALIGN_VALUES)[number]>;
        default: null;
        validator: (str: any) => boolean;
    };
}, vue.ExtractPropTypes<{
    tag: {
        type: StringConstructor;
        default: string;
    };
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    alignContentSm: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch", null>;
    alignContentMd: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch", null>;
    alignContentLg: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch", null>;
    alignContentXl: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch", null>;
    alignContentXxl: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly" | "stretch", null>;
    alignContent: {
        type: PropType<(typeof ALIGN_CONTENT_VALUES)[number]>;
        default: null;
        validator: (str: any) => boolean;
    };
    justifySm: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly", null>;
    justifyMd: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly", null>;
    justifyLg: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly", null>;
    justifyXl: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly", null>;
    justifyXxl: Prop<"center" | "end" | "start" | "space-around" | "space-between" | "space-evenly", null>;
    justify: {
        type: PropType<(typeof ALIGN_CONTENT_VALUES)[number]>;
        default: null;
        validator: (str: any) => boolean;
    };
    alignSm: Prop<"center" | "end" | "start" | "stretch" | "baseline", null>;
    alignMd: Prop<"center" | "end" | "start" | "stretch" | "baseline", null>;
    alignLg: Prop<"center" | "end" | "start" | "stretch" | "baseline", null>;
    alignXl: Prop<"center" | "end" | "start" | "stretch" | "baseline", null>;
    alignXxl: Prop<"center" | "end" | "start" | "stretch" | "baseline", null>;
    dense: BooleanConstructor;
    noGutters: BooleanConstructor;
    align: {
        type: PropType<(typeof ALIGN_VALUES)[number]>;
        default: null;
        validator: (str: any) => boolean;
    };
}>>;
type VRow = InstanceType<typeof VRow>;

declare const VSpacer: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        tag: string;
    } & {
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        tag: string;
    } & {
        class?: any;
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
        tag: string;
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
        tag: string;
    } & {
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: (() => vue.VNodeChild) | undefined;
        } | (() => vue.VNodeChild);
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    }, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
        [key: string]: any;
    }>, {}, {}, {}, {
        style: vue.StyleValue;
        tag: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    tag: string;
} & {
    class?: any;
} & {
    $children?: vue.VNodeChild | {
        default?: (() => vue.VNodeChild) | undefined;
    } | (() => vue.VNodeChild);
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
}, () => vue.VNode<vue.RendererNode, vue.RendererElement, {
    [key: string]: any;
}>, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, Record<string, any>, string, {
    style: vue.StyleValue;
    tag: string;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    tag: {
        type: StringConstructor;
        default: string;
    };
}, vue.ExtractPropTypes<{
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
    tag: {
        type: StringConstructor;
        default: string;
    };
}>>;
type VSpacer = InstanceType<typeof VSpacer>;

export { VCol, VContainer, VRow, VSpacer };
