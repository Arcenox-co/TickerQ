module.exports = {
  '#': {pattern: /\d/},
  'X': {pattern: /[0-9a-zA-Z]/},
  'S': {pattern: /[a-zA-Z]/},
  'A': {pattern: /[a-zA-Z]/, transform: v => v.toLocaleUpperCase()},
  'a': {pattern: /[a-zA-Z]/, transform: v => v.toLocaleLowerCase()},
  '!': {escape: true}
}

// https://github.com/fernandofleury/vanilla-masker/blob/master/lib/vanilla-masker.js
// DIGIT = "9",
// ALPHA = "A",
// ALPHANUM = "S"

// https://github.com/niksmr/vue-masked-input
// 1 - number
// a - letter
// A - letter, forced to upper case when entered
// * - alphanumeric
// # - alphanumeric, forced to upper case when entered
// + - any character

// https://github.com/probil/v-mask
// #	Number (0-9)
// A	Letter in any case (a-z,A-Z)
// N	Number or letter
// X	Any symbol

// https://github.com/igorescobar/jQuery-Mask-Plugin/blob/master/src/jquery.mask.js#L518
// '0': {pattern: /\d/},
// '9': {pattern: /\d/, optional: true},
// '#': {pattern: /\d/, recursive: true},
// 'A': {pattern: /[a-zA-Z0-9]/},
// 'S': {pattern: /[a-zA-Z]/}

// https://github.com/the-darc/string-mask
// 0	Any numbers
// 9	Any numbers (Optional)
// #	Any numbers (recursive)
// A	Any alphanumeric character
// a	Any alphanumeric character (Optional) Not implemented yet
// S	Any letter
// U	Any letter (All lower case character will be mapped to uppercase)
// L	Any letter (All upper case character will be mapped to lowercase)
// $	Escape character, used to escape any of the special formatting characters.
