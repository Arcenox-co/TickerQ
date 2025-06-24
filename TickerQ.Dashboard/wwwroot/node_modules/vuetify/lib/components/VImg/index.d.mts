import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

interface srcObject {
    src?: string;
    srcset?: string;
    lazySrc?: string;
    aspect: number;
}
declare const VImg: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        inline: boolean;
        absolute: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        style: vue.StyleValue;
        eager: boolean;
        options: IntersectionObserverInit;
        cover: boolean;
        src: string | srcObject;
        tile: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        aspectRatio?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: string | undefined;
        draggable?: boolean | "false" | "true" | undefined;
        class?: any;
        alt?: string | undefined;
        referrerpolicy?: "origin" | "same-origin" | "no-referrer" | "no-referrer-when-downgrade" | "origin-when-cross-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url" | undefined;
        crossorigin?: "" | "anonymous" | "use-credentials" | undefined;
        sizes?: string | undefined;
        srcset?: string | undefined;
        rounded?: string | number | boolean | undefined;
        contentClass?: any;
        gradient?: string | undefined;
        lazySrc?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            placeholder?: (() => vue.VNodeChild) | undefined;
            error?: (() => vue.VNodeChild) | undefined;
            sources?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            placeholder?: false | (() => vue.VNodeChild) | undefined;
            error?: false | (() => vue.VNodeChild) | undefined;
            sources?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:placeholder"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:error"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:sources"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        onError?: ((value: string | undefined) => any) | undefined;
        onLoad?: ((value: string | undefined) => any) | undefined;
        onLoadstart?: ((value: string | undefined) => any) | undefined;
    }, {
        currentSrc: vue.ShallowRef<string>;
        image: vue.Ref<HTMLImageElement | undefined>;
        state: vue.ShallowRef<"error" | "loaded" | "idle" | "loading">;
        naturalWidth: vue.ShallowRef<number | undefined>;
        naturalHeight: vue.ShallowRef<number | undefined>;
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        loadstart: (value: string | undefined) => true;
        load: (value: string | undefined) => true;
        error: (value: string | undefined) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        inline: boolean;
        absolute: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        style: vue.StyleValue;
        eager: boolean;
        options: IntersectionObserverInit;
        cover: boolean;
        src: string | srcObject;
        tile: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        aspectRatio?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: string | undefined;
        draggable?: boolean | "false" | "true" | undefined;
        class?: any;
        alt?: string | undefined;
        referrerpolicy?: "origin" | "same-origin" | "no-referrer" | "no-referrer-when-downgrade" | "origin-when-cross-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url" | undefined;
        crossorigin?: "" | "anonymous" | "use-credentials" | undefined;
        sizes?: string | undefined;
        srcset?: string | undefined;
        rounded?: string | number | boolean | undefined;
        contentClass?: any;
        gradient?: string | undefined;
        lazySrc?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            placeholder?: (() => vue.VNodeChild) | undefined;
            error?: (() => vue.VNodeChild) | undefined;
            sources?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            placeholder?: false | (() => vue.VNodeChild) | undefined;
            error?: false | (() => vue.VNodeChild) | undefined;
            sources?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:placeholder"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:error"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:sources"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        onError?: ((value: string | undefined) => any) | undefined;
        onLoad?: ((value: string | undefined) => any) | undefined;
        onLoadstart?: ((value: string | undefined) => any) | undefined;
    }, {
        inline: boolean;
        absolute: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        style: vue.StyleValue;
        draggable: boolean | "false" | "true";
        eager: boolean;
        options: IntersectionObserverInit;
        cover: boolean;
        src: string | srcObject;
        rounded: string | number | boolean;
        tile: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: () => vue.VNode[];
        placeholder: () => vue.VNode[];
        error: () => vue.VNode[];
        sources: () => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        inline: boolean;
        absolute: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        style: vue.StyleValue;
        eager: boolean;
        options: IntersectionObserverInit;
        cover: boolean;
        src: string | srcObject;
        tile: boolean;
    } & {
        height?: string | number | undefined;
        width?: string | number | undefined;
        aspectRatio?: string | number | undefined;
        color?: string | undefined;
        maxHeight?: string | number | undefined;
        maxWidth?: string | number | undefined;
        minHeight?: string | number | undefined;
        minWidth?: string | number | undefined;
        position?: string | undefined;
        draggable?: boolean | "false" | "true" | undefined;
        class?: any;
        alt?: string | undefined;
        referrerpolicy?: "origin" | "same-origin" | "no-referrer" | "no-referrer-when-downgrade" | "origin-when-cross-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url" | undefined;
        crossorigin?: "" | "anonymous" | "use-credentials" | undefined;
        sizes?: string | undefined;
        srcset?: string | undefined;
        rounded?: string | number | boolean | undefined;
        contentClass?: any;
        gradient?: string | undefined;
        lazySrc?: string | undefined;
    } & {
        $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
            default?: (() => vue.VNodeChild) | undefined;
            placeholder?: (() => vue.VNodeChild) | undefined;
            error?: (() => vue.VNodeChild) | undefined;
            sources?: (() => vue.VNodeChild) | undefined;
        };
        'v-slots'?: {
            default?: false | (() => vue.VNodeChild) | undefined;
            placeholder?: false | (() => vue.VNodeChild) | undefined;
            error?: false | (() => vue.VNodeChild) | undefined;
            sources?: false | (() => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:placeholder"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:error"?: false | (() => vue.VNodeChild) | undefined;
        "v-slot:sources"?: false | (() => vue.VNodeChild) | undefined;
    } & {
        onError?: ((value: string | undefined) => any) | undefined;
        onLoad?: ((value: string | undefined) => any) | undefined;
        onLoadstart?: ((value: string | undefined) => any) | undefined;
    }, {
        currentSrc: vue.ShallowRef<string>;
        image: vue.Ref<HTMLImageElement | undefined>;
        state: vue.ShallowRef<"error" | "loaded" | "idle" | "loading">;
        naturalWidth: vue.ShallowRef<number | undefined>;
        naturalHeight: vue.ShallowRef<number | undefined>;
    }, {}, {}, {}, {
        inline: boolean;
        absolute: boolean;
        transition: string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        });
        style: vue.StyleValue;
        draggable: boolean | "false" | "true";
        eager: boolean;
        options: IntersectionObserverInit;
        cover: boolean;
        src: string | srcObject;
        rounded: string | number | boolean;
        tile: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    inline: boolean;
    absolute: boolean;
    transition: string | boolean | (vue.TransitionProps & {
        component?: vue.Component;
    });
    style: vue.StyleValue;
    eager: boolean;
    options: IntersectionObserverInit;
    cover: boolean;
    src: string | srcObject;
    tile: boolean;
} & {
    height?: string | number | undefined;
    width?: string | number | undefined;
    aspectRatio?: string | number | undefined;
    color?: string | undefined;
    maxHeight?: string | number | undefined;
    maxWidth?: string | number | undefined;
    minHeight?: string | number | undefined;
    minWidth?: string | number | undefined;
    position?: string | undefined;
    draggable?: boolean | "false" | "true" | undefined;
    class?: any;
    alt?: string | undefined;
    referrerpolicy?: "origin" | "same-origin" | "no-referrer" | "no-referrer-when-downgrade" | "origin-when-cross-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url" | undefined;
    crossorigin?: "" | "anonymous" | "use-credentials" | undefined;
    sizes?: string | undefined;
    srcset?: string | undefined;
    rounded?: string | number | boolean | undefined;
    contentClass?: any;
    gradient?: string | undefined;
    lazySrc?: string | undefined;
} & {
    $children?: vue.VNodeChild | (() => vue.VNodeChild) | {
        default?: (() => vue.VNodeChild) | undefined;
        placeholder?: (() => vue.VNodeChild) | undefined;
        error?: (() => vue.VNodeChild) | undefined;
        sources?: (() => vue.VNodeChild) | undefined;
    };
    'v-slots'?: {
        default?: false | (() => vue.VNodeChild) | undefined;
        placeholder?: false | (() => vue.VNodeChild) | undefined;
        error?: false | (() => vue.VNodeChild) | undefined;
        sources?: false | (() => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:placeholder"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:error"?: false | (() => vue.VNodeChild) | undefined;
    "v-slot:sources"?: false | (() => vue.VNodeChild) | undefined;
} & {
    onError?: ((value: string | undefined) => any) | undefined;
    onLoad?: ((value: string | undefined) => any) | undefined;
    onLoadstart?: ((value: string | undefined) => any) | undefined;
}, {
    currentSrc: vue.ShallowRef<string>;
    image: vue.Ref<HTMLImageElement | undefined>;
    state: vue.ShallowRef<"error" | "loaded" | "idle" | "loading">;
    naturalWidth: vue.ShallowRef<number | undefined>;
    naturalHeight: vue.ShallowRef<number | undefined>;
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    loadstart: (value: string | undefined) => true;
    load: (value: string | undefined) => true;
    error: (value: string | undefined) => true;
}, string, {
    inline: boolean;
    absolute: boolean;
    transition: string | boolean | (vue.TransitionProps & {
        component?: vue.Component;
    });
    style: vue.StyleValue;
    draggable: boolean | "false" | "true";
    eager: boolean;
    options: IntersectionObserverInit;
    cover: boolean;
    src: string | srcObject;
    rounded: string | number | boolean;
    tile: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: () => vue.VNode[];
    placeholder: () => vue.VNode[];
    error: () => vue.VNode[];
    sources: () => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    transition: {
        type: PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    aspectRatio: (StringConstructor | NumberConstructor)[];
    contentClass: null;
    inline: BooleanConstructor;
    absolute: BooleanConstructor;
    alt: StringConstructor;
    cover: BooleanConstructor;
    color: StringConstructor;
    draggable: {
        type: PropType<boolean | "true" | "false">;
        default: undefined;
    };
    eager: BooleanConstructor;
    gradient: StringConstructor;
    lazySrc: StringConstructor;
    options: {
        type: PropType<IntersectionObserverInit>;
        default: () => {
            root: undefined;
            rootMargin: undefined;
            threshold: undefined;
        };
    };
    sizes: StringConstructor;
    src: {
        type: PropType<string | srcObject>;
        default: string;
    };
    crossorigin: PropType<"" | "anonymous" | "use-credentials">;
    referrerpolicy: PropType<"no-referrer" | "no-referrer-when-downgrade" | "origin" | "origin-when-cross-origin" | "same-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url">;
    srcset: StringConstructor;
    position: StringConstructor;
}, vue.ExtractPropTypes<{
    transition: {
        type: PropType<string | boolean | (vue.TransitionProps & {
            component?: vue.Component;
        })>;
        default: string;
        validator: (val: unknown) => boolean;
    };
    rounded: {
        type: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        default: undefined;
    };
    tile: BooleanConstructor;
    class: PropType<ClassValue>;
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
    };
    height: (StringConstructor | NumberConstructor)[];
    maxHeight: (StringConstructor | NumberConstructor)[];
    maxWidth: (StringConstructor | NumberConstructor)[];
    minHeight: (StringConstructor | NumberConstructor)[];
    minWidth: (StringConstructor | NumberConstructor)[];
    width: (StringConstructor | NumberConstructor)[];
    aspectRatio: (StringConstructor | NumberConstructor)[];
    contentClass: null;
    inline: BooleanConstructor;
    absolute: BooleanConstructor;
    alt: StringConstructor;
    cover: BooleanConstructor;
    color: StringConstructor;
    draggable: {
        type: PropType<boolean | "true" | "false">;
        default: undefined;
    };
    eager: BooleanConstructor;
    gradient: StringConstructor;
    lazySrc: StringConstructor;
    options: {
        type: PropType<IntersectionObserverInit>;
        default: () => {
            root: undefined;
            rootMargin: undefined;
            threshold: undefined;
        };
    };
    sizes: StringConstructor;
    src: {
        type: PropType<string | srcObject>;
        default: string;
    };
    crossorigin: PropType<"" | "anonymous" | "use-credentials">;
    referrerpolicy: PropType<"no-referrer" | "no-referrer-when-downgrade" | "origin" | "origin-when-cross-origin" | "same-origin" | "strict-origin" | "strict-origin-when-cross-origin" | "unsafe-url">;
    srcset: StringConstructor;
    position: StringConstructor;
}>>;
type VImg = InstanceType<typeof VImg>;

export { VImg };
