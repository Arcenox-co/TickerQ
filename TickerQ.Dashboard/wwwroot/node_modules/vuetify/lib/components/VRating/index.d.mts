import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType, ComponentPublicInstance, FunctionalComponent, Prop } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

type Density = null | 'default' | 'comfortable' | 'compact';

type JSXComponent<Props = any> = {
    new (): ComponentPublicInstance<Props>;
} | FunctionalComponent<Props>;
type IconValue = string | (string | [path: string, opacity: number])[] | JSXComponent;
declare const IconValue: PropType<IconValue>;

type VRatingItemSlot = {
    value: number;
    index: number;
    isFilled: boolean;
    isHovered: boolean;
    icon: IconValue;
    color?: string;
    props: Record<string, unknown>;
    rating: number;
};
type VRatingItemLabelSlot = {
    value: number;
    index: number;
    label?: string;
};
declare const VRating: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        length: string | number;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        modelValue: string | number;
        density: Density;
        ripple: boolean;
        clearable: boolean;
        hover: boolean;
        itemAriaLabel: string;
        emptyIcon: IconValue;
        fullIcon: IconValue;
        halfIncrements: boolean;
        itemLabelPosition: string;
    } & {
        name?: string | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        activeColor?: string | undefined;
        itemLabels?: string[] | undefined;
    } & {
        $children?: {} | vue.VNodeChild | {
            item?: ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
            'item-label'?: ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            item?: false | ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
            'item-label'?: false | ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:item"?: false | ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:item-label"?: false | ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: string | number) => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (value: number | string) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        length: string | number;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        modelValue: string | number;
        density: Density;
        ripple: boolean;
        clearable: boolean;
        hover: boolean;
        itemAriaLabel: string;
        emptyIcon: IconValue;
        fullIcon: IconValue;
        halfIncrements: boolean;
        itemLabelPosition: string;
    } & {
        name?: string | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        activeColor?: string | undefined;
        itemLabels?: string[] | undefined;
    } & {
        $children?: {} | vue.VNodeChild | {
            item?: ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
            'item-label'?: ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            item?: false | ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
            'item-label'?: false | ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:item"?: false | ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:item-label"?: false | ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: string | number) => any) | undefined;
    }, {
        length: string | number;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        modelValue: string | number;
        density: Density;
        ripple: boolean;
        clearable: boolean;
        hover: boolean;
        itemAriaLabel: string;
        emptyIcon: IconValue;
        fullIcon: IconValue;
        halfIncrements: boolean;
        itemLabelPosition: string;
    }, true, {}, vue.SlotsType<Partial<{
        item: (arg: VRatingItemSlot) => vue.VNode[];
        'item-label': (arg: VRatingItemLabelSlot) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        length: string | number;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        modelValue: string | number;
        density: Density;
        ripple: boolean;
        clearable: boolean;
        hover: boolean;
        itemAriaLabel: string;
        emptyIcon: IconValue;
        fullIcon: IconValue;
        halfIncrements: boolean;
        itemLabelPosition: string;
    } & {
        name?: string | undefined;
        color?: string | undefined;
        class?: any;
        theme?: string | undefined;
        activeColor?: string | undefined;
        itemLabels?: string[] | undefined;
    } & {
        $children?: {} | vue.VNodeChild | {
            item?: ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
            'item-label'?: ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            item?: false | ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
            'item-label'?: false | ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:item"?: false | ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
        "v-slot:item-label"?: false | ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
    } & {
        "onUpdate:modelValue"?: ((value: string | number) => any) | undefined;
    }, {}, {}, {}, {}, {
        length: string | number;
        style: vue.StyleValue;
        disabled: boolean;
        size: string | number;
        readonly: boolean;
        tag: string;
        modelValue: string | number;
        density: Density;
        ripple: boolean;
        clearable: boolean;
        hover: boolean;
        itemAriaLabel: string;
        emptyIcon: IconValue;
        fullIcon: IconValue;
        halfIncrements: boolean;
        itemLabelPosition: string;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    length: string | number;
    style: vue.StyleValue;
    disabled: boolean;
    size: string | number;
    readonly: boolean;
    tag: string;
    modelValue: string | number;
    density: Density;
    ripple: boolean;
    clearable: boolean;
    hover: boolean;
    itemAriaLabel: string;
    emptyIcon: IconValue;
    fullIcon: IconValue;
    halfIncrements: boolean;
    itemLabelPosition: string;
} & {
    name?: string | undefined;
    color?: string | undefined;
    class?: any;
    theme?: string | undefined;
    activeColor?: string | undefined;
    itemLabels?: string[] | undefined;
} & {
    $children?: {} | vue.VNodeChild | {
        item?: ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
        'item-label'?: ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        item?: false | ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
        'item-label'?: false | ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:item"?: false | ((arg: VRatingItemSlot) => vue.VNodeChild) | undefined;
    "v-slot:item-label"?: false | ((arg: VRatingItemLabelSlot) => vue.VNodeChild) | undefined;
} & {
    "onUpdate:modelValue"?: ((value: string | number) => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (value: number | string) => true;
}, string, {
    length: string | number;
    style: vue.StyleValue;
    disabled: boolean;
    size: string | number;
    readonly: boolean;
    tag: string;
    modelValue: string | number;
    density: Density;
    ripple: boolean;
    clearable: boolean;
    hover: boolean;
    itemAriaLabel: string;
    emptyIcon: IconValue;
    fullIcon: IconValue;
    halfIncrements: boolean;
    itemLabelPosition: string;
}, {}, string, vue.SlotsType<Partial<{
    item: (arg: VRatingItemSlot) => vue.VNode[];
    'item-label': (arg: VRatingItemLabelSlot) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    size: {
        type: (StringConstructor | NumberConstructor)[];
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
    name: StringConstructor;
    itemAriaLabel: {
        type: StringConstructor;
        default: string;
    };
    activeColor: StringConstructor;
    color: StringConstructor;
    clearable: BooleanConstructor;
    disabled: BooleanConstructor;
    emptyIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    fullIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    halfIncrements: BooleanConstructor;
    hover: BooleanConstructor;
    length: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    readonly: BooleanConstructor;
    modelValue: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    itemLabels: Prop<string[]>;
    itemLabelPosition: {
        type: StringConstructor;
        default: string;
        validator: (v: any) => boolean;
    };
    ripple: BooleanConstructor;
}, vue.ExtractPropTypes<{
    theme: StringConstructor;
    tag: {
        type: StringConstructor;
        default: string;
    };
    size: {
        type: (StringConstructor | NumberConstructor)[];
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
    name: StringConstructor;
    itemAriaLabel: {
        type: StringConstructor;
        default: string;
    };
    activeColor: StringConstructor;
    color: StringConstructor;
    clearable: BooleanConstructor;
    disabled: BooleanConstructor;
    emptyIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    fullIcon: {
        type: vue.PropType<IconValue>;
        default: string;
    };
    halfIncrements: BooleanConstructor;
    hover: BooleanConstructor;
    length: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    readonly: BooleanConstructor;
    modelValue: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    itemLabels: Prop<string[]>;
    itemLabelPosition: {
        type: StringConstructor;
        default: string;
        validator: (v: any) => boolean;
    };
    ripple: BooleanConstructor;
}>>;
type VRating = InstanceType<typeof VRating>;

export { VRating };
