import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType, DeepReadonly } from 'vue';

type HSV = {
    h: number;
    s: number;
    v: number;
    a?: number;
};
type RGB = {
    r: number;
    g: number;
    b: number;
    a?: number;
};
type HSL = {
    h: number;
    s: number;
    l: number;
    a?: number;
};
type Color = string | number | HSV | RGB | HSL;

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

type ColorPickerMode = {
    inputProps: Record<string, unknown>;
    inputs: {
        [key: string]: any;
        getValue: (color: any) => number | string;
        getColor: (color: any, v: string) => any;
    }[];
    from: (color: any) => HSV;
    to: (color: HSV) => any;
};
declare const modes: {
    rgb: {
        inputs: {
            [key: string]: any;
            getValue: (color: any) => number | string;
            getColor: (color: any, v: string) => any;
        }[];
        inputProps: Record<string, unknown>;
        from: (color: any) => HSV;
        to: (color: HSV) => any;
    };
    rgba: ColorPickerMode;
    hsl: {
        inputs: {
            [key: string]: any;
            getValue: (color: any) => number | string;
            getColor: (color: any, v: string) => any;
        }[];
        inputProps: Record<string, unknown>;
        from: (color: any) => HSV;
        to: (color: HSV) => any;
    };
    hsla: ColorPickerMode;
    hex: {
        inputs: {
            label: string;
            getValue: (c: string) => string;
            getColor: (c: string, v: string) => string;
        }[];
        inputProps: Record<string, unknown>;
        from: (color: any) => HSV;
        to: (color: HSV) => any;
    };
    hexa: ColorPickerMode;
};

