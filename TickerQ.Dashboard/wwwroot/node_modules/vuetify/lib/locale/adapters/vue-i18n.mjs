// Composables
import { useProxiedModel } from "../../composables/proxiedModel.mjs"; // Utilities
import { watch } from 'vue';

// Types

function useProvided(props, prop, provided) {
  const internal = useProxiedModel(props, prop);
  internal.value = props[prop] ?? provided.value;
  watch(provided, v => {
    if (props[prop] == null) {
      internal.value = v;
    }
  });
  return internal;
}
function createProvideFunction(data) {
  return props => {
    const current = useProvided(props, 'locale', data.current);
    const fallback = useProvided(props, 'fallback', data.fallback);
    const messages = useProvided(props, 'messages', data.messages);
    const i18n = data.useI18n({
      locale: current.value,
      fallbackLocale: fallback.value,
      messages: messages.value,
      useScope: 'local',
      legacy: false,
      inheritLocale: false
    });
    watch(current, v => {
      i18n.locale.value = v;
    });
    return {
      name: 'vue-i18n',
      current,
      fallback,
      messages,
      t: function (key) {
        for (var _len = arguments.length, params = new Array(_len > 1 ? _len - 1 : 0), _key = 1; _key < _len; _key++) {
          params[_key - 1] = arguments[_key];
        }
        return i18n.t(key, params);
      },
      n: i18n.n,
      provide: createProvideFunction({
        current,
        fallback,
        messages,
        useI18n: data.useI18n
      })
    };
  };
}
export function createVueI18nAdapter(_ref) {
  let {
    i18n,
    useI18n
  } = _ref;
  const current = i18n.global.locale;
  const fallback = i18n.global.fallbackLocale;
  const messages = i18n.global.messages;
  return {
    name: 'vue-i18n',
    current,
    fallback,
    messages,
    // @ts-expect-error Type instantiation is excessively deep and possibly infinite
    t: function (key) {
      for (var _len2 = arguments.length, params = new Array(_len2 > 1 ? _len2 - 1 : 0), _key2 = 1; _key2 < _len2; _key2++) {
        params[_key2 - 1] = arguments[_key2];
      }
      return i18n.global.t(key, params);
    },
    n: i18n.global.n,
    provide: createProvideFunction({
      current,
      fallback,
      messages,
      useI18n
    })
  };
}
//# sourceMappingURL=vue-i18n.mjs.map