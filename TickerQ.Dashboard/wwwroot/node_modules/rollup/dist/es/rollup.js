/*
  @license
	Rollup.js v4.30.1
	Tue, 07 Jan 2025 10:35:22 GMT - commit 94917087deb9103fbf605c68670ceb3e71a67bf7

	https://github.com/rollup/rollup

	Released under the MIT License.
*/
export { version as VERSION, defineConfig, rollup, watch } from './shared/node-entry.js';
import './shared/parseAst.js';
import '../native.js';
import 'node:path';
import 'path';
import 'node:process';
import 'node:perf_hooks';
import 'node:fs/promises';
