import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, VNode, PropType } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type VSkeletonLoaderType = keyof typeof rootTypes;
declare const rootTypes: {
    readonly actions: "button@2";
    readonly article: "heading, paragraph";
    readonly avatar: "avatar";
    readonly button: "button";
    readonly card: "image, heading";
    readonly 'card-avatar': "image, list-item-avatar";
    readonly chip: "chip";
    readonly 'date-picker': "list-item, heading, divider, date-picker-options, date-picker-days, actions";
    readonly 'date-picker-options': "text, avatar@2";
    readonly 'date-picker-days': "avatar@28";
    readonly divider: "divider";
    readonly heading: "heading";
    readonly image: "image";
    readonly 'list-item': "text";
    readonly 'list-item-avatar': "avatar, text";
    readonly 'list-item-two-line': "sentences";
    readonly 'list-item-avatar-two-line': "avatar, sentences";
    readonly 'list-item-three-line': "paragraph";
    readonly 'list-item-avatar-three-line': "avatar, paragraph";
    readonly ossein: "ossein";
    readonly paragraph: "text@3";
    readonly sentences: "text@2";
    readonly subtitle: "text";
    readonly table: "table-heading, table-thead, table-tbody, table-tfoot";
    readonly 'table-heading': "chip, text";
    readonly 'table-thead': "heading@6";
    readonly 'table-tbody': "table-row-divider@6";
    readonly 'table-row-divider': "table-row, divider";
    readonly 'table-row': "text@6";
    readonly 'table-tfoot': "text@2, avatar@2";
    readonly text: "text";
};
declare const VSkeletonLoader: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        type: "button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot" | readonly ("button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot")[];
        loading: boolean;
        loadingText: string;
        boilerplate: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        theme?: string | undefined;
        elevation?: string | number | undefined;
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
        type: "button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot" | readonly ("button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot")[];
        loading: boolean;
        loadingText: string;
        boilerplate: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        theme?: string | undefined;
        elevation?: string | number | undefined;
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
        type: "button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot" | readonly ("button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot")[];
        loading: boolean;
        loadingText: string;
        boilerplate: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        type: "button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot" | readonly ("button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot")[];
        loading: boolean;
        loadingText: string;
        boilerplate: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        theme?: string | undefined;
        elevation?: string | number | undefined;
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
        type: "button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot" | readonly ("button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot")[];
        loading: boolean;
        loadingText: string;
        boilerplate: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    type: "button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot" | readonly ("button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot")[];
    loading: boolean;
    loadingText: string;
    boilerplate: boolean;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    theme?: string | undefined;
    elevation?: string | number | undefined;
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
    type: "button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot" | readonly ("button" | "article" | "table" | "image" | "text" | (string & {}) | "table-row" | "list-item" | "sentences" | "heading" | "divider" | "subtitle" | "chip" | "avatar" | "actions" | "paragraph" | "ossein" | "card" | "card-avatar" | "date-picker" | "date-picker-options" | "date-picker-days" | "list-item-avatar" | "list-item-two-line" | "list-item-avatar-two-line" | "list-item-three-line" | "list-item-avatar-three-line" | "table-heading" | "table-thead" | "table-tbody" | "table-row-divider" | "table-tfoot")[];
    loading: boolean;
    loadingText: string;
    boilerplate: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    theme: StringConstructor;
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
    boilerplate: BooleanConstructor;
    color: StringConstructor;
    loading: BooleanConstructor;
    loadingText: {
        type: StringConstructor;
        default: string;
    };
    type: {
        type: PropType<VSkeletonLoaderType | (string & {}) | ReadonlyArray<VSkeletonLoaderType | (string & {})>>;
        default: string;
    };
}, vue.ExtractPropTypes<{
    theme: StringConstructor;
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
    boilerplate: BooleanConstructor;
    color: StringConstructor;
    loading: BooleanConstructor;
    loadingText: {
        type: StringConstructor;
        default: string;
    };
    type: {
        type: PropType<VSkeletonLoaderType | (string & {}) | ReadonlyArray<VSkeletonLoaderType | (string & {})>>;
        default: string;
    };
}>>;
type VSkeletonLoader = InstanceType<typeof VSkeletonLoader>;

export { VSkeletonLoader };
