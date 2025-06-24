// Utilities
import { defer, propsFactory } from "../util/index.mjs"; // Types
// Composables
export const makeDelayProps = propsFactory({
  closeDelay: [Number, String],
  openDelay: [Number, String]
}, 'delay');
export function useDelay(props, cb) {
  let clearDelay = () => {};
  function runDelay(isOpening) {
    clearDelay?.();
    const delay = Number(isOpening ? props.openDelay : props.closeDelay);
    return new Promise(resolve => {
      clearDelay = defer(delay, () => {
        cb?.(isOpening);
        resolve(isOpening);
      });
    });
  }
  function runOpenDelay() {
    return runDelay(true);
  }
  function runCloseDelay() {
    return runDelay(false);
  }
  return {
    clearDelay,
    runOpenDelay,
    runCloseDelay
  };
}
//# sourceMappingURL=delay.mjs.map