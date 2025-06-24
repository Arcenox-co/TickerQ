import * as vue from 'vue';
import { ComponentPropsOptions, ExtractPropTypes, PropType } from 'vue';

interface FilterPropsOptions<PropsOptions extends Readonly<ComponentPropsOptions>, Props = ExtractPropTypes<PropsOptions>> {
    filterProps<T extends Partial<Props>, U extends Exclude<keyof Props, Exclude<keyof Props, keyof T>>>(props: T): Partial<Pick<T, U>>;
}

type ClassValue = any;

type EventProp<T extends any[] = any[], F = (...args: T) => void> = F;
declare const EventProp: <T extends any[] = any[]>() => PropType<EventProp<T>>;

type ValidationResult = string | boolean;
type ValidationRule = ValidationResult | PromiseLike<ValidationResult> | ((value: any) => ValidationResult) | ((value: any) => PromiseLike<ValidationResult>);
type ValidateOnValue = 'blur' | 'input' | 'submit' | 'invalid-input';
type ValidateOn = ValidateOnValue | `${ValidateOnValue} lazy` | `${ValidateOnValue} eager` | `lazy ${ValidateOnValue}` | `eager ${ValidateOnValue}` | 'lazy' | 'eager';
interface ValidationProps {
    disabled: boolean | null;
    error: boolean;
    errorMessages: string | readonly string[] | null;
    focused: boolean;
    maxErrors: string | number;
    name: string | undefined;
    label: string | undefined;
    readonly: boolean | null;
    rules: readonly ValidationRule[];
    modelValue: any;
    'onUpdate:modelValue': EventProp | undefined;
    validateOn?: ValidateOn;
    validationValue: any;
}

interface FieldValidationResult {
    id: number | string;
    errorMessages: string[];
}
interface FormValidationResult {
    valid: boolean;
    errors: FieldValidationResult[];
}
interface SubmitEventPromise extends SubmitEvent, Promise<FormValidationResult> {
}
interface FormProps {
    disabled: boolean;
    fastFail: boolean;
    readonly: boolean;
    modelValue: boolean | null;
    'onUpdate:modelValue': EventProp<[boolean | null]> | undefined;
    validateOn: ValidationProps['validateOn'];
}

