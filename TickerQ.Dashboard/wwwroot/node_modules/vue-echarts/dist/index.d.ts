import { Ref, InjectionKey, DefineComponent } from 'vue-demi';
import { init, SetOptionOpts, ECElementEvent, ElementEvent } from 'echarts/core';

type Injection<T> = T | null | Ref<T | null> | { value: T | null };

type InitType = typeof init;
type InitParameters = Parameters<InitType>;
type Theme = NonNullable<InitParameters[1]>;
type ThemeInjection = Injection<Theme>;
type InitOptions = NonNullable<InitParameters[2]>;

type InitOptionsInjection = Injection<InitOptions>;

type UpdateOptions = SetOptionOpts;
type UpdateOptionsInjection = Injection<UpdateOptions>;

type EChartsType = ReturnType<InitType>;

type SetOptionType = EChartsType["setOption"];
type Option = Parameters<SetOptionType>[0];

type AutoResize =
  | boolean
  | {
      throttle?: number;
      onResize?: () => void;
    };

type LoadingOptions = {
  text?: string;
  textColor?: string;
  fontSize?: number | string;
  fontWeight?: number | string;
  fontStyle?: string;
  fontFamily?: string;
  maskColor?: string;
  showSpinner?: boolean;
  color?: string;
  spinnerRadius?: number;
  lineWidth?: number;
  zlevel?: number;
};
type LoadingOptionsInjection = Injection<LoadingOptions>;

type MouseEventName =
  | "click"
  | "dblclick"
  | "mouseout"
  | "mouseover"
  | "mouseup"
  | "mousedown"
  | "mousemove"
  | "contextmenu"
  | "globalout";

type ElementEventName =
  | MouseEventName
  | "mousewheel"
  | "drag"
  | "dragstart"
  | "dragend"
  | "dragenter"
  | "dragleave"
  | "dragover"
  | "drop";

type ZRenderEventName = `zr:${ElementEventName}`;

type OtherEventName =
  | "highlight"
  | "downplay"
  | "selectchanged"
  | "legendselectchanged"
  | "legendselected"
  | "legendunselected"
  | "legendselectall"
  | "legendinverseselect"
  | "legendscroll"
  | "datazoom"
  | "datarangeselected"
  | "graphroam"
  | "georoam"
  | "treeroam"
  | "timelinechanged"
  | "timelineplaychanged"
  | "restore"
  | "dataviewchanged"
  | "magictypechanged"
  | "geoselectchanged"
  | "geoselected"
  | "geounselected"
  | "axisareaselected"
  | "brush"
  | "brushEnd"
  | "brushselected"
  | "globalcursortaken";

type MouseEmits = {
  [key in MouseEventName]: (params: ECElementEvent) => void;
};

type ZRenderEmits = {
  [key in ZRenderEventName]: (params: ElementEvent) => void;
};

type OtherEmits = {
  [key in OtherEventName]: (params: any) => void;
};

type Emits = MouseEmits &
  OtherEmits & {
    rendered: (params: { elapsedTime: number }) => void;
    finished: () => void;
  } & ZRenderEmits;

/* eslint-disable @typescript-eslint/ban-types */


declare const THEME_KEY: InjectionKey<ThemeInjection>;
declare const INIT_OPTIONS_KEY: InjectionKey<InitOptionsInjection>;
declare const UPDATE_OPTIONS_KEY: InjectionKey<UpdateOptionsInjection>;
declare const LOADING_OPTIONS_KEY: InjectionKey<LoadingOptionsInjection>;

declare type ChartProps = {
  theme?: Theme;
  initOptions?: InitOptions;
  updateOptions?: UpdateOptions;
  loadingOptions?: LoadingOptions;
  option?: Option;
  autoresize?: AutoResize;
  loading?: boolean;
  group?: string;
  manualUpdate?: boolean;
};

// convert Emits to Props
// click => onClick
declare type ChartEventProps = {
  [key in keyof Emits as key extends string
    ? `on${Capitalize<key>}`
    : never]?: Emits[key];
};

type MethodNames =
  | "getWidth"
  | "getHeight"
  | "getDom"
  | "getOption"
  | "resize"
  | "dispatchAction"
  | "convertToPixel"
  | "convertFromPixel"
  | "containPixel"
  | "getDataURL"
  | "getConnectedDataURL"
  | "appendData"
  | "clear"
  | "isDisposed"
  | "dispose"
  | "setOption";

declare type ChartMethods = Pick<EChartsType, MethodNames>;

declare const Chart: DefineComponent<
  ChartProps & ChartEventProps,
  {
    root: Ref<HTMLElement | undefined>;
    chart: Ref<EChartsType | undefined>;
  },
  {},
  {},
  ChartMethods
>;

export { INIT_OPTIONS_KEY, LOADING_OPTIONS_KEY, THEME_KEY, UPDATE_OPTIONS_KEY, Chart as default };
