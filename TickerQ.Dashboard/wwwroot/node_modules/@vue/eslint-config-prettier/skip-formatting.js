const eslintConfigPrettier = require("eslint-config-prettier");

// Note that we are not extending from the recommended config.
// Because that would be an overkill:
// - since we are not formatting, the plugin isn't needed;
// - and without the `prettier/prettier` rule, we don't have to disable the conflicting `arrow-body-style` and `prefer-arrow-callback` rules.

/** @type {import('eslint').Linter.Config} */
module.exports = {
  ...eslintConfigPrettier,
  rules: {
    ...eslintConfigPrettier.rules,
    "prettier/prettier": "off",
  },
};