declare const VForm: {
    new (...args: any[]): vue.CreateComponentPublicInstance<{
        style: vue.StyleValue;
        disabled: boolean;
        readonly: boolean;
        modelValue: boolean | null;
        validateOn: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
        fastFail: boolean;
    } & {
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                errors: vue.Ref<{
                    id: number | string;
                    errorMessages: string[];
                }[]>;
                isDisabled: vue.ComputedRef<boolean>;
                isReadonly: vue.ComputedRef<boolean>;
                isValidating: vue.ShallowRef<boolean>;
                isValid: vue.Ref<boolean | null> & {
                    readonly externalValue: boolean | null;
                };
                items: vue.Ref<{
                    id: number | string;
                    validate: () => Promise<string[]>;
                    reset: () => Promise<void>;
                    resetValidation: () => Promise<void>;
                    vm: vue.Raw<vue.ComponentInternalInstance>;
                    isValid: boolean | null;
                    errorMessages: string[];
                }[]>;
                validate: () => Promise<{
                    valid: boolean;
                    errors: {
                        id: number | string;
                        errorMessages: string[];
                    }[];
                }>;
                reset: () => void;
                resetValidation: () => void;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            errors: vue.Ref<{
                id: number | string;
                errorMessages: string[];
            }[]>;
            isDisabled: vue.ComputedRef<boolean>;
            isReadonly: vue.ComputedRef<boolean>;
            isValidating: vue.ShallowRef<boolean>;
            isValid: vue.Ref<boolean | null> & {
                readonly externalValue: boolean | null;
            };
            items: vue.Ref<{
                id: number | string;
                validate: () => Promise<string[]>;
                reset: () => Promise<void>;
                resetValidation: () => Promise<void>;
                vm: vue.Raw<vue.ComponentInternalInstance>;
                isValid: boolean | null;
                errorMessages: string[];
            }[]>;
            validate: () => Promise<{
                valid: boolean;
                errors: {
                    id: number | string;
                    errorMessages: string[];
                }[];
            }>;
            reset: () => void;
            resetValidation: () => void;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                errors: vue.Ref<{
                    id: number | string;
                    errorMessages: string[];
                }[]>;
                isDisabled: vue.ComputedRef<boolean>;
                isReadonly: vue.ComputedRef<boolean>;
                isValidating: vue.ShallowRef<boolean>;
                isValid: vue.Ref<boolean | null> & {
                    readonly externalValue: boolean | null;
                };
                items: vue.Ref<{
                    id: number | string;
                    validate: () => Promise<string[]>;
                    reset: () => Promise<void>;
                    resetValidation: () => Promise<void>;
                    vm: vue.Raw<vue.ComponentInternalInstance>;
                    isValid: boolean | null;
                    errorMessages: string[];
                }[]>;
                validate: () => Promise<{
                    valid: boolean;
                    errors: {
                        id: number | string;
                        errorMessages: string[];
                    }[];
                }>;
                reset: () => void;
                resetValidation: () => void;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            errors: vue.Ref<{
                id: number | string;
                errorMessages: string[];
            }[]>;
            isDisabled: vue.ComputedRef<boolean>;
            isReadonly: vue.ComputedRef<boolean>;
            isValidating: vue.ShallowRef<boolean>;
            isValid: vue.Ref<boolean | null> & {
                readonly externalValue: boolean | null;
            };
            items: vue.Ref<{
                id: number | string;
                validate: () => Promise<string[]>;
                reset: () => Promise<void>;
                resetValidation: () => Promise<void>;
                vm: vue.Raw<vue.ComponentInternalInstance>;
                isValid: boolean | null;
                errorMessages: string[];
            }[]>;
            validate: () => Promise<{
                valid: boolean;
                errors: {
                    id: number | string;
                    errorMessages: string[];
                }[];
            }>;
            reset: () => void;
            resetValidation: () => void;
        }) => vue.VNodeChild) | undefined;
    } & {
        onSubmit?: ((e: SubmitEventPromise) => any) | undefined;
        "onUpdate:modelValue"?: ((val: boolean | null) => any) | undefined;
    }, {
        errors: vue.Ref<{
            id: number | string;
            errorMessages: string[];
        }[]>;
        isDisabled: vue.ComputedRef<boolean>;
        isReadonly: vue.ComputedRef<boolean>;
        isValidating: vue.ShallowRef<boolean>;
        isValid: vue.Ref<boolean | null> & {
            readonly externalValue: boolean | null;
        };
        items: vue.Ref<{
            id: number | string;
            validate: () => Promise<string[]>;
            reset: () => Promise<void>;
            resetValidation: () => Promise<void>;
            vm: vue.Raw<vue.ComponentInternalInstance>;
            isValid: boolean | null;
            errorMessages: string[];
        }[]>;
        validate: () => Promise<{
            valid: boolean;
            errors: {
                id: number | string;
                errorMessages: string[];
            }[];
        }>;
        reset: () => void;
        resetValidation: () => void;
    } & HTMLFormElement & {
        _allExposed: {
            errors: vue.Ref<{
                id: number | string;
                errorMessages: string[];
            }[]>;
            isDisabled: vue.ComputedRef<boolean>;
            isReadonly: vue.ComputedRef<boolean>;
            isValidating: vue.ShallowRef<boolean>;
            isValid: vue.Ref<boolean | null> & {
                readonly externalValue: boolean | null;
            };
            items: vue.Ref<{
                id: number | string;
                validate: () => Promise<string[]>;
                reset: () => Promise<void>;
                resetValidation: () => Promise<void>;
                vm: vue.Raw<vue.ComponentInternalInstance>;
                isValid: boolean | null;
                errorMessages: string[];
            }[]>;
            validate: () => Promise<{
                valid: boolean;
                errors: {
                    id: number | string;
                    errorMessages: string[];
                }[];
            }>;
            reset: () => void;
            resetValidation: () => void;
        };
    }, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
        'update:modelValue': (val: boolean | null) => true;
        submit: (e: SubmitEventPromise) => true;
    }, vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & {
        style: vue.StyleValue;
        disabled: boolean;
        readonly: boolean;
        modelValue: boolean | null;
        validateOn: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
        fastFail: boolean;
    } & {
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                errors: vue.Ref<{
                    id: number | string;
                    errorMessages: string[];
                }[]>;
                isDisabled: vue.ComputedRef<boolean>;
                isReadonly: vue.ComputedRef<boolean>;
                isValidating: vue.ShallowRef<boolean>;
                isValid: vue.Ref<boolean | null> & {
                    readonly externalValue: boolean | null;
                };
                items: vue.Ref<{
                    id: number | string;
                    validate: () => Promise<string[]>;
                    reset: () => Promise<void>;
                    resetValidation: () => Promise<void>;
                    vm: vue.Raw<vue.ComponentInternalInstance>;
                    isValid: boolean | null;
                    errorMessages: string[];
                }[]>;
                validate: () => Promise<{
                    valid: boolean;
                    errors: {
                        id: number | string;
                        errorMessages: string[];
                    }[];
                }>;
                reset: () => void;
                resetValidation: () => void;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            errors: vue.Ref<{
                id: number | string;
                errorMessages: string[];
            }[]>;
            isDisabled: vue.ComputedRef<boolean>;
            isReadonly: vue.ComputedRef<boolean>;
            isValidating: vue.ShallowRef<boolean>;
            isValid: vue.Ref<boolean | null> & {
                readonly externalValue: boolean | null;
            };
            items: vue.Ref<{
                id: number | string;
                validate: () => Promise<string[]>;
                reset: () => Promise<void>;
                resetValidation: () => Promise<void>;
                vm: vue.Raw<vue.ComponentInternalInstance>;
                isValid: boolean | null;
                errorMessages: string[];
            }[]>;
            validate: () => Promise<{
                valid: boolean;
                errors: {
                    id: number | string;
                    errorMessages: string[];
                }[];
            }>;
            reset: () => void;
            resetValidation: () => void;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                errors: vue.Ref<{
                    id: number | string;
                    errorMessages: string[];
                }[]>;
                isDisabled: vue.ComputedRef<boolean>;
                isReadonly: vue.ComputedRef<boolean>;
                isValidating: vue.ShallowRef<boolean>;
                isValid: vue.Ref<boolean | null> & {
                    readonly externalValue: boolean | null;
                };
                items: vue.Ref<{
                    id: number | string;
                    validate: () => Promise<string[]>;
                    reset: () => Promise<void>;
                    resetValidation: () => Promise<void>;
                    vm: vue.Raw<vue.ComponentInternalInstance>;
                    isValid: boolean | null;
                    errorMessages: string[];
                }[]>;
                validate: () => Promise<{
                    valid: boolean;
                    errors: {
                        id: number | string;
                        errorMessages: string[];
                    }[];
                }>;
                reset: () => void;
                resetValidation: () => void;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            errors: vue.Ref<{
                id: number | string;
                errorMessages: string[];
            }[]>;
            isDisabled: vue.ComputedRef<boolean>;
            isReadonly: vue.ComputedRef<boolean>;
            isValidating: vue.ShallowRef<boolean>;
            isValid: vue.Ref<boolean | null> & {
                readonly externalValue: boolean | null;
            };
            items: vue.Ref<{
                id: number | string;
                validate: () => Promise<string[]>;
                reset: () => Promise<void>;
                resetValidation: () => Promise<void>;
                vm: vue.Raw<vue.ComponentInternalInstance>;
                isValid: boolean | null;
                errorMessages: string[];
            }[]>;
            validate: () => Promise<{
                valid: boolean;
                errors: {
                    id: number | string;
                    errorMessages: string[];
                }[];
            }>;
            reset: () => void;
            resetValidation: () => void;
        }) => vue.VNodeChild) | undefined;
    } & {
        onSubmit?: ((e: SubmitEventPromise) => any) | undefined;
        "onUpdate:modelValue"?: ((val: boolean | null) => any) | undefined;
    }, {
        style: vue.StyleValue;
        disabled: boolean;
        readonly: boolean;
        modelValue: boolean | null;
        validateOn: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
        fastFail: boolean;
    }, true, {}, vue.SlotsType<Partial<{
        default: (arg: {
            errors: vue.Ref<{
                id: number | string;
                errorMessages: string[];
            }[]>;
            isDisabled: vue.ComputedRef<boolean>;
            isReadonly: vue.ComputedRef<boolean>;
            isValidating: vue.ShallowRef<boolean>;
            isValid: vue.Ref<boolean | null> & {
                readonly externalValue: boolean | null;
            };
            items: vue.Ref<{
                id: number | string;
                validate: () => Promise<string[]>;
                reset: () => Promise<void>;
                resetValidation: () => Promise<void>;
                vm: vue.Raw<vue.ComponentInternalInstance>;
                isValid: boolean | null;
                errorMessages: string[];
            }[]>;
            validate: () => Promise<{
                valid: boolean;
                errors: {
                    id: number | string;
                    errorMessages: string[];
                }[];
            }>;
            reset: () => void;
            resetValidation: () => void;
        }) => vue.VNode[];
    }>>, {
        P: {};
        B: {};
        D: {};
        C: {};
        M: {};
        Defaults: {};
    }, {
        style: vue.StyleValue;
        disabled: boolean;
        readonly: boolean;
        modelValue: boolean | null;
        validateOn: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
        fastFail: boolean;
    } & {
        class?: any;
    } & {
        $children?: vue.VNodeChild | {
            default?: ((arg: {
                errors: vue.Ref<{
                    id: number | string;
                    errorMessages: string[];
                }[]>;
                isDisabled: vue.ComputedRef<boolean>;
                isReadonly: vue.ComputedRef<boolean>;
                isValidating: vue.ShallowRef<boolean>;
                isValid: vue.Ref<boolean | null> & {
                    readonly externalValue: boolean | null;
                };
                items: vue.Ref<{
                    id: number | string;
                    validate: () => Promise<string[]>;
                    reset: () => Promise<void>;
                    resetValidation: () => Promise<void>;
                    vm: vue.Raw<vue.ComponentInternalInstance>;
                    isValid: boolean | null;
                    errorMessages: string[];
                }[]>;
                validate: () => Promise<{
                    valid: boolean;
                    errors: {
                        id: number | string;
                        errorMessages: string[];
                    }[];
                }>;
                reset: () => void;
                resetValidation: () => void;
            }) => vue.VNodeChild) | undefined;
        } | ((arg: {
            errors: vue.Ref<{
                id: number | string;
                errorMessages: string[];
            }[]>;
            isDisabled: vue.ComputedRef<boolean>;
            isReadonly: vue.ComputedRef<boolean>;
            isValidating: vue.ShallowRef<boolean>;
            isValid: vue.Ref<boolean | null> & {
                readonly externalValue: boolean | null;
            };
            items: vue.Ref<{
                id: number | string;
                validate: () => Promise<string[]>;
                reset: () => Promise<void>;
                resetValidation: () => Promise<void>;
                vm: vue.Raw<vue.ComponentInternalInstance>;
                isValid: boolean | null;
                errorMessages: string[];
            }[]>;
            validate: () => Promise<{
                valid: boolean;
                errors: {
                    id: number | string;
                    errorMessages: string[];
                }[];
            }>;
            reset: () => void;
            resetValidation: () => void;
        }) => vue.VNodeChild);
        'v-slots'?: {
            default?: false | ((arg: {
                errors: vue.Ref<{
                    id: number | string;
                    errorMessages: string[];
                }[]>;
                isDisabled: vue.ComputedRef<boolean>;
                isReadonly: vue.ComputedRef<boolean>;
                isValidating: vue.ShallowRef<boolean>;
                isValid: vue.Ref<boolean | null> & {
                    readonly externalValue: boolean | null;
                };
                items: vue.Ref<{
                    id: number | string;
                    validate: () => Promise<string[]>;
                    reset: () => Promise<void>;
                    resetValidation: () => Promise<void>;
                    vm: vue.Raw<vue.ComponentInternalInstance>;
                    isValid: boolean | null;
                    errorMessages: string[];
                }[]>;
                validate: () => Promise<{
                    valid: boolean;
                    errors: {
                        id: number | string;
                        errorMessages: string[];
                    }[];
                }>;
                reset: () => void;
                resetValidation: () => void;
            }) => vue.VNodeChild) | undefined;
        } | undefined;
    } & {
        "v-slot:default"?: false | ((arg: {
            errors: vue.Ref<{
                id: number | string;
                errorMessages: string[];
            }[]>;
            isDisabled: vue.ComputedRef<boolean>;
            isReadonly: vue.ComputedRef<boolean>;
            isValidating: vue.ShallowRef<boolean>;
            isValid: vue.Ref<boolean | null> & {
                readonly externalValue: boolean | null;
            };
            items: vue.Ref<{
                id: number | string;
                validate: () => Promise<string[]>;
                reset: () => Promise<void>;
                resetValidation: () => Promise<void>;
                vm: vue.Raw<vue.ComponentInternalInstance>;
                isValid: boolean | null;
                errorMessages: string[];
            }[]>;
            validate: () => Promise<{
                valid: boolean;
                errors: {
                    id: number | string;
                    errorMessages: string[];
                }[];
            }>;
            reset: () => void;
            resetValidation: () => void;
        }) => vue.VNodeChild) | undefined;
    } & {
        onSubmit?: ((e: SubmitEventPromise) => any) | undefined;
        "onUpdate:modelValue"?: ((val: boolean | null) => any) | undefined;
    }, {
        errors: vue.Ref<{
            id: number | string;
            errorMessages: string[];
        }[]>;
        isDisabled: vue.ComputedRef<boolean>;
        isReadonly: vue.ComputedRef<boolean>;
        isValidating: vue.ShallowRef<boolean>;
        isValid: vue.Ref<boolean | null> & {
            readonly externalValue: boolean | null;
        };
        items: vue.Ref<{
            id: number | string;
            validate: () => Promise<string[]>;
            reset: () => Promise<void>;
            resetValidation: () => Promise<void>;
            vm: vue.Raw<vue.ComponentInternalInstance>;
            isValid: boolean | null;
            errorMessages: string[];
        }[]>;
        validate: () => Promise<{
            valid: boolean;
            errors: {
                id: number | string;
                errorMessages: string[];
            }[];
        }>;
        reset: () => void;
        resetValidation: () => void;
    } & HTMLFormElement & {
        _allExposed: {
            errors: vue.Ref<{
                id: number | string;
                errorMessages: string[];
            }[]>;
            isDisabled: vue.ComputedRef<boolean>;
            isReadonly: vue.ComputedRef<boolean>;
            isValidating: vue.ShallowRef<boolean>;
            isValid: vue.Ref<boolean | null> & {
                readonly externalValue: boolean | null;
            };
            items: vue.Ref<{
                id: number | string;
                validate: () => Promise<string[]>;
                reset: () => Promise<void>;
                resetValidation: () => Promise<void>;
                vm: vue.Raw<vue.ComponentInternalInstance>;
                isValid: boolean | null;
                errorMessages: string[];
            }[]>;
            validate: () => Promise<{
                valid: boolean;
                errors: {
                    id: number | string;
                    errorMessages: string[];
                }[];
            }>;
            reset: () => void;
            resetValidation: () => void;
        };
    }, {}, {}, {}, {
        style: vue.StyleValue;
        disabled: boolean;
        readonly: boolean;
        modelValue: boolean | null;
        validateOn: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
        fastFail: boolean;
    }>;
    __isFragment?: never;
    __isTeleport?: never;
    __isSuspense?: never;
} & vue.ComponentOptionsBase<{
    style: vue.StyleValue;
    disabled: boolean;
    readonly: boolean;
    modelValue: boolean | null;
    validateOn: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
    fastFail: boolean;
} & {
    class?: any;
} & {
    $children?: vue.VNodeChild | {
        default?: ((arg: {
            errors: vue.Ref<{
                id: number | string;
                errorMessages: string[];
            }[]>;
            isDisabled: vue.ComputedRef<boolean>;
            isReadonly: vue.ComputedRef<boolean>;
            isValidating: vue.ShallowRef<boolean>;
            isValid: vue.Ref<boolean | null> & {
                readonly externalValue: boolean | null;
            };
            items: vue.Ref<{
                id: number | string;
                validate: () => Promise<string[]>;
                reset: () => Promise<void>;
                resetValidation: () => Promise<void>;
                vm: vue.Raw<vue.ComponentInternalInstance>;
                isValid: boolean | null;
                errorMessages: string[];
            }[]>;
            validate: () => Promise<{
                valid: boolean;
                errors: {
                    id: number | string;
                    errorMessages: string[];
                }[];
            }>;
            reset: () => void;
            resetValidation: () => void;
        }) => vue.VNodeChild) | undefined;
    } | ((arg: {
        errors: vue.Ref<{
            id: number | string;
            errorMessages: string[];
        }[]>;
        isDisabled: vue.ComputedRef<boolean>;
        isReadonly: vue.ComputedRef<boolean>;
        isValidating: vue.ShallowRef<boolean>;
        isValid: vue.Ref<boolean | null> & {
            readonly externalValue: boolean | null;
        };
        items: vue.Ref<{
            id: number | string;
            validate: () => Promise<string[]>;
            reset: () => Promise<void>;
            resetValidation: () => Promise<void>;
            vm: vue.Raw<vue.ComponentInternalInstance>;
            isValid: boolean | null;
            errorMessages: string[];
        }[]>;
        validate: () => Promise<{
            valid: boolean;
            errors: {
                id: number | string;
                errorMessages: string[];
            }[];
        }>;
        reset: () => void;
        resetValidation: () => void;
    }) => vue.VNodeChild);
    'v-slots'?: {
        default?: false | ((arg: {
            errors: vue.Ref<{
                id: number | string;
                errorMessages: string[];
            }[]>;
            isDisabled: vue.ComputedRef<boolean>;
            isReadonly: vue.ComputedRef<boolean>;
            isValidating: vue.ShallowRef<boolean>;
            isValid: vue.Ref<boolean | null> & {
                readonly externalValue: boolean | null;
            };
            items: vue.Ref<{
                id: number | string;
                validate: () => Promise<string[]>;
                reset: () => Promise<void>;
                resetValidation: () => Promise<void>;
                vm: vue.Raw<vue.ComponentInternalInstance>;
                isValid: boolean | null;
                errorMessages: string[];
            }[]>;
            validate: () => Promise<{
                valid: boolean;
                errors: {
                    id: number | string;
                    errorMessages: string[];
                }[];
            }>;
            reset: () => void;
            resetValidation: () => void;
        }) => vue.VNodeChild) | undefined;
    } | undefined;
} & {
    "v-slot:default"?: false | ((arg: {
        errors: vue.Ref<{
            id: number | string;
            errorMessages: string[];
        }[]>;
        isDisabled: vue.ComputedRef<boolean>;
        isReadonly: vue.ComputedRef<boolean>;
        isValidating: vue.ShallowRef<boolean>;
        isValid: vue.Ref<boolean | null> & {
            readonly externalValue: boolean | null;
        };
        items: vue.Ref<{
            id: number | string;
            validate: () => Promise<string[]>;
            reset: () => Promise<void>;
            resetValidation: () => Promise<void>;
            vm: vue.Raw<vue.ComponentInternalInstance>;
            isValid: boolean | null;
            errorMessages: string[];
        }[]>;
        validate: () => Promise<{
            valid: boolean;
            errors: {
                id: number | string;
                errorMessages: string[];
            }[];
        }>;
        reset: () => void;
        resetValidation: () => void;
    }) => vue.VNodeChild) | undefined;
} & {
    onSubmit?: ((e: SubmitEventPromise) => any) | undefined;
    "onUpdate:modelValue"?: ((val: boolean | null) => any) | undefined;
}, {
    errors: vue.Ref<{
        id: number | string;
        errorMessages: string[];
    }[]>;
    isDisabled: vue.ComputedRef<boolean>;
    isReadonly: vue.ComputedRef<boolean>;
    isValidating: vue.ShallowRef<boolean>;
    isValid: vue.Ref<boolean | null> & {
        readonly externalValue: boolean | null;
    };
    items: vue.Ref<{
        id: number | string;
        validate: () => Promise<string[]>;
        reset: () => Promise<void>;
        resetValidation: () => Promise<void>;
        vm: vue.Raw<vue.ComponentInternalInstance>;
        isValid: boolean | null;
        errorMessages: string[];
    }[]>;
    validate: () => Promise<{
        valid: boolean;
        errors: {
            id: number | string;
            errorMessages: string[];
        }[];
    }>;
    reset: () => void;
    resetValidation: () => void;
} & HTMLFormElement & {
    _allExposed: {
        errors: vue.Ref<{
            id: number | string;
            errorMessages: string[];
        }[]>;
        isDisabled: vue.ComputedRef<boolean>;
        isReadonly: vue.ComputedRef<boolean>;
        isValidating: vue.ShallowRef<boolean>;
        isValid: vue.Ref<boolean | null> & {
            readonly externalValue: boolean | null;
        };
        items: vue.Ref<{
            id: number | string;
            validate: () => Promise<string[]>;
            reset: () => Promise<void>;
            resetValidation: () => Promise<void>;
            vm: vue.Raw<vue.ComponentInternalInstance>;
            isValid: boolean | null;
            errorMessages: string[];
        }[]>;
        validate: () => Promise<{
            valid: boolean;
            errors: {
                id: number | string;
                errorMessages: string[];
            }[];
        }>;
        reset: () => void;
        resetValidation: () => void;
    };
}, unknown, {}, {}, vue.ComponentOptionsMixin, vue.ComponentOptionsMixin, {
    'update:modelValue': (val: boolean | null) => true;
    submit: (e: SubmitEventPromise) => true;
}, string, {
    style: vue.StyleValue;
    disabled: boolean;
    readonly: boolean;
    modelValue: boolean | null;
    validateOn: ("eager" | "lazy" | ("input" | "blur" | "submit" | "invalid-input") | "input lazy" | "blur lazy" | "submit lazy" | "invalid-input lazy" | "input eager" | "blur eager" | "submit eager" | "invalid-input eager" | "lazy input" | "lazy blur" | "lazy submit" | "lazy invalid-input" | "eager input" | "eager blur" | "eager submit" | "eager invalid-input") | undefined;
    fastFail: boolean;
}, {}, string, vue.SlotsType<Partial<{
    default: (arg: {
        errors: vue.Ref<{
            id: number | string;
            errorMessages: string[];
        }[]>;
        isDisabled: vue.ComputedRef<boolean>;
        isReadonly: vue.ComputedRef<boolean>;
        isValidating: vue.ShallowRef<boolean>;
        isValid: vue.Ref<boolean | null> & {
            readonly externalValue: boolean | null;
        };
        items: vue.Ref<{
            id: number | string;
            validate: () => Promise<string[]>;
            reset: () => Promise<void>;
            resetValidation: () => Promise<void>;
            vm: vue.Raw<vue.ComponentInternalInstance>;
            isValid: boolean | null;
            errorMessages: string[];
        }[]>;
        validate: () => Promise<{
            valid: boolean;
            errors: {
                id: number | string;
                errorMessages: string[];
            }[];
        }>;
        reset: () => void;
        resetValidation: () => void;
    }) => vue.VNode[];
}>>> & vue.VNodeProps & vue.AllowedComponentProps & vue.ComponentCustomProps & FilterPropsOptions<{
    disabled: BooleanConstructor;
    fastFail: BooleanConstructor;
    readonly: BooleanConstructor;
    modelValue: {
        type: vue.PropType<boolean | null>;
        default: null;
    };
    validateOn: {
        type: vue.PropType<FormProps["validateOn"]>;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
}, vue.ExtractPropTypes<{
    disabled: BooleanConstructor;
    fastFail: BooleanConstructor;
    readonly: BooleanConstructor;
    modelValue: {
        type: vue.PropType<boolean | null>;
        default: null;
    };
    validateOn: {
        type: vue.PropType<FormProps["validateOn"]>;
        default: string;
    };
    class: vue.PropType<ClassValue>;
    style: {
        type: vue.PropType<vue.StyleValue>;
        default: null;
    };
}>>;
type VForm = InstanceType<typeof VForm>;

export { VForm };
