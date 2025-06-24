export function pad(n) {
  let length = arguments.length > 1 && arguments[1] !== undefined ? arguments[1] : 2;
  return String(n).padStart(length, '0');
}
//# sourceMappingURL=util.mjs.map