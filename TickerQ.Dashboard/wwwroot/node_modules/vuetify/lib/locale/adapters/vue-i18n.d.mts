import { I18n, useI18n } from 'vue-i18n';
import { Ref } from 'vue';

interface LocaleMessages {
    [key: string]: LocaleMessages | string;
}
interface LocaleOptions {
    messages?: LocaleMessages;
    locale?: string;
    fallback?: string;
    adapter?: LocaleInstance;
}
interface LocaleInstance {
    name: string;
    messages: Ref<LocaleMessages>;
    current: Ref<string>;
    fallback: Ref<string>;
    t: (key: string, ...params: unknown[]) => string;
    n: (value: number) => string;
    provide: (props: LocaleOptions) => LocaleInstance;
}

type VueI18nAdapterParams = {
    i18n: I18n<any, {}, {}, string, false>;
    useI18n: typeof useI18n;
};
declare function createVueI18nAdapter({ i18n, useI18n }: VueI18nAdapterParams): LocaleInstance;

export { createVueI18nAdapter };
