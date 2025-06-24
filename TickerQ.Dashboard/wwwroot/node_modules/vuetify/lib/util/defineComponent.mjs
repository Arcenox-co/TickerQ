// Composables
import { injectDefaults, internalUseDefaults } from "../composables/defaults.mjs"; // Utilities
import { defineComponent as _defineComponent // eslint-disable-line no-restricted-imports
} from 'vue';
import { consoleWarn } from "./console.mjs";
import { pick } from "./helpers.mjs";
import { propsFactory } from "./propsFactory.mjs"; // Types
// No props
// Object Props
// Implementation
export function defineComponent(options) {
  options._setup = options._setup ?? options.setup;
  if (!options.name) {
    consoleWarn('The component is missing an explicit name, unable to generate default prop value');
    return options;
  }
  if (options._setup) {
    options.props = propsFactory(options.props ?? {}, options.name)();
    const propKeys = Object.keys(options.props).filter(key => key !== 'class' && key !== 'style');
    options.filterProps = function filterProps(props) {
      return pick(props, propKeys);
    };
    options.props._as = String;
    options.setup = function setup(props, ctx) {
      const defaults = injectDefaults();

      // Skip props proxy if defaults are not provided
      if (!defaults.value) return options._setup(props, ctx);
      const {
        props: _props,
        provideSubDefaults
      } = internalUseDefaults(props, props._as ?? options.name, defaults);
      const setupBindings = options._setup(_props, ctx);
      provideSubDefaults();
      return setupBindings;
    };
  }
  return options;
}

// No argument - simple default slot

// Generic constructor argument - generic props and slots

// Slots argument - simple slots

// Implementation
export function genericComponent() {
  let exposeDefaults = arguments.length > 0 && arguments[0] !== undefined ? arguments[0] : true;
  return options => (exposeDefaults ? defineComponent : _defineComponent)(options);
}
export function defineFunctionalComponent(props, render) {
  render.props = props;
  return render;
}

// Adds a filterProps method to the component options

// https://github.com/vuejs/core/pull/10557

// not a vue Component
//# sourceMappingURL=defineComponent.mjs.map