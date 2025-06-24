/* eslint-disable local-rules/sort-imports */
// Components
import * as components from "./allComponents.mjs";
import * as directives from "../directives/index.mjs";
import { createVuetify as _createVuetify } from "../framework.mjs"; // Types
export * from "../entry-bundler.mjs";
export { components };
export const createVuetify = function () {
  let options = arguments.length > 0 && arguments[0] !== undefined ? arguments[0] : {};
  return _createVuetify({
    components,
    directives,
    ...options
  });
};
//# sourceMappingURL=entry-bundler.mjs.map