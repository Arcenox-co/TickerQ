/* eslint-disable no-console */

// Utilities
import { warn } from 'vue';
export function consoleWarn(message) {
  warn(`Vuetify: ${message}`);
}
export function consoleError(message) {
  warn(`Vuetify error: ${message}`);
}
export function deprecate(original, replacement) {
  replacement = Array.isArray(replacement) ? replacement.slice(0, -1).map(s => `'${s}'`).join(', ') + ` or '${replacement.at(-1)}'` : `'${replacement}'`;
  warn(`[Vuetify UPGRADE] '${original}' is deprecated, use ${replacement} instead.`);
}
export function breaking(original, replacement) {
  // warn(`[Vuetify BREAKING] '${original}' has been removed, use '${replacement}' instead. For more information, see the upgrade guide https://github.com/vuetifyjs/vuetify/releases/tag/v2.0.0#user-content-upgrade-guide`)
}
export function removed(original) {
  // warn(`[Vuetify REMOVED] '${original}' has been removed. You can safely omit it.`)
}
//# sourceMappingURL=console.mjs.map