declare const VColorPicker: {
    new (...args: any[]): vue.CreateComponentPublicInstance<Readonly<vue.ExtractPropTypes<{
        width: {
            type: PropType<string | number>;
            default: NonNullable<string | number>;
        };
        border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        color: StringConstructor;
        position: {
            type: PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
            validator: (v: any) => boolean;
        };
        style: {
            type: PropType<vue.StyleValue>;
            default: null;
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
        canvasHeight: {
            type: (StringConstructor | NumberConstructor)[];
            default: number;
        };
        disabled: BooleanConstructor;
        dotSize: {
            type: (StringConstructor | NumberConstructor)[];
            default: number;
        };
        hideCanvas: BooleanConstructor;
        hideSliders: BooleanConstructor;
        hideInputs: BooleanConstructor;
        mode: {
            type: PropType<keyof typeof modes>;
            default: string;
            validator: (v: string) => boolean;
        };
        modes: {
            type: PropType<readonly (keyof typeof modes)[]>;
            default: () => string[];
            validator: (v: any) => boolean;
        };
        showSwatches: BooleanConstructor;
        swatches: PropType<DeepReadonly<Color[][]>>;
        swatchesMaxHeight: {
            type: (StringConstructor | NumberConstructor)[];
            default: number;
        };
        modelValue: {
            type: PropType<Record<string, unknown> | string | undefined | null>;
        };
    }>> & {
        "onUpdate:modelValue"?: ((color: any) => any) | undefined;
        "onUpdate:mode"?: ((mode: "rgb" | "hex" | "rgba" | "hsl" | "hsla" | "hexa") => any) | undefined;
    }, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (color: any) => true;
        'update:mode': (mode: keyof typeof modes) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & Readonly<vue.ExtractPropTypes<{
        width: {
            type: PropType<string | number>;
            default: NonNullable<string | number>;
        };
        border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        color: StringConstructor;
        position: {
            type: PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
            validator: (v: any) => boolean;
        };
        style: {
            type: PropType<vue.StyleValue>;
            default: null;
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
        canvasHeight: {
            type: (StringConstructor | NumberConstructor)[];
            default: number;
        };
        disabled: BooleanConstructor;
        dotSize: {
            type: (StringConstructor | NumberConstructor)[];
            default: number;
        };
        hideCanvas: BooleanConstructor;
        hideSliders: BooleanConstructor;
        hideInputs: BooleanConstructor;
        mode: {
            type: PropType<keyof typeof modes>;
            default: string;
            validator: (v: string) => boolean;
        };
        modes: {
            type: PropType<readonly (keyof typeof modes)[]>;
            default: () => string[];
            validator: (v: any) => boolean;
        };
        showSwatches: BooleanConstructor;
        swatches: PropType<DeepReadonly<Color[][]>>;
        swatchesMaxHeight: {
            type: (StringConstructor | NumberConstructor)[];
            default: number;
        };
        modelValue: {
            type: PropType<Record<string, unknown> | string | undefined | null>;
        };
    }>> & {
        "onUpdate:modelValue"?: ((color: any) => any) | undefined;
        "onUpdate:mode"?: ((mode: "rgb" | "hex" | "rgba" | "hsl" | "hsla" | "hexa") => any) | undefined;
    }, {
        width: string | number;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        mode: "rgb" | "hex" | "rgba" | "hsl" | "hsla" | "hexa";
        rounded: string | number | boolean;
        tile: boolean;
        dotSize: string | number;
        modes: readonly ("rgb" | "hex" | "rgba" | "hsl" | "hsla" | "hexa")[];
        canvasHeight: string | number;
        hideCanvas: boolean;
        hideSliders: boolean;
        hideInputs: boolean;
        showSwatches: boolean;
        swatchesMaxHeight: string | number;
    }, true, {}, {}, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, Readonly<vue.ExtractPropTypes<{
        width: {
            type: PropType<string | number>;
            default: NonNullable<string | number>;
        };
        border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
        color: StringConstructor;
        position: {
            type: PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
            validator: (v: any) => boolean;
        };
        style: {
            type: PropType<vue.StyleValue>;
            default: null;
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
        canvasHeight: {
            type: (StringConstructor | NumberConstructor)[];
            default: number;
        };
        disabled: BooleanConstructor;
        dotSize: {
            type: (StringConstructor | NumberConstructor)[];
            default: number;
        };
        hideCanvas: BooleanConstructor;
        hideSliders: BooleanConstructor;
        hideInputs: BooleanConstructor;
        mode: {
            type: PropType<keyof typeof modes>;
            default: string;
            validator: (v: string) => boolean;
        };
        modes: {
            type: PropType<readonly (keyof typeof modes)[]>;
            default: () => string[];
            validator: (v: any) => boolean;
        };
        showSwatches: BooleanConstructor;
        swatches: PropType<DeepReadonly<Color[][]>>;
        swatchesMaxHeight: {
            type: (StringConstructor | NumberConstructor)[];
            default: number;
        };
        modelValue: {
            type: PropType<Record<string, unknown> | string | undefined | null>;
        };
    }>> & {
        "onUpdate:modelValue"?: ((color: any) => any) | undefined;
        "onUpdate:mode"?: ((mode: "rgb" | "hex" | "rgba" | "hsl" | "hsla" | "hexa") => any) | undefined;
    }, {}, {}, {}, {}, {
        width: string | number;
        style: vue.StyleValue;
        disabled: boolean;
        tag: string;
        mode: "rgb" | "hex" | "rgba" | "hsl" | "hsla" | "hexa";
        rounded: string | number | boolean;
        tile: boolean;
        dotSize: string | number;
        modes: readonly ("rgb" | "hex" | "rgba" | "hsl" | "hsla" | "hexa")[];
        canvasHeight: string | number;
        hideCanvas: boolean;
        hideSliders: boolean;
        hideInputs: boolean;
        showSwatches: boolean;
        swatchesMaxHeight: string | number;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<Readonly<vue.ExtractPropTypes<{
    width: {
        type: PropType<string | number>;
        default: NonNullable<string | number>;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    position: {
        type: PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
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
    canvasHeight: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    disabled: BooleanConstructor;
    dotSize: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    hideCanvas: BooleanConstructor;
    hideSliders: BooleanConstructor;
    hideInputs: BooleanConstructor;
    mode: {
        type: PropType<keyof typeof modes>;
        default: string;
        validator: (v: string) => boolean;
    };
    modes: {
        type: PropType<readonly (keyof typeof modes)[]>;
        default: () => string[];
        validator: (v: any) => boolean;
    };
    showSwatches: BooleanConstructor;
    swatches: PropType<DeepReadonly<Color[][]>>;
    swatchesMaxHeight: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    modelValue: {
        type: PropType<Record<string, unknown> | string | undefined | null>;
    };
}>> & {
    "onUpdate:modelValue"?: ((color: any) => any) | undefined;
    "onUpdate:mode"?: ((mode: "rgb" | "hex" | "rgba" | "hsl" | "hsla" | "hexa") => any) | undefined;
}, {}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (color: any) => true;
    'update:mode': (mode: keyof typeof modes) => true;
}, string, {
    width: string | number;
    style: vue.StyleValue;
    disabled: boolean;
    tag: string;
    mode: "rgb" | "hex" | "rgba" | "hsl" | "hsla" | "hexa";
    rounded: string | number | boolean;
    tile: boolean;
    dotSize: string | number;
    modes: readonly ("rgb" | "hex" | "rgba" | "hsl" | "hsla" | "hexa")[];
    canvasHeight: string | number;
    hideCanvas: boolean;
    hideSliders: boolean;
    hideInputs: boolean;
    showSwatches: boolean;
    swatchesMaxHeight: string | number;
}, {}, string, {}> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    width: {
        type: PropType<string | number>;
        default: NonNullable<string | number>;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    position: {
        type: PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
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
    canvasHeight: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    disabled: BooleanConstructor;
    dotSize: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    hideCanvas: BooleanConstructor;
    hideSliders: BooleanConstructor;
    hideInputs: BooleanConstructor;
    mode: {
        type: PropType<keyof typeof modes>;
        default: string;
        validator: (v: string) => boolean;
    };
    modes: {
        type: PropType<readonly (keyof typeof modes)[]>;
        default: () => string[];
        validator: (v: any) => boolean;
    };
    showSwatches: BooleanConstructor;
    swatches: PropType<DeepReadonly<Color[][]>>;
    swatchesMaxHeight: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    modelValue: {
        type: PropType<Record<string, unknown> | string | undefined | null>;
    };
}, vue.ExtractPropTypes<{
    width: {
        type: PropType<string | number>;
        default: NonNullable<string | number>;
    };
    border: (StringConstructor | BooleanConstructor | NumberConstructor)[];
    color: StringConstructor;
    position: {
        type: PropType<"fixed" | "absolute" | "relative" | "static" | "sticky">;
        validator: (v: any) => boolean;
    };
    style: {
        type: PropType<vue.StyleValue>;
        default: null;
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
    canvasHeight: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    disabled: BooleanConstructor;
    dotSize: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    hideCanvas: BooleanConstructor;
    hideSliders: BooleanConstructor;
    hideInputs: BooleanConstructor;
    mode: {
        type: PropType<keyof typeof modes>;
        default: string;
        validator: (v: string) => boolean;
    };
    modes: {
        type: PropType<readonly (keyof typeof modes)[]>;
        default: () => string[];
        validator: (v: any) => boolean;
    };
    showSwatches: BooleanConstructor;
    swatches: PropType<DeepReadonly<Color[][]>>;
    swatchesMaxHeight: {
        type: (StringConstructor | NumberConstructor)[];
        default: number;
    };
    modelValue: {
        type: PropType<Record<string, unknown> | string | undefined | null>;
    };
}>>;
type VColorPicker = InstanceType<typeof VColorPicker>;

export { VColorPicker };
