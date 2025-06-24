import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

declare const VPullToRefresh: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        disabled: boolean;
        pullDownThreshold: number;
    } & {} & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            pullDownPanel?: ((arg: {
                canRefresh: boolean;
                goingUp: boolean;
                refreshing: boolean;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            pullDownPanel?: false | ((arg: {
                canRefresh: boolean;
                goingUp: boolean;
                refreshing: boolean;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:pullDownPanel"?: false | ((arg: {
            canRefresh: boolean;
            goingUp: boolean;
            refreshing: boolean;
        }) => vue.VNodeChild) | undefined;
    } & {
        onLoad?: ((options: {
            done: () => void;
        }) => any) | undefined;
    }, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        load: (options: {
            done: () => void;
        }) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        disabled: boolean;
        pullDownThreshold: number;
    } & {} & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            pullDownPanel?: ((arg: {
                canRefresh: boolean;
                goingUp: boolean;
                refreshing: boolean;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            pullDownPanel?: false | ((arg: {
                canRefresh: boolean;
                goingUp: boolean;
                refreshing: boolean;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:pullDownPanel"?: false | ((arg: {
            canRefresh: boolean;
            goingUp: boolean;
            refreshing: boolean;
        }) => vue.VNodeChild) | undefined;
    } & {
        onLoad?: ((options: {
            done: () => void;
        }) => any) | undefined;
    }, {
        disabled: boolean;
        pullDownThreshold: number;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        pullDownPanel: (arg: {
            canRefresh: boolean;
            goingUp: boolean;
            refreshing: boolean;
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        disabled: boolean;
        pullDownThreshold: number;
    } & {} & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            pullDownPanel?: ((arg: {
                canRefresh: boolean;
                goingUp: boolean;
                refreshing: boolean;
            }) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            pullDownPanel?: false | ((arg: {
                canRefresh: boolean;
                goingUp: boolean;
                refreshing: boolean;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:pullDownPanel"?: false | ((arg: {
            canRefresh: boolean;
            goingUp: boolean;
            refreshing: boolean;
        }) => vue.VNodeChild) | undefined;
    } & {
        onLoad?: ((options: {
            done: () => void;
        }) => any) | undefined;
    }, {}, {}, {}, {}, {
        disabled: boolean;
        pullDownThreshold: number;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    disabled: boolean;
    pullDownThreshold: number;
} & {} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        pullDownPanel?: ((arg: {
            canRefresh: boolean;
            goingUp: boolean;
            refreshing: boolean;
        }) => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        pullDownPanel?: false | ((arg: {
            canRefresh: boolean;
            goingUp: boolean;
            refreshing: boolean;
        }) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:pullDownPanel"?: false | ((arg: {
        canRefresh: boolean;
        goingUp: boolean;
        refreshing: boolean;
    }) => vue.VNodeChild) | undefined;
} & {
    onLoad?: ((options: {
        done: () => void;
    }) => any) | undefined;
}, void, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    load: (options: {
        done: () => void;
    }) => true;
}, string, {
    disabled: boolean;
    pullDownThreshold: number;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    pullDownPanel: (arg: {
        canRefresh: boolean;
        goingUp: boolean;
        refreshing: boolean;
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    pullDownThreshold: {
        type: NumberConstructor;
        default: number;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    pullDownThreshold: {
        type: NumberConstructor;
        default: number;
    };
}>>;
type VPullToRefresh = InstanceType<typeof VPullToRefresh>;

export { VPullToRefresh };
