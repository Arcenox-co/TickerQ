import { DirectiveBinding, VNode } from 'vue';

interface ClickOutsideBindingArgs {
    handler: (e: MouseEvent) => void;
    closeConditional?: (e: Event) => boolean;
    include?: () => HTMLElement[];
}
interface ClickOutsideDirectiveBinding extends DirectiveBinding {
    value: ((e: MouseEvent) => void) | ClickOutsideBindingArgs;
}
declare const ClickOutside: {
    mounted(el: HTMLElement, binding: ClickOutsideDirectiveBinding): void;
    beforeUnmount(el: HTMLElement, binding: ClickOutsideDirectiveBinding): void;
};

type ObserveHandler = (isIntersecting: boolean, entries: IntersectionObserverEntry[], observer: IntersectionObserver) => void;
interface ObserveDirectiveBinding extends Omit<DirectiveBinding, 'modifiers' | 'value'> {
    value?: ObserveHandler | {
        handler: ObserveHandler;
        options?: IntersectionObserverInit;
    };
    modifiers: {
        once?: boolean;
        quiet?: boolean;
    };
}
declare function mounted$5(el: HTMLElement, binding: ObserveDirectiveBinding): void;
declare function unmounted$5(el: HTMLElement, binding: ObserveDirectiveBinding): void;
declare const Intersect: {
    mounted: typeof mounted$5;
    unmounted: typeof unmounted$5;
};

interface MutationOptions {
    attr?: boolean;
    char?: boolean;
    child?: boolean;
    sub?: boolean;
    once?: boolean;
    immediate?: boolean;
}

interface MutationDirectiveBinding extends Omit<DirectiveBinding, 'modifiers' | 'value'> {
    value: MutationCallback | {
        handler: MutationCallback;
        options?: MutationObserverInit;
    };
    modifiers: MutationOptions;
}
declare function mounted$4(el: HTMLElement, binding: MutationDirectiveBinding): void;
declare function unmounted$4(el: HTMLElement, binding: MutationDirectiveBinding): void;
declare const Mutate: {
    mounted: typeof mounted$4;
    unmounted: typeof unmounted$4;
};

interface ResizeDirectiveBinding extends Omit<DirectiveBinding, 'modifiers'> {
    value: () => void;
    modifiers?: {
        active?: boolean;
        quiet?: boolean;
    };
}
declare function mounted$3(el: HTMLElement, binding: ResizeDirectiveBinding): void;
declare function unmounted$3(el: HTMLElement, binding: ResizeDirectiveBinding): void;
declare const Resize: {
    mounted: typeof mounted$3;
    unmounted: typeof unmounted$3;
};

interface RippleDirectiveBinding extends Omit<DirectiveBinding, 'modifiers' | 'value'> {
    value?: boolean | {
        class: string;
    };
    modifiers: {
        center?: boolean;
        circle?: boolean;
        stop?: boolean;
    };
}
declare function mounted$2(el: HTMLElement, binding: RippleDirectiveBinding): void;
declare function unmounted$2(el: HTMLElement): void;
declare function updated$1(el: HTMLElement, binding: RippleDirectiveBinding): void;
declare const Ripple: {
    mounted: typeof mounted$2;
    unmounted: typeof unmounted$2;
    updated: typeof updated$1;
};

interface ScrollDirectiveBinding extends Omit<DirectiveBinding, 'modifiers'> {
    value: EventListener | {
        handler: EventListener;
        options?: AddEventListenerOptions;
    } | EventListenerObject & {
        options?: AddEventListenerOptions;
    };
    modifiers?: {
        self?: boolean;
    };
}
declare function mounted$1(el: HTMLElement, binding: ScrollDirectiveBinding): void;
declare function unmounted$1(el: HTMLElement, binding: ScrollDirectiveBinding): void;
declare function updated(el: HTMLElement, binding: ScrollDirectiveBinding): void;
declare const Scroll: {
    mounted: typeof mounted$1;
    unmounted: typeof unmounted$1;
    updated: typeof updated;
};

interface TouchHandlers {
    start?: (wrapperEvent: {
        originalEvent: TouchEvent;
    } & TouchData) => void;
    end?: (wrapperEvent: {
        originalEvent: TouchEvent;
    } & TouchData) => void;
    move?: (wrapperEvent: {
        originalEvent: TouchEvent;
    } & TouchData) => void;
    left?: (wrapper: TouchData) => void;
    right?: (wrapper: TouchData) => void;
    up?: (wrapper: TouchData) => void;
    down?: (wrapper: TouchData) => void;
}
interface TouchData {
    touchstartX: number;
    touchstartY: number;
    touchmoveX: number;
    touchmoveY: number;
    touchendX: number;
    touchendY: number;
    offsetX: number;
    offsetY: number;
}
interface TouchValue extends TouchHandlers {
    parent?: boolean;
    options?: AddEventListenerOptions;
}
interface TouchDirectiveBinding extends Omit<DirectiveBinding, 'value'> {
    value?: TouchValue;
}
declare function mounted(el: HTMLElement, binding: TouchDirectiveBinding): void;
declare function unmounted(el: HTMLElement, binding: TouchDirectiveBinding): void;
declare const Touch: {
    mounted: typeof mounted;
    unmounted: typeof unmounted;
};

declare const block: readonly ["top", "bottom"];
declare const inline: readonly ["start", "end", "left", "right"];
type Tblock = typeof block[number];
type Tinline = typeof inline[number];
type Anchor = Tblock | Tinline | 'center' | 'center center' | `${Tblock} ${Tinline | 'center'}` | `${Tinline} ${Tblock | 'center'}`;

declare const CustomDirectiveSymbol: unique symbol;
type DirectiveHook<B extends DirectiveBinding> = (el: any, binding: B, vnode: VNode<any, any>, prevVNode: VNode<any, any>) => void;
interface CustomDirective<B extends DirectiveBinding = DirectiveBinding> {
    created?: DirectiveHook<B>;
    beforeMount?: DirectiveHook<B>;
    mounted?: DirectiveHook<B>;
    beforeUpdate?: DirectiveHook<B>;
    updated?: DirectiveHook<B>;
    beforeUnmount?: DirectiveHook<B>;
    unmounted?: DirectiveHook<B>;
    [CustomDirectiveSymbol]: true;
}

interface TooltipDirectiveBinding extends Omit<DirectiveBinding<string>, 'arg' | 'value'> {
    arg?: {
        [T in Anchor]: T extends `${infer A} ${infer B}` ? `${A}-${B}` : T;
    }[Anchor];
    value: boolean | string | Record<string, any>;
}
declare const Tooltip: CustomDirective<TooltipDirectiveBinding>;

export { ClickOutside, Intersect, Mutate, Resize, Ripple, Scroll, Tooltip, Touch };
