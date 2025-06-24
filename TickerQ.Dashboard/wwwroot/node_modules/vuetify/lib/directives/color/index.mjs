// @ts-nocheck
/* eslint-disable */
// Utilities
import { classToHex, isCssColor, parseGradient } from "../../util/colorUtils.mjs";
import colors from "../../util/colors.mjs"; // Types
function setTextColor(el, color, currentTheme) {
  const cssColor = !isCssColor(color) ? classToHex(color, colors, currentTheme) : color;
  el.style.color = cssColor;
  el.style.caretColor = cssColor;
}
function setBackgroundColor(el, color, currentTheme) {
  const cssColor = !isCssColor(color) ? classToHex(color, colors, currentTheme) : color;
  el.style.backgroundColor = cssColor;
  el.style.borderColor = cssColor;
}
function setBorderColor(el, color, currentTheme, modifiers) {
  const cssColor = !isCssColor(color) ? classToHex(color, colors, currentTheme) : color;
  if (!modifiers || !Object.keys(modifiers).length) {
    el.style.borderColor = cssColor;
    return;
  }
  if (modifiers.top) el.style.borderTopColor = cssColor;
  if (modifiers.right) el.style.borderRightColor = cssColor;
  if (modifiers.bottom) el.style.borderBottomColor = cssColor;
  if (modifiers.left) el.style.borderLeftColor = cssColor;
}
function setGradientColor(el, gradient, currentTheme) {
  el.style.backgroundImage = `linear-gradient(${parseGradient(gradient, colors, currentTheme)})`;
}
function updateColor(el, binding, node) {
  const currentTheme = node.context.$vuetify.theme.currentTheme;
  if (binding.arg === undefined) {
    setBackgroundColor(el, binding.value, currentTheme);
  } else if (binding.arg === 'text') {
    setTextColor(el, binding.value, currentTheme);
  } else if (binding.arg === 'border') {
    setBorderColor(el, binding.value, currentTheme, binding.modifiers);
  } else if (binding.arg === 'gradient') {
    setGradientColor(el, binding.value, currentTheme);
  }
}
function update(el, binding, node) {
  if (binding.value === binding.oldValue) return;
  updateColor(el, binding, node);
}
export const Color = {
  bind: updateColor,
  update
};
export default Color;
//# sourceMappingURL=index.mjs.map