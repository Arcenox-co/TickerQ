# @vue/eslint-config-prettier

> A wrapper around `eslint-config-prettier` designed to work more intuitively with `create-vue` setups.

This config is specifically designed to be used by `create-vue` setups
and is not meant for outside use (it can be used but some adaptations
on the user side might be needed - for details see the config file).

A part of its design is that this config may implicitly depend on
other parts of `create-vue` setups.

## Installation

```sh
npm add --dev @vue/eslint-config-prettier
```

Please also make sure that you have `prettier` and `eslint` installed.

## Usage

> Note: the current version doesn't support the legacy `.eslintrc*` configuration format. For that you need to use version 9 or earlier. See the [corresponding README](https://www.npmjs.com/package/@vue/eslint-config-prettier/v/legacy-eslintrc) for more usage instructions.

Import `@vue/eslint-config-prettier` in `eslint.config.js` (or `eslint.config.mjs`), and put it in the configuration array – **after** other configs that you want to override.

```js
import someConfig from "some-other-config-you-use";
import prettierConfig from "@vue/eslint-config-prettier";

export default [someConfig, prettierConfig];
```

Make sure to put it **last**, so it gets the chance to override other configs.

This configuration is the most straightward way to use ESLint with Prettier.

It disables all rules that are unnecessary or might conflict with Prettier.
It also enables the `eslint-plugin-prettier` plugin, which runs Prettier as an ESLint rule and reports differences as individual ESLint issues.

By default all formatting issues are reported as warnings, and will be automatically fixed during `eslint --fix`.

## Use Separate Commands for Linting and Formatting

While the above setup is very straightforward, it is not necessarily the best way.

Running prettier inside the linter slows down the linting process, might clutter the editor with annoying warnings, and adds one layer of indirection where things may break.
[Prettier's official documentation](https://prettier.io/docs/en/integrating-with-linters.html) recommends using separate commands for linting and formatting, i.e., Prettier for code formatting concerns and ESLint for code-quality concerns.

So we offered an additional ruleset to support this workflow:

```js
import someConfig from "some-other-config-you-use";
import skipFormattingConfig from "@vue/eslint-config-prettier/skip-formatting";

export default [someConfig, skipFormattingConfig];
```

Formatting issues won't be reported with this config.

You can run `prettier --check .` separately to check for formatting issues, or `prettier --write .` to fix them.

## Further Reading

The default config is based on the recommended configuration of [`eslint-plugin-prettier`](https://github.com/prettier/eslint-plugin-prettier/#recommended-configuration), which also depends on [`eslint-config-prettier`](https://github.com/prettier/eslint-config-prettier). Please refer to their corresponding documentations for more implementation details.
