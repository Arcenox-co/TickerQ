// Utilities
import { isOn } from "./helpers.mjs";
export function getPrefixedEventHandlers(attrs, suffix, getData) {
  return Object.keys(attrs).filter(key => isOn(key) && key.endsWith(suffix)).reduce((acc, key) => {
    acc[key.slice(0, -suffix.length)] = event => attrs[key](event, getData(event));
    return acc;
  }, {});
}
//# sourceMappingURL=events.mjs.map