import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type InfiniteScrollSide = 'start' | 'end' | 'both';
type InfiniteScrollStatus = 'ok' | 'empty' | 'loading' | 'error';
type InfiniteScrollSlot = {
    side: InfiniteScrollSide;
    props: Record<string, any>;
};
declare const VInfiniteScroll: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        direction: "horizontal" | "vertical";
        tag: string;
        mode: "manual" | "intersect";
        side: InfiniteScrollSide;
        loadMoreText: string;
        emptyText: string;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        margin?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            loading?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            error?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            empty?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            'load-more'?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            loading?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            error?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            empty?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            'load-more'?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:loading"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        "v-slot:error"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        "v-slot:empty"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        "v-slot:load-more"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
    } & {
        onLoad?: ((options: {
            side: InfiniteScrollSide;
            done: (status: InfiniteScrollStatus) => void;
        }) => any) | undefined;
    }, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        load: (options: {
            side: InfiniteScrollSide;
            done: (status: InfiniteScrollStatus) => void;
        }) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        direction: "horizontal" | "vertical";
        tag: string;
        mode: "manual" | "intersect";
        side: InfiniteScrollSide;
        loadMoreText: string;
        emptyText: string;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        margin?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            loading?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            error?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            empty?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            'load-more'?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            loading?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            error?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            empty?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            'load-more'?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:loading"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        "v-slot:error"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        "v-slot:empty"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        "v-slot:load-more"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
    } & {
        onLoad?: ((options: {
            side: InfiniteScrollSide;
            done: (status: InfiniteScrollStatus) => void;
        }) => any) | undefined;
    }, {
        direction: "horizontal" | "vertical";
        tag: string;
        mode: "manual" | "intersect";
        side: InfiniteScrollSide;
        loadMoreText: string;
        emptyText: string;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        loading: (arg: InfiniteScrollSlot) => vue.VNode[];
        error: (arg: InfiniteScrollSlot) => vue.VNode[];
        empty: (arg: InfiniteScrollSlot) => vue.VNode[];
        'load-more': (arg: InfiniteScrollSlot) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        direction: "horizontal" | "vertical";
        tag: string;
        mode: "manual" | "intersect";
        side: InfiniteScrollSide;
        loadMoreText: string;
        emptyText: string;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        margin?: string | number | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            loading?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            error?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            empty?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            'load-more'?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            loading?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            error?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            empty?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
            'load-more'?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:loading"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        "v-slot:error"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        "v-slot:empty"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        "v-slot:load-more"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
    } & {
        onLoad?: ((options: {
            side: InfiniteScrollSide;
            done: (status: InfiniteScrollStatus) => void;
        }) => any) | undefined;
    }, {}, {}, {}, {}, {
        direction: "horizontal" | "vertical";
        tag: string;
        mode: "manual" | "intersect";
        side: InfiniteScrollSide;
        loadMoreText: string;
        emptyText: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    direction: "horizontal" | "vertical";
    tag: string;
    mode: "manual" | "intersect";
    side: InfiniteScrollSide;
    loadMoreText: string;
    emptyText: string;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    color?: string | undefined;
    margin?: string | number | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        loading?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        error?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        empty?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        'load-more'?: ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        loading?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        error?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        empty?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
        'load-more'?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:loading"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
    "v-slot:error"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
    "v-slot:empty"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
    "v-slot:load-more"?: false | ((arg: InfiniteScrollSlot) => vue.VNodeChild) | undefined;
} & {
    onLoad?: ((options: {
        side: InfiniteScrollSide;
        done: (status: InfiniteScrollStatus) => void;
    }) => any) | undefined;
}, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    load: (options: {
        side: InfiniteScrollSide;
        done: (status: InfiniteScrollStatus) => void;
    }) => true;
}, string, {
    direction: "horizontal" | "vertical";
    tag: string;
    mode: "manual" | "intersect";
    side: InfiniteScrollSide;
    loadMoreText: string;
    emptyText: string;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    loading: (arg: InfiniteScrollSlot) => vue.VNode[];
    error: (arg: InfiniteScrollSlot) => vue.VNode[];
    empty: (arg: InfiniteScrollSlot) => vue.VNode[];
    'load-more': (arg: InfiniteScrollSlot) => vue.VNode[];
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
    color: StringConstructor;
    direction: {
        type: PropType<"vertical" | "horizontal">;
        default: string;
        validator: (v: any) => boolean;
    };
    side: {
        type: PropType<InfiniteScrollSide>;
        default: string;
        validator: (v: any) => boolean;
    };
    mode: {
        type: PropType<"intersect" | "manual">;
        default: string;
        validator: (v: any) => boolean;
    };
    margin: (StringConstructor | NumberConstructor)[];
    loadMoreText: {
        type: StringConstructor;
        default: string;
    };
    emptyText: {
        type: StringConstructor;
        default: string;
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
    color: StringConstructor;
    direction: {
        type: PropType<"vertical" | "horizontal">;
        default: string;
        validator: (v: any) => boolean;
    };
    side: {
        type: PropType<InfiniteScrollSide>;
        default: string;
        validator: (v: any) => boolean;
    };
    mode: {
        type: PropType<"intersect" | "manual">;
        default: string;
        validator: (v: any) => boolean;
    };
    margin: (StringConstructor | NumberConstructor)[];
    loadMoreText: {
        type: StringConstructor;
        default: string;
    };
    emptyText: {
        type: StringConstructor;
        default: string;
    };
}>>;
type VInfiniteScroll = InstanceType<typeof VInfiniteScroll>;

export { VInfiniteScroll };
