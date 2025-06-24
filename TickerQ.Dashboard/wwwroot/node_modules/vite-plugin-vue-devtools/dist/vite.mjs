import fs$4 from 'node:fs';
import path$1, { dirname, resolve as resolve$1 } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createViteServerRpc } from '@vue/devtools-core';
import { getViteRpcServer, setViteServerContext } from '@vue/devtools-kit';
import sirv from 'sirv';
import { normalizePath } from 'vite';
import Inspect from 'vite-plugin-inspect';
import VueInspector from 'vite-plugin-vue-inspector';
import fsp from 'node:fs/promises';
import require$$0 from 'os';
import require$$0$1 from 'path';
import require$$0$2 from 'util';
import require$$0$3 from 'stream';
import require$$0$5 from 'events';
import require$$0$4 from 'fs';

let enabled = true;
// Support both browser and node environments
const globalVar = typeof self !== 'undefined'
    ? self
    : typeof window !== 'undefined'
        ? window
        : typeof global !== 'undefined'
            ? global
            : {};
/**
 * Detect how much colors the current terminal supports
 */
let supportLevel = 0 /* none */;
if (globalVar.process && globalVar.process.env && globalVar.process.stdout) {
    const { FORCE_COLOR, NODE_DISABLE_COLORS, NO_COLOR, TERM, COLORTERM } = globalVar.process.env;
    if (NODE_DISABLE_COLORS || NO_COLOR || FORCE_COLOR === '0') {
        enabled = false;
    }
    else if (FORCE_COLOR === '1' ||
        FORCE_COLOR === '2' ||
        FORCE_COLOR === '3') {
        enabled = true;
    }
    else if (TERM === 'dumb') {
        enabled = false;
    }
    else if ('CI' in globalVar.process.env &&
        [
            'TRAVIS',
            'CIRCLECI',
            'APPVEYOR',
            'GITLAB_CI',
            'GITHUB_ACTIONS',
            'BUILDKITE',
            'DRONE',
        ].some(vendor => vendor in globalVar.process.env)) {
        enabled = true;
    }
    else {
        enabled = process.stdout.isTTY;
    }
    if (enabled) {
        // Windows supports 24bit True Colors since Windows 10 revision #14931,
        // see https://devblogs.microsoft.com/commandline/24-bit-color-in-the-windows-console/
        if (process.platform === 'win32') {
            supportLevel = 3 /* trueColor */;
        }
        else {
            if (COLORTERM && (COLORTERM === 'truecolor' || COLORTERM === '24bit')) {
                supportLevel = 3 /* trueColor */;
            }
            else if (TERM && (TERM.endsWith('-256color') || TERM.endsWith('256'))) {
                supportLevel = 2 /* ansi256 */;
            }
            else {
                supportLevel = 1 /* ansi */;
            }
        }
    }
}
let options = {
    enabled,
    supportLevel,
};
function kolorist(start, end, level = 1 /* ansi */) {
    const open = `\x1b[${start}m`;
    const close = `\x1b[${end}m`;
    const regex = new RegExp(`\\x1b\\[${end}m`, 'g');
    return (str) => {
        return options.enabled && options.supportLevel >= level
            ? open + ('' + str).replace(regex, open) + close
            : '' + str;
    };
}
const bold = kolorist(1, 22);
const dim = kolorist(2, 22);
const green = kolorist(32, 39);
const yellow = kolorist(33, 39);
const cyan = kolorist(36, 39);

const DIR_DIST = typeof __dirname !== "undefined" ? __dirname : dirname(fileURLToPath(import.meta.url));
const DIR_CLIENT = resolve$1(DIR_DIST, "../client");

var commonjsGlobal = typeof globalThis !== 'undefined' ? globalThis : typeof window !== 'undefined' ? window : typeof global !== 'undefined' ? global : typeof self !== 'undefined' ? self : {};

function getDefaultExportFromCjs (x) {
	return x && x.__esModule && Object.prototype.hasOwnProperty.call(x, 'default') ? x['default'] : x;
}

var tasks = {};

var utils$3 = {};

var array = {};

var hasRequiredArray;

function requireArray () {
	if (hasRequiredArray) return array;
	hasRequiredArray = 1;
	Object.defineProperty(array, "__esModule", { value: true });
	array.splitWhen = array.flatten = void 0;
	function flatten(items) {
	    return items.reduce((collection, item) => [].concat(collection, item), []);
	}
	array.flatten = flatten;
	function splitWhen(items, predicate) {
	    const result = [[]];
	    let groupIndex = 0;
	    for (const item of items) {
	        if (predicate(item)) {
	            groupIndex++;
	            result[groupIndex] = [];
	        }
	        else {
	            result[groupIndex].push(item);
	        }
	    }
	    return result;
	}
	array.splitWhen = splitWhen;
	return array;
}

var errno = {};

var hasRequiredErrno;

function requireErrno () {
	if (hasRequiredErrno) return errno;
	hasRequiredErrno = 1;
	Object.defineProperty(errno, "__esModule", { value: true });
	errno.isEnoentCodeError = void 0;
	function isEnoentCodeError(error) {
	    return error.code === 'ENOENT';
	}
	errno.isEnoentCodeError = isEnoentCodeError;
	return errno;
}

var fs$3 = {};

var hasRequiredFs$3;

function requireFs$3 () {
	if (hasRequiredFs$3) return fs$3;
	hasRequiredFs$3 = 1;
	Object.defineProperty(fs$3, "__esModule", { value: true });
	fs$3.createDirentFromStats = void 0;
	class DirentFromStats {
	    constructor(name, stats) {
	        this.name = name;
	        this.isBlockDevice = stats.isBlockDevice.bind(stats);
	        this.isCharacterDevice = stats.isCharacterDevice.bind(stats);
	        this.isDirectory = stats.isDirectory.bind(stats);
	        this.isFIFO = stats.isFIFO.bind(stats);
	        this.isFile = stats.isFile.bind(stats);
	        this.isSocket = stats.isSocket.bind(stats);
	        this.isSymbolicLink = stats.isSymbolicLink.bind(stats);
	    }
	}
	function createDirentFromStats(name, stats) {
	    return new DirentFromStats(name, stats);
	}
	fs$3.createDirentFromStats = createDirentFromStats;
	return fs$3;
}

var path = {};

var hasRequiredPath;

function requirePath () {
	if (hasRequiredPath) return path;
	hasRequiredPath = 1;
	Object.defineProperty(path, "__esModule", { value: true });
	path.convertPosixPathToPattern = path.convertWindowsPathToPattern = path.convertPathToPattern = path.escapePosixPath = path.escapeWindowsPath = path.escape = path.removeLeadingDotSegment = path.makeAbsolute = path.unixify = void 0;
	const os = require$$0;
	const path$1 = require$$0$1;
	const IS_WINDOWS_PLATFORM = os.platform() === 'win32';
	const LEADING_DOT_SEGMENT_CHARACTERS_COUNT = 2; // ./ or .\\
	/**
	 * All non-escaped special characters.
	 * Posix: ()*?[]{|}, !+@ before (, ! at the beginning, \\ before non-special characters.
	 * Windows: (){}[], !+@ before (, ! at the beginning.
	 */
	const POSIX_UNESCAPED_GLOB_SYMBOLS_RE = /(\\?)([()*?[\]{|}]|^!|[!+@](?=\()|\\(?![!()*+?@[\]{|}]))/g;
	const WINDOWS_UNESCAPED_GLOB_SYMBOLS_RE = /(\\?)([()[\]{}]|^!|[!+@](?=\())/g;
	/**
	 * The device path (\\.\ or \\?\).
	 * https://learn.microsoft.com/en-us/dotnet/standard/io/file-path-formats#dos-device-paths
	 */
	const DOS_DEVICE_PATH_RE = /^\\\\([.?])/;
	/**
	 * All backslashes except those escaping special characters.
	 * Windows: !()+@{}
	 * https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file#naming-conventions
	 */
	const WINDOWS_BACKSLASHES_RE = /\\(?![!()+@[\]{}])/g;
	/**
	 * Designed to work only with simple paths: `dir\\file`.
	 */
	function unixify(filepath) {
	    return filepath.replace(/\\/g, '/');
	}
	path.unixify = unixify;
	function makeAbsolute(cwd, filepath) {
	    return path$1.resolve(cwd, filepath);
	}
	path.makeAbsolute = makeAbsolute;
	function removeLeadingDotSegment(entry) {
	    // We do not use `startsWith` because this is 10x slower than current implementation for some cases.
	    // eslint-disable-next-line @typescript-eslint/prefer-string-starts-ends-with
	    if (entry.charAt(0) === '.') {
	        const secondCharactery = entry.charAt(1);
	        if (secondCharactery === '/' || secondCharactery === '\\') {
	            return entry.slice(LEADING_DOT_SEGMENT_CHARACTERS_COUNT);
	        }
	    }
	    return entry;
	}
	path.removeLeadingDotSegment = removeLeadingDotSegment;
	path.escape = IS_WINDOWS_PLATFORM ? escapeWindowsPath : escapePosixPath;
	function escapeWindowsPath(pattern) {
	    return pattern.replace(WINDOWS_UNESCAPED_GLOB_SYMBOLS_RE, '\\$2');
	}
	path.escapeWindowsPath = escapeWindowsPath;
	function escapePosixPath(pattern) {
	    return pattern.replace(POSIX_UNESCAPED_GLOB_SYMBOLS_RE, '\\$2');
	}
	path.escapePosixPath = escapePosixPath;
	path.convertPathToPattern = IS_WINDOWS_PLATFORM ? convertWindowsPathToPattern : convertPosixPathToPattern;
	function convertWindowsPathToPattern(filepath) {
	    return escapeWindowsPath(filepath)
	        .replace(DOS_DEVICE_PATH_RE, '//$1')
	        .replace(WINDOWS_BACKSLASHES_RE, '/');
	}
	path.convertWindowsPathToPattern = convertWindowsPathToPattern;
	function convertPosixPathToPattern(filepath) {
	    return escapePosixPath(filepath);
	}
	path.convertPosixPathToPattern = convertPosixPathToPattern;
	return path;
}

var pattern = {};

/*!
 * is-extglob <https://github.com/jonschlinkert/is-extglob>
 *
 * Copyright (c) 2014-2016, Jon Schlinkert.
 * Licensed under the MIT License.
 */

var isExtglob;
var hasRequiredIsExtglob;

function requireIsExtglob () {
	if (hasRequiredIsExtglob) return isExtglob;
	hasRequiredIsExtglob = 1;
	isExtglob = function isExtglob(str) {
	  if (typeof str !== 'string' || str === '') {
	    return false;
	  }

	  var match;
	  while ((match = /(\\).|([@?!+*]\(.*\))/g.exec(str))) {
	    if (match[2]) return true;
	    str = str.slice(match.index + match[0].length);
	  }

	  return false;
	};
	return isExtglob;
}

/*!
 * is-glob <https://github.com/jonschlinkert/is-glob>
 *
 * Copyright (c) 2014-2017, Jon Schlinkert.
 * Released under the MIT License.
 */

var isGlob;
var hasRequiredIsGlob;

function requireIsGlob () {
	if (hasRequiredIsGlob) return isGlob;
	hasRequiredIsGlob = 1;
	var isExtglob = requireIsExtglob();
	var chars = { '{': '}', '(': ')', '[': ']'};
	var strictCheck = function(str) {
	  if (str[0] === '!') {
	    return true;
	  }
	  var index = 0;
	  var pipeIndex = -2;
	  var closeSquareIndex = -2;
	  var closeCurlyIndex = -2;
	  var closeParenIndex = -2;
	  var backSlashIndex = -2;
	  while (index < str.length) {
	    if (str[index] === '*') {
	      return true;
	    }

	    if (str[index + 1] === '?' && /[\].+)]/.test(str[index])) {
	      return true;
	    }

	    if (closeSquareIndex !== -1 && str[index] === '[' && str[index + 1] !== ']') {
	      if (closeSquareIndex < index) {
	        closeSquareIndex = str.indexOf(']', index);
	      }
	      if (closeSquareIndex > index) {
	        if (backSlashIndex === -1 || backSlashIndex > closeSquareIndex) {
	          return true;
	        }
	        backSlashIndex = str.indexOf('\\', index);
	        if (backSlashIndex === -1 || backSlashIndex > closeSquareIndex) {
	          return true;
	        }
	      }
	    }

	    if (closeCurlyIndex !== -1 && str[index] === '{' && str[index + 1] !== '}') {
	      closeCurlyIndex = str.indexOf('}', index);
	      if (closeCurlyIndex > index) {
	        backSlashIndex = str.indexOf('\\', index);
	        if (backSlashIndex === -1 || backSlashIndex > closeCurlyIndex) {
	          return true;
	        }
	      }
	    }

	    if (closeParenIndex !== -1 && str[index] === '(' && str[index + 1] === '?' && /[:!=]/.test(str[index + 2]) && str[index + 3] !== ')') {
	      closeParenIndex = str.indexOf(')', index);
	      if (closeParenIndex > index) {
	        backSlashIndex = str.indexOf('\\', index);
	        if (backSlashIndex === -1 || backSlashIndex > closeParenIndex) {
	          return true;
	        }
	      }
	    }

	    if (pipeIndex !== -1 && str[index] === '(' && str[index + 1] !== '|') {
	      if (pipeIndex < index) {
	        pipeIndex = str.indexOf('|', index);
	      }
	      if (pipeIndex !== -1 && str[pipeIndex + 1] !== ')') {
	        closeParenIndex = str.indexOf(')', pipeIndex);
	        if (closeParenIndex > pipeIndex) {
	          backSlashIndex = str.indexOf('\\', pipeIndex);
	          if (backSlashIndex === -1 || backSlashIndex > closeParenIndex) {
	            return true;
	          }
	        }
	      }
	    }

	    if (str[index] === '\\') {
	      var open = str[index + 1];
	      index += 2;
	      var close = chars[open];

	      if (close) {
	        var n = str.indexOf(close, index);
	        if (n !== -1) {
	          index = n + 1;
	        }
	      }

	      if (str[index] === '!') {
	        return true;
	      }
	    } else {
	      index++;
	    }
	  }
	  return false;
	};

	var relaxedCheck = function(str) {
	  if (str[0] === '!') {
	    return true;
	  }
	  var index = 0;
	  while (index < str.length) {
	    if (/[*?{}()[\]]/.test(str[index])) {
	      return true;
	    }

	    if (str[index] === '\\') {
	      var open = str[index + 1];
	      index += 2;
	      var close = chars[open];

	      if (close) {
	        var n = str.indexOf(close, index);
	        if (n !== -1) {
	          index = n + 1;
	        }
	      }

	      if (str[index] === '!') {
	        return true;
	      }
	    } else {
	      index++;
	    }
	  }
	  return false;
	};

	isGlob = function isGlob(str, options) {
	  if (typeof str !== 'string' || str === '') {
	    return false;
	  }

	  if (isExtglob(str)) {
	    return true;
	  }

	  var check = strictCheck;

	  // optionally relax check
	  if (options && options.strict === false) {
	    check = relaxedCheck;
	  }

	  return check(str);
	};
	return isGlob;
}

var globParent;
var hasRequiredGlobParent;

function requireGlobParent () {
	if (hasRequiredGlobParent) return globParent;
	hasRequiredGlobParent = 1;

	var isGlob = requireIsGlob();
	var pathPosixDirname = require$$0$1.posix.dirname;
	var isWin32 = require$$0.platform() === 'win32';

	var slash = '/';
	var backslash = /\\/g;
	var enclosure = /[\{\[].*[\}\]]$/;
	var globby = /(^|[^\\])([\{\[]|\([^\)]+$)/;
	var escaped = /\\([\!\*\?\|\[\]\(\)\{\}])/g;

	/**
	 * @param {string} str
	 * @param {Object} opts
	 * @param {boolean} [opts.flipBackslashes=true]
	 * @returns {string}
	 */
	globParent = function globParent(str, opts) {
	  var options = Object.assign({ flipBackslashes: true }, opts);

	  // flip windows path separators
	  if (options.flipBackslashes && isWin32 && str.indexOf(slash) < 0) {
	    str = str.replace(backslash, slash);
	  }

	  // special case for strings ending in enclosure containing path separator
	  if (enclosure.test(str)) {
	    str += slash;
	  }

	  // preserves full path in case of trailing path separator
	  str += 'a';

	  // remove path parts that are globby
	  do {
	    str = pathPosixDirname(str);
	  } while (isGlob(str) || globby.test(str));

	  // remove escape chars and return result
	  return str.replace(escaped, '$1');
	};
	return globParent;
}

var utils$2 = {};

var hasRequiredUtils$3;

function requireUtils$3 () {
	if (hasRequiredUtils$3) return utils$2;
	hasRequiredUtils$3 = 1;
	(function (exports) {

		exports.isInteger = num => {
		  if (typeof num === 'number') {
		    return Number.isInteger(num);
		  }
		  if (typeof num === 'string' && num.trim() !== '') {
		    return Number.isInteger(Number(num));
		  }
		  return false;
		};

		/**
		 * Find a node of the given type
		 */

		exports.find = (node, type) => node.nodes.find(node => node.type === type);

		/**
		 * Find a node of the given type
		 */

		exports.exceedsLimit = (min, max, step = 1, limit) => {
		  if (limit === false) return false;
		  if (!exports.isInteger(min) || !exports.isInteger(max)) return false;
		  return ((Number(max) - Number(min)) / Number(step)) >= limit;
		};

		/**
		 * Escape the given node with '\\' before node.value
		 */

		exports.escapeNode = (block, n = 0, type) => {
		  const node = block.nodes[n];
		  if (!node) return;

		  if ((type && node.type === type) || node.type === 'open' || node.type === 'close') {
		    if (node.escaped !== true) {
		      node.value = '\\' + node.value;
		      node.escaped = true;
		    }
		  }
		};

		/**
		 * Returns true if the given brace node should be enclosed in literal braces
		 */

		exports.encloseBrace = node => {
		  if (node.type !== 'brace') return false;
		  if ((node.commas >> 0 + node.ranges >> 0) === 0) {
		    node.invalid = true;
		    return true;
		  }
		  return false;
		};

		/**
		 * Returns true if a brace node is invalid.
		 */

		exports.isInvalidBrace = block => {
		  if (block.type !== 'brace') return false;
		  if (block.invalid === true || block.dollar) return true;
		  if ((block.commas >> 0 + block.ranges >> 0) === 0) {
		    block.invalid = true;
		    return true;
		  }
		  if (block.open !== true || block.close !== true) {
		    block.invalid = true;
		    return true;
		  }
		  return false;
		};

		/**
		 * Returns true if a node is an open or close node
		 */

		exports.isOpenOrClose = node => {
		  if (node.type === 'open' || node.type === 'close') {
		    return true;
		  }
		  return node.open === true || node.close === true;
		};

		/**
		 * Reduce an array of text nodes.
		 */

		exports.reduce = nodes => nodes.reduce((acc, node) => {
		  if (node.type === 'text') acc.push(node.value);
		  if (node.type === 'range') node.type = 'text';
		  return acc;
		}, []);

		/**
		 * Flatten an array
		 */

		exports.flatten = (...args) => {
		  const result = [];

		  const flat = arr => {
		    for (let i = 0; i < arr.length; i++) {
		      const ele = arr[i];

		      if (Array.isArray(ele)) {
		        flat(ele);
		        continue;
		      }

		      if (ele !== undefined) {
		        result.push(ele);
		      }
		    }
		    return result;
		  };

		  flat(args);
		  return result;
		}; 
	} (utils$2));
	return utils$2;
}

var stringify;
var hasRequiredStringify;

function requireStringify () {
	if (hasRequiredStringify) return stringify;
	hasRequiredStringify = 1;

	const utils = requireUtils$3();

	stringify = (ast, options = {}) => {
	  const stringify = (node, parent = {}) => {
	    const invalidBlock = options.escapeInvalid && utils.isInvalidBrace(parent);
	    const invalidNode = node.invalid === true && options.escapeInvalid === true;
	    let output = '';

	    if (node.value) {
	      if ((invalidBlock || invalidNode) && utils.isOpenOrClose(node)) {
	        return '\\' + node.value;
	      }
	      return node.value;
	    }

	    if (node.value) {
	      return node.value;
	    }

	    if (node.nodes) {
	      for (const child of node.nodes) {
	        output += stringify(child);
	      }
	    }
	    return output;
	  };

	  return stringify(ast);
	};
	return stringify;
}

/*!
 * is-number <https://github.com/jonschlinkert/is-number>
 *
 * Copyright (c) 2014-present, Jon Schlinkert.
 * Released under the MIT License.
 */

var isNumber;
var hasRequiredIsNumber;

function requireIsNumber () {
	if (hasRequiredIsNumber) return isNumber;
	hasRequiredIsNumber = 1;

	isNumber = function(num) {
	  if (typeof num === 'number') {
	    return num - num === 0;
	  }
	  if (typeof num === 'string' && num.trim() !== '') {
	    return Number.isFinite ? Number.isFinite(+num) : isFinite(+num);
	  }
	  return false;
	};
	return isNumber;
}

/*!
 * to-regex-range <https://github.com/micromatch/to-regex-range>
 *
 * Copyright (c) 2015-present, Jon Schlinkert.
 * Released under the MIT License.
 */

var toRegexRange_1;
var hasRequiredToRegexRange;

function requireToRegexRange () {
	if (hasRequiredToRegexRange) return toRegexRange_1;
	hasRequiredToRegexRange = 1;

	const isNumber = requireIsNumber();

	const toRegexRange = (min, max, options) => {
	  if (isNumber(min) === false) {
	    throw new TypeError('toRegexRange: expected the first argument to be a number');
	  }

	  if (max === void 0 || min === max) {
	    return String(min);
	  }

	  if (isNumber(max) === false) {
	    throw new TypeError('toRegexRange: expected the second argument to be a number.');
	  }

	  let opts = { relaxZeros: true, ...options };
	  if (typeof opts.strictZeros === 'boolean') {
	    opts.relaxZeros = opts.strictZeros === false;
	  }

	  let relax = String(opts.relaxZeros);
	  let shorthand = String(opts.shorthand);
	  let capture = String(opts.capture);
	  let wrap = String(opts.wrap);
	  let cacheKey = min + ':' + max + '=' + relax + shorthand + capture + wrap;

	  if (toRegexRange.cache.hasOwnProperty(cacheKey)) {
	    return toRegexRange.cache[cacheKey].result;
	  }

	  let a = Math.min(min, max);
	  let b = Math.max(min, max);

	  if (Math.abs(a - b) === 1) {
	    let result = min + '|' + max;
	    if (opts.capture) {
	      return `(${result})`;
	    }
	    if (opts.wrap === false) {
	      return result;
	    }
	    return `(?:${result})`;
	  }

	  let isPadded = hasPadding(min) || hasPadding(max);
	  let state = { min, max, a, b };
	  let positives = [];
	  let negatives = [];

	  if (isPadded) {
	    state.isPadded = isPadded;
	    state.maxLen = String(state.max).length;
	  }

	  if (a < 0) {
	    let newMin = b < 0 ? Math.abs(b) : 1;
	    negatives = splitToPatterns(newMin, Math.abs(a), state, opts);
	    a = state.a = 0;
	  }

	  if (b >= 0) {
	    positives = splitToPatterns(a, b, state, opts);
	  }

	  state.negatives = negatives;
	  state.positives = positives;
	  state.result = collatePatterns(negatives, positives);

	  if (opts.capture === true) {
	    state.result = `(${state.result})`;
	  } else if (opts.wrap !== false && (positives.length + negatives.length) > 1) {
	    state.result = `(?:${state.result})`;
	  }

	  toRegexRange.cache[cacheKey] = state;
	  return state.result;
	};

	function collatePatterns(neg, pos, options) {
	  let onlyNegative = filterPatterns(neg, pos, '-', false);
	  let onlyPositive = filterPatterns(pos, neg, '', false);
	  let intersected = filterPatterns(neg, pos, '-?', true);
	  let subpatterns = onlyNegative.concat(intersected).concat(onlyPositive);
	  return subpatterns.join('|');
	}

	function splitToRanges(min, max) {
	  let nines = 1;
	  let zeros = 1;

	  let stop = countNines(min, nines);
	  let stops = new Set([max]);

	  while (min <= stop && stop <= max) {
	    stops.add(stop);
	    nines += 1;
	    stop = countNines(min, nines);
	  }

	  stop = countZeros(max + 1, zeros) - 1;

	  while (min < stop && stop <= max) {
	    stops.add(stop);
	    zeros += 1;
	    stop = countZeros(max + 1, zeros) - 1;
	  }

	  stops = [...stops];
	  stops.sort(compare);
	  return stops;
	}

	/**
	 * Convert a range to a regex pattern
	 * @param {Number} `start`
	 * @param {Number} `stop`
	 * @return {String}
	 */

	function rangeToPattern(start, stop, options) {
	  if (start === stop) {
	    return { pattern: start, count: [], digits: 0 };
	  }

	  let zipped = zip(start, stop);
	  let digits = zipped.length;
	  let pattern = '';
	  let count = 0;

	  for (let i = 0; i < digits; i++) {
	    let [startDigit, stopDigit] = zipped[i];

	    if (startDigit === stopDigit) {
	      pattern += startDigit;

	    } else if (startDigit !== '0' || stopDigit !== '9') {
	      pattern += toCharacterClass(startDigit, stopDigit);

	    } else {
	      count++;
	    }
	  }

	  if (count) {
	    pattern += options.shorthand === true ? '\\d' : '[0-9]';
	  }

	  return { pattern, count: [count], digits };
	}

	function splitToPatterns(min, max, tok, options) {
	  let ranges = splitToRanges(min, max);
	  let tokens = [];
	  let start = min;
	  let prev;

	  for (let i = 0; i < ranges.length; i++) {
	    let max = ranges[i];
	    let obj = rangeToPattern(String(start), String(max), options);
	    let zeros = '';

	    if (!tok.isPadded && prev && prev.pattern === obj.pattern) {
	      if (prev.count.length > 1) {
	        prev.count.pop();
	      }

	      prev.count.push(obj.count[0]);
	      prev.string = prev.pattern + toQuantifier(prev.count);
	      start = max + 1;
	      continue;
	    }

	    if (tok.isPadded) {
	      zeros = padZeros(max, tok, options);
	    }

	    obj.string = zeros + obj.pattern + toQuantifier(obj.count);
	    tokens.push(obj);
	    start = max + 1;
	    prev = obj;
	  }

	  return tokens;
	}

	function filterPatterns(arr, comparison, prefix, intersection, options) {
	  let result = [];

	  for (let ele of arr) {
	    let { string } = ele;

	    // only push if _both_ are negative...
	    if (!intersection && !contains(comparison, 'string', string)) {
	      result.push(prefix + string);
	    }

	    // or _both_ are positive
	    if (intersection && contains(comparison, 'string', string)) {
	      result.push(prefix + string);
	    }
	  }
	  return result;
	}

	/**
	 * Zip strings
	 */

	function zip(a, b) {
	  let arr = [];
	  for (let i = 0; i < a.length; i++) arr.push([a[i], b[i]]);
	  return arr;
	}

	function compare(a, b) {
	  return a > b ? 1 : b > a ? -1 : 0;
	}

	function contains(arr, key, val) {
	  return arr.some(ele => ele[key] === val);
	}

	function countNines(min, len) {
	  return Number(String(min).slice(0, -len) + '9'.repeat(len));
	}

	function countZeros(integer, zeros) {
	  return integer - (integer % Math.pow(10, zeros));
	}

	function toQuantifier(digits) {
	  let [start = 0, stop = ''] = digits;
	  if (stop || start > 1) {
	    return `{${start + (stop ? ',' + stop : '')}}`;
	  }
	  return '';
	}

	function toCharacterClass(a, b, options) {
	  return `[${a}${(b - a === 1) ? '' : '-'}${b}]`;
	}

	function hasPadding(str) {
	  return /^-?(0+)\d/.test(str);
	}

	function padZeros(value, tok, options) {
	  if (!tok.isPadded) {
	    return value;
	  }

	  let diff = Math.abs(tok.maxLen - String(value).length);
	  let relax = options.relaxZeros !== false;

	  switch (diff) {
	    case 0:
	      return '';
	    case 1:
	      return relax ? '0?' : '0';
	    case 2:
	      return relax ? '0{0,2}' : '00';
	    default: {
	      return relax ? `0{0,${diff}}` : `0{${diff}}`;
	    }
	  }
	}

	/**
	 * Cache
	 */

	toRegexRange.cache = {};
	toRegexRange.clearCache = () => (toRegexRange.cache = {});

	/**
	 * Expose `toRegexRange`
	 */

	toRegexRange_1 = toRegexRange;
	return toRegexRange_1;
}

/*!
 * fill-range <https://github.com/jonschlinkert/fill-range>
 *
 * Copyright (c) 2014-present, Jon Schlinkert.
 * Licensed under the MIT License.
 */

var fillRange;
var hasRequiredFillRange;

function requireFillRange () {
	if (hasRequiredFillRange) return fillRange;
	hasRequiredFillRange = 1;

	const util = require$$0$2;
	const toRegexRange = requireToRegexRange();

	const isObject = val => val !== null && typeof val === 'object' && !Array.isArray(val);

	const transform = toNumber => {
	  return value => toNumber === true ? Number(value) : String(value);
	};

	const isValidValue = value => {
	  return typeof value === 'number' || (typeof value === 'string' && value !== '');
	};

	const isNumber = num => Number.isInteger(+num);

	const zeros = input => {
	  let value = `${input}`;
	  let index = -1;
	  if (value[0] === '-') value = value.slice(1);
	  if (value === '0') return false;
	  while (value[++index] === '0');
	  return index > 0;
	};

	const stringify = (start, end, options) => {
	  if (typeof start === 'string' || typeof end === 'string') {
	    return true;
	  }
	  return options.stringify === true;
	};

	const pad = (input, maxLength, toNumber) => {
	  if (maxLength > 0) {
	    let dash = input[0] === '-' ? '-' : '';
	    if (dash) input = input.slice(1);
	    input = (dash + input.padStart(dash ? maxLength - 1 : maxLength, '0'));
	  }
	  if (toNumber === false) {
	    return String(input);
	  }
	  return input;
	};

	const toMaxLen = (input, maxLength) => {
	  let negative = input[0] === '-' ? '-' : '';
	  if (negative) {
	    input = input.slice(1);
	    maxLength--;
	  }
	  while (input.length < maxLength) input = '0' + input;
	  return negative ? ('-' + input) : input;
	};

	const toSequence = (parts, options, maxLen) => {
	  parts.negatives.sort((a, b) => a < b ? -1 : a > b ? 1 : 0);
	  parts.positives.sort((a, b) => a < b ? -1 : a > b ? 1 : 0);

	  let prefix = options.capture ? '' : '?:';
	  let positives = '';
	  let negatives = '';
	  let result;

	  if (parts.positives.length) {
	    positives = parts.positives.map(v => toMaxLen(String(v), maxLen)).join('|');
	  }

	  if (parts.negatives.length) {
	    negatives = `-(${prefix}${parts.negatives.map(v => toMaxLen(String(v), maxLen)).join('|')})`;
	  }

	  if (positives && negatives) {
	    result = `${positives}|${negatives}`;
	  } else {
	    result = positives || negatives;
	  }

	  if (options.wrap) {
	    return `(${prefix}${result})`;
	  }

	  return result;
	};

	const toRange = (a, b, isNumbers, options) => {
	  if (isNumbers) {
	    return toRegexRange(a, b, { wrap: false, ...options });
	  }

	  let start = String.fromCharCode(a);
	  if (a === b) return start;

	  let stop = String.fromCharCode(b);
	  return `[${start}-${stop}]`;
	};

	const toRegex = (start, end, options) => {
	  if (Array.isArray(start)) {
	    let wrap = options.wrap === true;
	    let prefix = options.capture ? '' : '?:';
	    return wrap ? `(${prefix}${start.join('|')})` : start.join('|');
	  }
	  return toRegexRange(start, end, options);
	};

	const rangeError = (...args) => {
	  return new RangeError('Invalid range arguments: ' + util.inspect(...args));
	};

	const invalidRange = (start, end, options) => {
	  if (options.strictRanges === true) throw rangeError([start, end]);
	  return [];
	};

	const invalidStep = (step, options) => {
	  if (options.strictRanges === true) {
	    throw new TypeError(`Expected step "${step}" to be a number`);
	  }
	  return [];
	};

	const fillNumbers = (start, end, step = 1, options = {}) => {
	  let a = Number(start);
	  let b = Number(end);

	  if (!Number.isInteger(a) || !Number.isInteger(b)) {
	    if (options.strictRanges === true) throw rangeError([start, end]);
	    return [];
	  }

	  // fix negative zero
	  if (a === 0) a = 0;
	  if (b === 0) b = 0;

	  let descending = a > b;
	  let startString = String(start);
	  let endString = String(end);
	  let stepString = String(step);
	  step = Math.max(Math.abs(step), 1);

	  let padded = zeros(startString) || zeros(endString) || zeros(stepString);
	  let maxLen = padded ? Math.max(startString.length, endString.length, stepString.length) : 0;
	  let toNumber = padded === false && stringify(start, end, options) === false;
	  let format = options.transform || transform(toNumber);

	  if (options.toRegex && step === 1) {
	    return toRange(toMaxLen(start, maxLen), toMaxLen(end, maxLen), true, options);
	  }

	  let parts = { negatives: [], positives: [] };
	  let push = num => parts[num < 0 ? 'negatives' : 'positives'].push(Math.abs(num));
	  let range = [];
	  let index = 0;

	  while (descending ? a >= b : a <= b) {
	    if (options.toRegex === true && step > 1) {
	      push(a);
	    } else {
	      range.push(pad(format(a, index), maxLen, toNumber));
	    }
	    a = descending ? a - step : a + step;
	    index++;
	  }

	  if (options.toRegex === true) {
	    return step > 1
	      ? toSequence(parts, options, maxLen)
	      : toRegex(range, null, { wrap: false, ...options });
	  }

	  return range;
	};

	const fillLetters = (start, end, step = 1, options = {}) => {
	  if ((!isNumber(start) && start.length > 1) || (!isNumber(end) && end.length > 1)) {
	    return invalidRange(start, end, options);
	  }

	  let format = options.transform || (val => String.fromCharCode(val));
	  let a = `${start}`.charCodeAt(0);
	  let b = `${end}`.charCodeAt(0);

	  let descending = a > b;
	  let min = Math.min(a, b);
	  let max = Math.max(a, b);

	  if (options.toRegex && step === 1) {
	    return toRange(min, max, false, options);
	  }

	  let range = [];
	  let index = 0;

	  while (descending ? a >= b : a <= b) {
	    range.push(format(a, index));
	    a = descending ? a - step : a + step;
	    index++;
	  }

	  if (options.toRegex === true) {
	    return toRegex(range, null, { wrap: false, options });
	  }

	  return range;
	};

	const fill = (start, end, step, options = {}) => {
	  if (end == null && isValidValue(start)) {
	    return [start];
	  }

	  if (!isValidValue(start) || !isValidValue(end)) {
	    return invalidRange(start, end, options);
	  }

	  if (typeof step === 'function') {
	    return fill(start, end, 1, { transform: step });
	  }

	  if (isObject(step)) {
	    return fill(start, end, 0, step);
	  }

	  let opts = { ...options };
	  if (opts.capture === true) opts.wrap = true;
	  step = step || opts.step || 1;

	  if (!isNumber(step)) {
	    if (step != null && !isObject(step)) return invalidStep(step, opts);
	    return fill(start, end, 1, step);
	  }

	  if (isNumber(start) && isNumber(end)) {
	    return fillNumbers(start, end, step, opts);
	  }

	  return fillLetters(start, end, Math.max(Math.abs(step), 1), opts);
	};

	fillRange = fill;
	return fillRange;
}

var compile_1;
var hasRequiredCompile;

function requireCompile () {
	if (hasRequiredCompile) return compile_1;
	hasRequiredCompile = 1;

	const fill = requireFillRange();
	const utils = requireUtils$3();

	const compile = (ast, options = {}) => {
	  const walk = (node, parent = {}) => {
	    const invalidBlock = utils.isInvalidBrace(parent);
	    const invalidNode = node.invalid === true && options.escapeInvalid === true;
	    const invalid = invalidBlock === true || invalidNode === true;
	    const prefix = options.escapeInvalid === true ? '\\' : '';
	    let output = '';

	    if (node.isOpen === true) {
	      return prefix + node.value;
	    }

	    if (node.isClose === true) {
	      console.log('node.isClose', prefix, node.value);
	      return prefix + node.value;
	    }

	    if (node.type === 'open') {
	      return invalid ? prefix + node.value : '(';
	    }

	    if (node.type === 'close') {
	      return invalid ? prefix + node.value : ')';
	    }

	    if (node.type === 'comma') {
	      return node.prev.type === 'comma' ? '' : invalid ? node.value : '|';
	    }

	    if (node.value) {
	      return node.value;
	    }

	    if (node.nodes && node.ranges > 0) {
	      const args = utils.reduce(node.nodes);
	      const range = fill(...args, { ...options, wrap: false, toRegex: true, strictZeros: true });

	      if (range.length !== 0) {
	        return args.length > 1 && range.length > 1 ? `(${range})` : range;
	      }
	    }

	    if (node.nodes) {
	      for (const child of node.nodes) {
	        output += walk(child, node);
	      }
	    }

	    return output;
	  };

	  return walk(ast);
	};

	compile_1 = compile;
	return compile_1;
}

var expand_1;
var hasRequiredExpand;

function requireExpand () {
	if (hasRequiredExpand) return expand_1;
	hasRequiredExpand = 1;

	const fill = requireFillRange();
	const stringify = requireStringify();
	const utils = requireUtils$3();

	const append = (queue = '', stash = '', enclose = false) => {
	  const result = [];

	  queue = [].concat(queue);
	  stash = [].concat(stash);

	  if (!stash.length) return queue;
	  if (!queue.length) {
	    return enclose ? utils.flatten(stash).map(ele => `{${ele}}`) : stash;
	  }

	  for (const item of queue) {
	    if (Array.isArray(item)) {
	      for (const value of item) {
	        result.push(append(value, stash, enclose));
	      }
	    } else {
	      for (let ele of stash) {
	        if (enclose === true && typeof ele === 'string') ele = `{${ele}}`;
	        result.push(Array.isArray(ele) ? append(item, ele, enclose) : item + ele);
	      }
	    }
	  }
	  return utils.flatten(result);
	};

	const expand = (ast, options = {}) => {
	  const rangeLimit = options.rangeLimit === undefined ? 1000 : options.rangeLimit;

	  const walk = (node, parent = {}) => {
	    node.queue = [];

	    let p = parent;
	    let q = parent.queue;

	    while (p.type !== 'brace' && p.type !== 'root' && p.parent) {
	      p = p.parent;
	      q = p.queue;
	    }

	    if (node.invalid || node.dollar) {
	      q.push(append(q.pop(), stringify(node, options)));
	      return;
	    }

	    if (node.type === 'brace' && node.invalid !== true && node.nodes.length === 2) {
	      q.push(append(q.pop(), ['{}']));
	      return;
	    }

	    if (node.nodes && node.ranges > 0) {
	      const args = utils.reduce(node.nodes);

	      if (utils.exceedsLimit(...args, options.step, rangeLimit)) {
	        throw new RangeError('expanded array length exceeds range limit. Use options.rangeLimit to increase or disable the limit.');
	      }

	      let range = fill(...args, options);
	      if (range.length === 0) {
	        range = stringify(node, options);
	      }

	      q.push(append(q.pop(), range));
	      node.nodes = [];
	      return;
	    }

	    const enclose = utils.encloseBrace(node);
	    let queue = node.queue;
	    let block = node;

	    while (block.type !== 'brace' && block.type !== 'root' && block.parent) {
	      block = block.parent;
	      queue = block.queue;
	    }

	    for (let i = 0; i < node.nodes.length; i++) {
	      const child = node.nodes[i];

	      if (child.type === 'comma' && node.type === 'brace') {
	        if (i === 1) queue.push('');
	        queue.push('');
	        continue;
	      }

	      if (child.type === 'close') {
	        q.push(append(q.pop(), queue, enclose));
	        continue;
	      }

	      if (child.value && child.type !== 'open') {
	        queue.push(append(queue.pop(), child.value));
	        continue;
	      }

	      if (child.nodes) {
	        walk(child, node);
	      }
	    }

	    return queue;
	  };

	  return utils.flatten(walk(ast));
	};

	expand_1 = expand;
	return expand_1;
}

var constants$2;
var hasRequiredConstants$2;

function requireConstants$2 () {
	if (hasRequiredConstants$2) return constants$2;
	hasRequiredConstants$2 = 1;

	constants$2 = {
	  MAX_LENGTH: 10000,

	  // Digits
	  CHAR_0: '0', /* 0 */
	  CHAR_9: '9', /* 9 */

	  // Alphabet chars.
	  CHAR_UPPERCASE_A: 'A', /* A */
	  CHAR_LOWERCASE_A: 'a', /* a */
	  CHAR_UPPERCASE_Z: 'Z', /* Z */
	  CHAR_LOWERCASE_Z: 'z', /* z */

	  CHAR_LEFT_PARENTHESES: '(', /* ( */
	  CHAR_RIGHT_PARENTHESES: ')', /* ) */

	  CHAR_ASTERISK: '*', /* * */

	  // Non-alphabetic chars.
	  CHAR_AMPERSAND: '&', /* & */
	  CHAR_AT: '@', /* @ */
	  CHAR_BACKSLASH: '\\', /* \ */
	  CHAR_BACKTICK: '`', /* ` */
	  CHAR_CARRIAGE_RETURN: '\r', /* \r */
	  CHAR_CIRCUMFLEX_ACCENT: '^', /* ^ */
	  CHAR_COLON: ':', /* : */
	  CHAR_COMMA: ',', /* , */
	  CHAR_DOLLAR: '$', /* . */
	  CHAR_DOT: '.', /* . */
	  CHAR_DOUBLE_QUOTE: '"', /* " */
	  CHAR_EQUAL: '=', /* = */
	  CHAR_EXCLAMATION_MARK: '!', /* ! */
	  CHAR_FORM_FEED: '\f', /* \f */
	  CHAR_FORWARD_SLASH: '/', /* / */
	  CHAR_HASH: '#', /* # */
	  CHAR_HYPHEN_MINUS: '-', /* - */
	  CHAR_LEFT_ANGLE_BRACKET: '<', /* < */
	  CHAR_LEFT_CURLY_BRACE: '{', /* { */
	  CHAR_LEFT_SQUARE_BRACKET: '[', /* [ */
	  CHAR_LINE_FEED: '\n', /* \n */
	  CHAR_NO_BREAK_SPACE: '\u00A0', /* \u00A0 */
	  CHAR_PERCENT: '%', /* % */
	  CHAR_PLUS: '+', /* + */
	  CHAR_QUESTION_MARK: '?', /* ? */
	  CHAR_RIGHT_ANGLE_BRACKET: '>', /* > */
	  CHAR_RIGHT_CURLY_BRACE: '}', /* } */
	  CHAR_RIGHT_SQUARE_BRACKET: ']', /* ] */
	  CHAR_SEMICOLON: ';', /* ; */
	  CHAR_SINGLE_QUOTE: '\'', /* ' */
	  CHAR_SPACE: ' ', /*   */
	  CHAR_TAB: '\t', /* \t */
	  CHAR_UNDERSCORE: '_', /* _ */
	  CHAR_VERTICAL_LINE: '|', /* | */
	  CHAR_ZERO_WIDTH_NOBREAK_SPACE: '\uFEFF' /* \uFEFF */
	};
	return constants$2;
}

var parse_1$1;
var hasRequiredParse$1;

function requireParse$1 () {
	if (hasRequiredParse$1) return parse_1$1;
	hasRequiredParse$1 = 1;

	const stringify = requireStringify();

	/**
	 * Constants
	 */

	const {
	  MAX_LENGTH,
	  CHAR_BACKSLASH, /* \ */
	  CHAR_BACKTICK, /* ` */
	  CHAR_COMMA, /* , */
	  CHAR_DOT, /* . */
	  CHAR_LEFT_PARENTHESES, /* ( */
	  CHAR_RIGHT_PARENTHESES, /* ) */
	  CHAR_LEFT_CURLY_BRACE, /* { */
	  CHAR_RIGHT_CURLY_BRACE, /* } */
	  CHAR_LEFT_SQUARE_BRACKET, /* [ */
	  CHAR_RIGHT_SQUARE_BRACKET, /* ] */
	  CHAR_DOUBLE_QUOTE, /* " */
	  CHAR_SINGLE_QUOTE, /* ' */
	  CHAR_NO_BREAK_SPACE,
	  CHAR_ZERO_WIDTH_NOBREAK_SPACE
	} = requireConstants$2();

	/**
	 * parse
	 */

	const parse = (input, options = {}) => {
	  if (typeof input !== 'string') {
	    throw new TypeError('Expected a string');
	  }

	  const opts = options || {};
	  const max = typeof opts.maxLength === 'number' ? Math.min(MAX_LENGTH, opts.maxLength) : MAX_LENGTH;
	  if (input.length > max) {
	    throw new SyntaxError(`Input length (${input.length}), exceeds max characters (${max})`);
	  }

	  const ast = { type: 'root', input, nodes: [] };
	  const stack = [ast];
	  let block = ast;
	  let prev = ast;
	  let brackets = 0;
	  const length = input.length;
	  let index = 0;
	  let depth = 0;
	  let value;

	  /**
	   * Helpers
	   */

	  const advance = () => input[index++];
	  const push = node => {
	    if (node.type === 'text' && prev.type === 'dot') {
	      prev.type = 'text';
	    }

	    if (prev && prev.type === 'text' && node.type === 'text') {
	      prev.value += node.value;
	      return;
	    }

	    block.nodes.push(node);
	    node.parent = block;
	    node.prev = prev;
	    prev = node;
	    return node;
	  };

	  push({ type: 'bos' });

	  while (index < length) {
	    block = stack[stack.length - 1];
	    value = advance();

	    /**
	     * Invalid chars
	     */

	    if (value === CHAR_ZERO_WIDTH_NOBREAK_SPACE || value === CHAR_NO_BREAK_SPACE) {
	      continue;
	    }

	    /**
	     * Escaped chars
	     */

	    if (value === CHAR_BACKSLASH) {
	      push({ type: 'text', value: (options.keepEscaping ? value : '') + advance() });
	      continue;
	    }

	    /**
	     * Right square bracket (literal): ']'
	     */

	    if (value === CHAR_RIGHT_SQUARE_BRACKET) {
	      push({ type: 'text', value: '\\' + value });
	      continue;
	    }

	    /**
	     * Left square bracket: '['
	     */

	    if (value === CHAR_LEFT_SQUARE_BRACKET) {
	      brackets++;

	      let next;

	      while (index < length && (next = advance())) {
	        value += next;

	        if (next === CHAR_LEFT_SQUARE_BRACKET) {
	          brackets++;
	          continue;
	        }

	        if (next === CHAR_BACKSLASH) {
	          value += advance();
	          continue;
	        }

	        if (next === CHAR_RIGHT_SQUARE_BRACKET) {
	          brackets--;

	          if (brackets === 0) {
	            break;
	          }
	        }
	      }

	      push({ type: 'text', value });
	      continue;
	    }

	    /**
	     * Parentheses
	     */

	    if (value === CHAR_LEFT_PARENTHESES) {
	      block = push({ type: 'paren', nodes: [] });
	      stack.push(block);
	      push({ type: 'text', value });
	      continue;
	    }

	    if (value === CHAR_RIGHT_PARENTHESES) {
	      if (block.type !== 'paren') {
	        push({ type: 'text', value });
	        continue;
	      }
	      block = stack.pop();
	      push({ type: 'text', value });
	      block = stack[stack.length - 1];
	      continue;
	    }

	    /**
	     * Quotes: '|"|`
	     */

	    if (value === CHAR_DOUBLE_QUOTE || value === CHAR_SINGLE_QUOTE || value === CHAR_BACKTICK) {
	      const open = value;
	      let next;

	      if (options.keepQuotes !== true) {
	        value = '';
	      }

	      while (index < length && (next = advance())) {
	        if (next === CHAR_BACKSLASH) {
	          value += next + advance();
	          continue;
	        }

	        if (next === open) {
	          if (options.keepQuotes === true) value += next;
	          break;
	        }

	        value += next;
	      }

	      push({ type: 'text', value });
	      continue;
	    }

	    /**
	     * Left curly brace: '{'
	     */

	    if (value === CHAR_LEFT_CURLY_BRACE) {
	      depth++;

	      const dollar = prev.value && prev.value.slice(-1) === '$' || block.dollar === true;
	      const brace = {
	        type: 'brace',
	        open: true,
	        close: false,
	        dollar,
	        depth,
	        commas: 0,
	        ranges: 0,
	        nodes: []
	      };

	      block = push(brace);
	      stack.push(block);
	      push({ type: 'open', value });
	      continue;
	    }

	    /**
	     * Right curly brace: '}'
	     */

	    if (value === CHAR_RIGHT_CURLY_BRACE) {
	      if (block.type !== 'brace') {
	        push({ type: 'text', value });
	        continue;
	      }

	      const type = 'close';
	      block = stack.pop();
	      block.close = true;

	      push({ type, value });
	      depth--;

	      block = stack[stack.length - 1];
	      continue;
	    }

	    /**
	     * Comma: ','
	     */

	    if (value === CHAR_COMMA && depth > 0) {
	      if (block.ranges > 0) {
	        block.ranges = 0;
	        const open = block.nodes.shift();
	        block.nodes = [open, { type: 'text', value: stringify(block) }];
	      }

	      push({ type: 'comma', value });
	      block.commas++;
	      continue;
	    }

	    /**
	     * Dot: '.'
	     */

	    if (value === CHAR_DOT && depth > 0 && block.commas === 0) {
	      const siblings = block.nodes;

	      if (depth === 0 || siblings.length === 0) {
	        push({ type: 'text', value });
	        continue;
	      }

	      if (prev.type === 'dot') {
	        block.range = [];
	        prev.value += value;
	        prev.type = 'range';

	        if (block.nodes.length !== 3 && block.nodes.length !== 5) {
	          block.invalid = true;
	          block.ranges = 0;
	          prev.type = 'text';
	          continue;
	        }

	        block.ranges++;
	        block.args = [];
	        continue;
	      }

	      if (prev.type === 'range') {
	        siblings.pop();

	        const before = siblings[siblings.length - 1];
	        before.value += prev.value + value;
	        prev = before;
	        block.ranges--;
	        continue;
	      }

	      push({ type: 'dot', value });
	      continue;
	    }

	    /**
	     * Text
	     */

	    push({ type: 'text', value });
	  }

	  // Mark imbalanced braces and brackets as invalid
	  do {
	    block = stack.pop();

	    if (block.type !== 'root') {
	      block.nodes.forEach(node => {
	        if (!node.nodes) {
	          if (node.type === 'open') node.isOpen = true;
	          if (node.type === 'close') node.isClose = true;
	          if (!node.nodes) node.type = 'text';
	          node.invalid = true;
	        }
	      });

	      // get the location of the block on parent.nodes (block's siblings)
	      const parent = stack[stack.length - 1];
	      const index = parent.nodes.indexOf(block);
	      // replace the (invalid) block with it's nodes
	      parent.nodes.splice(index, 1, ...block.nodes);
	    }
	  } while (stack.length > 0);

	  push({ type: 'eos' });
	  return ast;
	};

	parse_1$1 = parse;
	return parse_1$1;
}

var braces_1;
var hasRequiredBraces;

function requireBraces () {
	if (hasRequiredBraces) return braces_1;
	hasRequiredBraces = 1;

	const stringify = requireStringify();
	const compile = requireCompile();
	const expand = requireExpand();
	const parse = requireParse$1();

	/**
	 * Expand the given pattern or create a regex-compatible string.
	 *
	 * ```js
	 * const braces = require('braces');
	 * console.log(braces('{a,b,c}', { compile: true })); //=> ['(a|b|c)']
	 * console.log(braces('{a,b,c}')); //=> ['a', 'b', 'c']
	 * ```
	 * @param {String} `str`
	 * @param {Object} `options`
	 * @return {String}
	 * @api public
	 */

	const braces = (input, options = {}) => {
	  let output = [];

	  if (Array.isArray(input)) {
	    for (const pattern of input) {
	      const result = braces.create(pattern, options);
	      if (Array.isArray(result)) {
	        output.push(...result);
	      } else {
	        output.push(result);
	      }
	    }
	  } else {
	    output = [].concat(braces.create(input, options));
	  }

	  if (options && options.expand === true && options.nodupes === true) {
	    output = [...new Set(output)];
	  }
	  return output;
	};

	/**
	 * Parse the given `str` with the given `options`.
	 *
	 * ```js
	 * // braces.parse(pattern, [, options]);
	 * const ast = braces.parse('a/{b,c}/d');
	 * console.log(ast);
	 * ```
	 * @param {String} pattern Brace pattern to parse
	 * @param {Object} options
	 * @return {Object} Returns an AST
	 * @api public
	 */

	braces.parse = (input, options = {}) => parse(input, options);

	/**
	 * Creates a braces string from an AST, or an AST node.
	 *
	 * ```js
	 * const braces = require('braces');
	 * let ast = braces.parse('foo/{a,b}/bar');
	 * console.log(stringify(ast.nodes[2])); //=> '{a,b}'
	 * ```
	 * @param {String} `input` Brace pattern or AST.
	 * @param {Object} `options`
	 * @return {Array} Returns an array of expanded values.
	 * @api public
	 */

	braces.stringify = (input, options = {}) => {
	  if (typeof input === 'string') {
	    return stringify(braces.parse(input, options), options);
	  }
	  return stringify(input, options);
	};

	/**
	 * Compiles a brace pattern into a regex-compatible, optimized string.
	 * This method is called by the main [braces](#braces) function by default.
	 *
	 * ```js
	 * const braces = require('braces');
	 * console.log(braces.compile('a/{b,c}/d'));
	 * //=> ['a/(b|c)/d']
	 * ```
	 * @param {String} `input` Brace pattern or AST.
	 * @param {Object} `options`
	 * @return {Array} Returns an array of expanded values.
	 * @api public
	 */

	braces.compile = (input, options = {}) => {
	  if (typeof input === 'string') {
	    input = braces.parse(input, options);
	  }
	  return compile(input, options);
	};

	/**
	 * Expands a brace pattern into an array. This method is called by the
	 * main [braces](#braces) function when `options.expand` is true. Before
	 * using this method it's recommended that you read the [performance notes](#performance))
	 * and advantages of using [.compile](#compile) instead.
	 *
	 * ```js
	 * const braces = require('braces');
	 * console.log(braces.expand('a/{b,c}/d'));
	 * //=> ['a/b/d', 'a/c/d'];
	 * ```
	 * @param {String} `pattern` Brace pattern
	 * @param {Object} `options`
	 * @return {Array} Returns an array of expanded values.
	 * @api public
	 */

	braces.expand = (input, options = {}) => {
	  if (typeof input === 'string') {
	    input = braces.parse(input, options);
	  }

	  let result = expand(input, options);

	  // filter out empty strings if specified
	  if (options.noempty === true) {
	    result = result.filter(Boolean);
	  }

	  // filter out duplicates if specified
	  if (options.nodupes === true) {
	    result = [...new Set(result)];
	  }

	  return result;
	};

	/**
	 * Processes a brace pattern and returns either an expanded array
	 * (if `options.expand` is true), a highly optimized regex-compatible string.
	 * This method is called by the main [braces](#braces) function.
	 *
	 * ```js
	 * const braces = require('braces');
	 * console.log(braces.create('user-{200..300}/project-{a,b,c}-{1..10}'))
	 * //=> 'user-(20[0-9]|2[1-9][0-9]|300)/project-(a|b|c)-([1-9]|10)'
	 * ```
	 * @param {String} `pattern` Brace pattern
	 * @param {Object} `options`
	 * @return {Array} Returns an array of expanded values.
	 * @api public
	 */

	braces.create = (input, options = {}) => {
	  if (input === '' || input.length < 3) {
	    return [input];
	  }

	  return options.expand !== true
	    ? braces.compile(input, options)
	    : braces.expand(input, options);
	};

	/**
	 * Expose "braces"
	 */

	braces_1 = braces;
	return braces_1;
}

var utils$1 = {};

var constants$1;
var hasRequiredConstants$1;

function requireConstants$1 () {
	if (hasRequiredConstants$1) return constants$1;
	hasRequiredConstants$1 = 1;

	const path = require$$0$1;
	const WIN_SLASH = '\\\\/';
	const WIN_NO_SLASH = `[^${WIN_SLASH}]`;

	/**
	 * Posix glob regex
	 */

	const DOT_LITERAL = '\\.';
	const PLUS_LITERAL = '\\+';
	const QMARK_LITERAL = '\\?';
	const SLASH_LITERAL = '\\/';
	const ONE_CHAR = '(?=.)';
	const QMARK = '[^/]';
	const END_ANCHOR = `(?:${SLASH_LITERAL}|$)`;
	const START_ANCHOR = `(?:^|${SLASH_LITERAL})`;
	const DOTS_SLASH = `${DOT_LITERAL}{1,2}${END_ANCHOR}`;
	const NO_DOT = `(?!${DOT_LITERAL})`;
	const NO_DOTS = `(?!${START_ANCHOR}${DOTS_SLASH})`;
	const NO_DOT_SLASH = `(?!${DOT_LITERAL}{0,1}${END_ANCHOR})`;
	const NO_DOTS_SLASH = `(?!${DOTS_SLASH})`;
	const QMARK_NO_DOT = `[^.${SLASH_LITERAL}]`;
	const STAR = `${QMARK}*?`;

	const POSIX_CHARS = {
	  DOT_LITERAL,
	  PLUS_LITERAL,
	  QMARK_LITERAL,
	  SLASH_LITERAL,
	  ONE_CHAR,
	  QMARK,
	  END_ANCHOR,
	  DOTS_SLASH,
	  NO_DOT,
	  NO_DOTS,
	  NO_DOT_SLASH,
	  NO_DOTS_SLASH,
	  QMARK_NO_DOT,
	  STAR,
	  START_ANCHOR
	};

	/**
	 * Windows glob regex
	 */

	const WINDOWS_CHARS = {
	  ...POSIX_CHARS,

	  SLASH_LITERAL: `[${WIN_SLASH}]`,
	  QMARK: WIN_NO_SLASH,
	  STAR: `${WIN_NO_SLASH}*?`,
	  DOTS_SLASH: `${DOT_LITERAL}{1,2}(?:[${WIN_SLASH}]|$)`,
	  NO_DOT: `(?!${DOT_LITERAL})`,
	  NO_DOTS: `(?!(?:^|[${WIN_SLASH}])${DOT_LITERAL}{1,2}(?:[${WIN_SLASH}]|$))`,
	  NO_DOT_SLASH: `(?!${DOT_LITERAL}{0,1}(?:[${WIN_SLASH}]|$))`,
	  NO_DOTS_SLASH: `(?!${DOT_LITERAL}{1,2}(?:[${WIN_SLASH}]|$))`,
	  QMARK_NO_DOT: `[^.${WIN_SLASH}]`,
	  START_ANCHOR: `(?:^|[${WIN_SLASH}])`,
	  END_ANCHOR: `(?:[${WIN_SLASH}]|$)`
	};

	/**
	 * POSIX Bracket Regex
	 */

	const POSIX_REGEX_SOURCE = {
	  alnum: 'a-zA-Z0-9',
	  alpha: 'a-zA-Z',
	  ascii: '\\x00-\\x7F',
	  blank: ' \\t',
	  cntrl: '\\x00-\\x1F\\x7F',
	  digit: '0-9',
	  graph: '\\x21-\\x7E',
	  lower: 'a-z',
	  print: '\\x20-\\x7E ',
	  punct: '\\-!"#$%&\'()\\*+,./:;<=>?@[\\]^_`{|}~',
	  space: ' \\t\\r\\n\\v\\f',
	  upper: 'A-Z',
	  word: 'A-Za-z0-9_',
	  xdigit: 'A-Fa-f0-9'
	};

	constants$1 = {
	  MAX_LENGTH: 1024 * 64,
	  POSIX_REGEX_SOURCE,

	  // regular expressions
	  REGEX_BACKSLASH: /\\(?![*+?^${}(|)[\]])/g,
	  REGEX_NON_SPECIAL_CHARS: /^[^@![\].,$*+?^{}()|\\/]+/,
	  REGEX_SPECIAL_CHARS: /[-*+?.^${}(|)[\]]/,
	  REGEX_SPECIAL_CHARS_BACKREF: /(\\?)((\W)(\3*))/g,
	  REGEX_SPECIAL_CHARS_GLOBAL: /([-*+?.^${}(|)[\]])/g,
	  REGEX_REMOVE_BACKSLASH: /(?:\[.*?[^\\]\]|\\(?=.))/g,

	  // Replace globs with equivalent patterns to reduce parsing time.
	  REPLACEMENTS: {
	    '***': '*',
	    '**/**': '**',
	    '**/**/**': '**'
	  },

	  // Digits
	  CHAR_0: 48, /* 0 */
	  CHAR_9: 57, /* 9 */

	  // Alphabet chars.
	  CHAR_UPPERCASE_A: 65, /* A */
	  CHAR_LOWERCASE_A: 97, /* a */
	  CHAR_UPPERCASE_Z: 90, /* Z */
	  CHAR_LOWERCASE_Z: 122, /* z */

	  CHAR_LEFT_PARENTHESES: 40, /* ( */
	  CHAR_RIGHT_PARENTHESES: 41, /* ) */

	  CHAR_ASTERISK: 42, /* * */

	  // Non-alphabetic chars.
	  CHAR_AMPERSAND: 38, /* & */
	  CHAR_AT: 64, /* @ */
	  CHAR_BACKWARD_SLASH: 92, /* \ */
	  CHAR_CARRIAGE_RETURN: 13, /* \r */
	  CHAR_CIRCUMFLEX_ACCENT: 94, /* ^ */
	  CHAR_COLON: 58, /* : */
	  CHAR_COMMA: 44, /* , */
	  CHAR_DOT: 46, /* . */
	  CHAR_DOUBLE_QUOTE: 34, /* " */
	  CHAR_EQUAL: 61, /* = */
	  CHAR_EXCLAMATION_MARK: 33, /* ! */
	  CHAR_FORM_FEED: 12, /* \f */
	  CHAR_FORWARD_SLASH: 47, /* / */
	  CHAR_GRAVE_ACCENT: 96, /* ` */
	  CHAR_HASH: 35, /* # */
	  CHAR_HYPHEN_MINUS: 45, /* - */
	  CHAR_LEFT_ANGLE_BRACKET: 60, /* < */
	  CHAR_LEFT_CURLY_BRACE: 123, /* { */
	  CHAR_LEFT_SQUARE_BRACKET: 91, /* [ */
	  CHAR_LINE_FEED: 10, /* \n */
	  CHAR_NO_BREAK_SPACE: 160, /* \u00A0 */
	  CHAR_PERCENT: 37, /* % */
	  CHAR_PLUS: 43, /* + */
	  CHAR_QUESTION_MARK: 63, /* ? */
	  CHAR_RIGHT_ANGLE_BRACKET: 62, /* > */
	  CHAR_RIGHT_CURLY_BRACE: 125, /* } */
	  CHAR_RIGHT_SQUARE_BRACKET: 93, /* ] */
	  CHAR_SEMICOLON: 59, /* ; */
	  CHAR_SINGLE_QUOTE: 39, /* ' */
	  CHAR_SPACE: 32, /*   */
	  CHAR_TAB: 9, /* \t */
	  CHAR_UNDERSCORE: 95, /* _ */
	  CHAR_VERTICAL_LINE: 124, /* | */
	  CHAR_ZERO_WIDTH_NOBREAK_SPACE: 65279, /* \uFEFF */

	  SEP: path.sep,

	  /**
	   * Create EXTGLOB_CHARS
	   */

	  extglobChars(chars) {
	    return {
	      '!': { type: 'negate', open: '(?:(?!(?:', close: `))${chars.STAR})` },
	      '?': { type: 'qmark', open: '(?:', close: ')?' },
	      '+': { type: 'plus', open: '(?:', close: ')+' },
	      '*': { type: 'star', open: '(?:', close: ')*' },
	      '@': { type: 'at', open: '(?:', close: ')' }
	    };
	  },

	  /**
	   * Create GLOB_CHARS
	   */

	  globChars(win32) {
	    return win32 === true ? WINDOWS_CHARS : POSIX_CHARS;
	  }
	};
	return constants$1;
}

var hasRequiredUtils$2;

function requireUtils$2 () {
	if (hasRequiredUtils$2) return utils$1;
	hasRequiredUtils$2 = 1;
	(function (exports) {

		const path = require$$0$1;
		const win32 = process.platform === 'win32';
		const {
		  REGEX_BACKSLASH,
		  REGEX_REMOVE_BACKSLASH,
		  REGEX_SPECIAL_CHARS,
		  REGEX_SPECIAL_CHARS_GLOBAL
		} = requireConstants$1();

		exports.isObject = val => val !== null && typeof val === 'object' && !Array.isArray(val);
		exports.hasRegexChars = str => REGEX_SPECIAL_CHARS.test(str);
		exports.isRegexChar = str => str.length === 1 && exports.hasRegexChars(str);
		exports.escapeRegex = str => str.replace(REGEX_SPECIAL_CHARS_GLOBAL, '\\$1');
		exports.toPosixSlashes = str => str.replace(REGEX_BACKSLASH, '/');

		exports.removeBackslashes = str => {
		  return str.replace(REGEX_REMOVE_BACKSLASH, match => {
		    return match === '\\' ? '' : match;
		  });
		};

		exports.supportsLookbehinds = () => {
		  const segs = process.version.slice(1).split('.').map(Number);
		  if (segs.length === 3 && segs[0] >= 9 || (segs[0] === 8 && segs[1] >= 10)) {
		    return true;
		  }
		  return false;
		};

		exports.isWindows = options => {
		  if (options && typeof options.windows === 'boolean') {
		    return options.windows;
		  }
		  return win32 === true || path.sep === '\\';
		};

		exports.escapeLast = (input, char, lastIdx) => {
		  const idx = input.lastIndexOf(char, lastIdx);
		  if (idx === -1) return input;
		  if (input[idx - 1] === '\\') return exports.escapeLast(input, char, idx - 1);
		  return `${input.slice(0, idx)}\\${input.slice(idx)}`;
		};

		exports.removePrefix = (input, state = {}) => {
		  let output = input;
		  if (output.startsWith('./')) {
		    output = output.slice(2);
		    state.prefix = './';
		  }
		  return output;
		};

		exports.wrapOutput = (input, state = {}, options = {}) => {
		  const prepend = options.contains ? '' : '^';
		  const append = options.contains ? '' : '$';

		  let output = `${prepend}(?:${input})${append}`;
		  if (state.negated === true) {
		    output = `(?:^(?!${output}).*$)`;
		  }
		  return output;
		}; 
	} (utils$1));
	return utils$1;
}

var scan_1;
var hasRequiredScan;

function requireScan () {
	if (hasRequiredScan) return scan_1;
	hasRequiredScan = 1;

	const utils = requireUtils$2();
	const {
	  CHAR_ASTERISK,             /* * */
	  CHAR_AT,                   /* @ */
	  CHAR_BACKWARD_SLASH,       /* \ */
	  CHAR_COMMA,                /* , */
	  CHAR_DOT,                  /* . */
	  CHAR_EXCLAMATION_MARK,     /* ! */
	  CHAR_FORWARD_SLASH,        /* / */
	  CHAR_LEFT_CURLY_BRACE,     /* { */
	  CHAR_LEFT_PARENTHESES,     /* ( */
	  CHAR_LEFT_SQUARE_BRACKET,  /* [ */
	  CHAR_PLUS,                 /* + */
	  CHAR_QUESTION_MARK,        /* ? */
	  CHAR_RIGHT_CURLY_BRACE,    /* } */
	  CHAR_RIGHT_PARENTHESES,    /* ) */
	  CHAR_RIGHT_SQUARE_BRACKET  /* ] */
	} = requireConstants$1();

	const isPathSeparator = code => {
	  return code === CHAR_FORWARD_SLASH || code === CHAR_BACKWARD_SLASH;
	};

	const depth = token => {
	  if (token.isPrefix !== true) {
	    token.depth = token.isGlobstar ? Infinity : 1;
	  }
	};

	/**
	 * Quickly scans a glob pattern and returns an object with a handful of
	 * useful properties, like `isGlob`, `path` (the leading non-glob, if it exists),
	 * `glob` (the actual pattern), `negated` (true if the path starts with `!` but not
	 * with `!(`) and `negatedExtglob` (true if the path starts with `!(`).
	 *
	 * ```js
	 * const pm = require('picomatch');
	 * console.log(pm.scan('foo/bar/*.js'));
	 * { isGlob: true, input: 'foo/bar/*.js', base: 'foo/bar', glob: '*.js' }
	 * ```
	 * @param {String} `str`
	 * @param {Object} `options`
	 * @return {Object} Returns an object with tokens and regex source string.
	 * @api public
	 */

	const scan = (input, options) => {
	  const opts = options || {};

	  const length = input.length - 1;
	  const scanToEnd = opts.parts === true || opts.scanToEnd === true;
	  const slashes = [];
	  const tokens = [];
	  const parts = [];

	  let str = input;
	  let index = -1;
	  let start = 0;
	  let lastIndex = 0;
	  let isBrace = false;
	  let isBracket = false;
	  let isGlob = false;
	  let isExtglob = false;
	  let isGlobstar = false;
	  let braceEscaped = false;
	  let backslashes = false;
	  let negated = false;
	  let negatedExtglob = false;
	  let finished = false;
	  let braces = 0;
	  let prev;
	  let code;
	  let token = { value: '', depth: 0, isGlob: false };

	  const eos = () => index >= length;
	  const peek = () => str.charCodeAt(index + 1);
	  const advance = () => {
	    prev = code;
	    return str.charCodeAt(++index);
	  };

	  while (index < length) {
	    code = advance();
	    let next;

	    if (code === CHAR_BACKWARD_SLASH) {
	      backslashes = token.backslashes = true;
	      code = advance();

	      if (code === CHAR_LEFT_CURLY_BRACE) {
	        braceEscaped = true;
	      }
	      continue;
	    }

	    if (braceEscaped === true || code === CHAR_LEFT_CURLY_BRACE) {
	      braces++;

	      while (eos() !== true && (code = advance())) {
	        if (code === CHAR_BACKWARD_SLASH) {
	          backslashes = token.backslashes = true;
	          advance();
	          continue;
	        }

	        if (code === CHAR_LEFT_CURLY_BRACE) {
	          braces++;
	          continue;
	        }

	        if (braceEscaped !== true && code === CHAR_DOT && (code = advance()) === CHAR_DOT) {
	          isBrace = token.isBrace = true;
	          isGlob = token.isGlob = true;
	          finished = true;

	          if (scanToEnd === true) {
	            continue;
	          }

	          break;
	        }

	        if (braceEscaped !== true && code === CHAR_COMMA) {
	          isBrace = token.isBrace = true;
	          isGlob = token.isGlob = true;
	          finished = true;

	          if (scanToEnd === true) {
	            continue;
	          }

	          break;
	        }

	        if (code === CHAR_RIGHT_CURLY_BRACE) {
	          braces--;

	          if (braces === 0) {
	            braceEscaped = false;
	            isBrace = token.isBrace = true;
	            finished = true;
	            break;
	          }
	        }
	      }

	      if (scanToEnd === true) {
	        continue;
	      }

	      break;
	    }

	    if (code === CHAR_FORWARD_SLASH) {
	      slashes.push(index);
	      tokens.push(token);
	      token = { value: '', depth: 0, isGlob: false };

	      if (finished === true) continue;
	      if (prev === CHAR_DOT && index === (start + 1)) {
	        start += 2;
	        continue;
	      }

	      lastIndex = index + 1;
	      continue;
	    }

	    if (opts.noext !== true) {
	      const isExtglobChar = code === CHAR_PLUS
	        || code === CHAR_AT
	        || code === CHAR_ASTERISK
	        || code === CHAR_QUESTION_MARK
	        || code === CHAR_EXCLAMATION_MARK;

	      if (isExtglobChar === true && peek() === CHAR_LEFT_PARENTHESES) {
	        isGlob = token.isGlob = true;
	        isExtglob = token.isExtglob = true;
	        finished = true;
	        if (code === CHAR_EXCLAMATION_MARK && index === start) {
	          negatedExtglob = true;
	        }

	        if (scanToEnd === true) {
	          while (eos() !== true && (code = advance())) {
	            if (code === CHAR_BACKWARD_SLASH) {
	              backslashes = token.backslashes = true;
	              code = advance();
	              continue;
	            }

	            if (code === CHAR_RIGHT_PARENTHESES) {
	              isGlob = token.isGlob = true;
	              finished = true;
	              break;
	            }
	          }
	          continue;
	        }
	        break;
	      }
	    }

	    if (code === CHAR_ASTERISK) {
	      if (prev === CHAR_ASTERISK) isGlobstar = token.isGlobstar = true;
	      isGlob = token.isGlob = true;
	      finished = true;

	      if (scanToEnd === true) {
	        continue;
	      }
	      break;
	    }

	    if (code === CHAR_QUESTION_MARK) {
	      isGlob = token.isGlob = true;
	      finished = true;

	      if (scanToEnd === true) {
	        continue;
	      }
	      break;
	    }

	    if (code === CHAR_LEFT_SQUARE_BRACKET) {
	      while (eos() !== true && (next = advance())) {
	        if (next === CHAR_BACKWARD_SLASH) {
	          backslashes = token.backslashes = true;
	          advance();
	          continue;
	        }

	        if (next === CHAR_RIGHT_SQUARE_BRACKET) {
	          isBracket = token.isBracket = true;
	          isGlob = token.isGlob = true;
	          finished = true;
	          break;
	        }
	      }

	      if (scanToEnd === true) {
	        continue;
	      }

	      break;
	    }

	    if (opts.nonegate !== true && code === CHAR_EXCLAMATION_MARK && index === start) {
	      negated = token.negated = true;
	      start++;
	      continue;
	    }

	    if (opts.noparen !== true && code === CHAR_LEFT_PARENTHESES) {
	      isGlob = token.isGlob = true;

	      if (scanToEnd === true) {
	        while (eos() !== true && (code = advance())) {
	          if (code === CHAR_LEFT_PARENTHESES) {
	            backslashes = token.backslashes = true;
	            code = advance();
	            continue;
	          }

	          if (code === CHAR_RIGHT_PARENTHESES) {
	            finished = true;
	            break;
	          }
	        }
	        continue;
	      }
	      break;
	    }

	    if (isGlob === true) {
	      finished = true;

	      if (scanToEnd === true) {
	        continue;
	      }

	      break;
	    }
	  }

	  if (opts.noext === true) {
	    isExtglob = false;
	    isGlob = false;
	  }

	  let base = str;
	  let prefix = '';
	  let glob = '';

	  if (start > 0) {
	    prefix = str.slice(0, start);
	    str = str.slice(start);
	    lastIndex -= start;
	  }

	  if (base && isGlob === true && lastIndex > 0) {
	    base = str.slice(0, lastIndex);
	    glob = str.slice(lastIndex);
	  } else if (isGlob === true) {
	    base = '';
	    glob = str;
	  } else {
	    base = str;
	  }

	  if (base && base !== '' && base !== '/' && base !== str) {
	    if (isPathSeparator(base.charCodeAt(base.length - 1))) {
	      base = base.slice(0, -1);
	    }
	  }

	  if (opts.unescape === true) {
	    if (glob) glob = utils.removeBackslashes(glob);

	    if (base && backslashes === true) {
	      base = utils.removeBackslashes(base);
	    }
	  }

	  const state = {
	    prefix,
	    input,
	    start,
	    base,
	    glob,
	    isBrace,
	    isBracket,
	    isGlob,
	    isExtglob,
	    isGlobstar,
	    negated,
	    negatedExtglob
	  };

	  if (opts.tokens === true) {
	    state.maxDepth = 0;
	    if (!isPathSeparator(code)) {
	      tokens.push(token);
	    }
	    state.tokens = tokens;
	  }

	  if (opts.parts === true || opts.tokens === true) {
	    let prevIndex;

	    for (let idx = 0; idx < slashes.length; idx++) {
	      const n = prevIndex ? prevIndex + 1 : start;
	      const i = slashes[idx];
	      const value = input.slice(n, i);
	      if (opts.tokens) {
	        if (idx === 0 && start !== 0) {
	          tokens[idx].isPrefix = true;
	          tokens[idx].value = prefix;
	        } else {
	          tokens[idx].value = value;
	        }
	        depth(tokens[idx]);
	        state.maxDepth += tokens[idx].depth;
	      }
	      if (idx !== 0 || value !== '') {
	        parts.push(value);
	      }
	      prevIndex = i;
	    }

	    if (prevIndex && prevIndex + 1 < input.length) {
	      const value = input.slice(prevIndex + 1);
	      parts.push(value);

	      if (opts.tokens) {
	        tokens[tokens.length - 1].value = value;
	        depth(tokens[tokens.length - 1]);
	        state.maxDepth += tokens[tokens.length - 1].depth;
	      }
	    }

	    state.slashes = slashes;
	    state.parts = parts;
	  }

	  return state;
	};

	scan_1 = scan;
	return scan_1;
}

var parse_1;
var hasRequiredParse;

function requireParse () {
	if (hasRequiredParse) return parse_1;
	hasRequiredParse = 1;

	const constants = requireConstants$1();
	const utils = requireUtils$2();

	/**
	 * Constants
	 */

	const {
	  MAX_LENGTH,
	  POSIX_REGEX_SOURCE,
	  REGEX_NON_SPECIAL_CHARS,
	  REGEX_SPECIAL_CHARS_BACKREF,
	  REPLACEMENTS
	} = constants;

	/**
	 * Helpers
	 */

	const expandRange = (args, options) => {
	  if (typeof options.expandRange === 'function') {
	    return options.expandRange(...args, options);
	  }

	  args.sort();
	  const value = `[${args.join('-')}]`;

	  try {
	    /* eslint-disable-next-line no-new */
	    new RegExp(value);
	  } catch (ex) {
	    return args.map(v => utils.escapeRegex(v)).join('..');
	  }

	  return value;
	};

	/**
	 * Create the message for a syntax error
	 */

	const syntaxError = (type, char) => {
	  return `Missing ${type}: "${char}" - use "\\\\${char}" to match literal characters`;
	};

	/**
	 * Parse the given input string.
	 * @param {String} input
	 * @param {Object} options
	 * @return {Object}
	 */

	const parse = (input, options) => {
	  if (typeof input !== 'string') {
	    throw new TypeError('Expected a string');
	  }

	  input = REPLACEMENTS[input] || input;

	  const opts = { ...options };
	  const max = typeof opts.maxLength === 'number' ? Math.min(MAX_LENGTH, opts.maxLength) : MAX_LENGTH;

	  let len = input.length;
	  if (len > max) {
	    throw new SyntaxError(`Input length: ${len}, exceeds maximum allowed length: ${max}`);
	  }

	  const bos = { type: 'bos', value: '', output: opts.prepend || '' };
	  const tokens = [bos];

	  const capture = opts.capture ? '' : '?:';
	  const win32 = utils.isWindows(options);

	  // create constants based on platform, for windows or posix
	  const PLATFORM_CHARS = constants.globChars(win32);
	  const EXTGLOB_CHARS = constants.extglobChars(PLATFORM_CHARS);

	  const {
	    DOT_LITERAL,
	    PLUS_LITERAL,
	    SLASH_LITERAL,
	    ONE_CHAR,
	    DOTS_SLASH,
	    NO_DOT,
	    NO_DOT_SLASH,
	    NO_DOTS_SLASH,
	    QMARK,
	    QMARK_NO_DOT,
	    STAR,
	    START_ANCHOR
	  } = PLATFORM_CHARS;

	  const globstar = opts => {
	    return `(${capture}(?:(?!${START_ANCHOR}${opts.dot ? DOTS_SLASH : DOT_LITERAL}).)*?)`;
	  };

	  const nodot = opts.dot ? '' : NO_DOT;
	  const qmarkNoDot = opts.dot ? QMARK : QMARK_NO_DOT;
	  let star = opts.bash === true ? globstar(opts) : STAR;

	  if (opts.capture) {
	    star = `(${star})`;
	  }

	  // minimatch options support
	  if (typeof opts.noext === 'boolean') {
	    opts.noextglob = opts.noext;
	  }

	  const state = {
	    input,
	    index: -1,
	    start: 0,
	    dot: opts.dot === true,
	    consumed: '',
	    output: '',
	    prefix: '',
	    backtrack: false,
	    negated: false,
	    brackets: 0,
	    braces: 0,
	    parens: 0,
	    quotes: 0,
	    globstar: false,
	    tokens
	  };

	  input = utils.removePrefix(input, state);
	  len = input.length;

	  const extglobs = [];
	  const braces = [];
	  const stack = [];
	  let prev = bos;
	  let value;

	  /**
	   * Tokenizing helpers
	   */

	  const eos = () => state.index === len - 1;
	  const peek = state.peek = (n = 1) => input[state.index + n];
	  const advance = state.advance = () => input[++state.index] || '';
	  const remaining = () => input.slice(state.index + 1);
	  const consume = (value = '', num = 0) => {
	    state.consumed += value;
	    state.index += num;
	  };

	  const append = token => {
	    state.output += token.output != null ? token.output : token.value;
	    consume(token.value);
	  };

	  const negate = () => {
	    let count = 1;

	    while (peek() === '!' && (peek(2) !== '(' || peek(3) === '?')) {
	      advance();
	      state.start++;
	      count++;
	    }

	    if (count % 2 === 0) {
	      return false;
	    }

	    state.negated = true;
	    state.start++;
	    return true;
	  };

	  const increment = type => {
	    state[type]++;
	    stack.push(type);
	  };

	  const decrement = type => {
	    state[type]--;
	    stack.pop();
	  };

	  /**
	   * Push tokens onto the tokens array. This helper speeds up
	   * tokenizing by 1) helping us avoid backtracking as much as possible,
	   * and 2) helping us avoid creating extra tokens when consecutive
	   * characters are plain text. This improves performance and simplifies
	   * lookbehinds.
	   */

	  const push = tok => {
	    if (prev.type === 'globstar') {
	      const isBrace = state.braces > 0 && (tok.type === 'comma' || tok.type === 'brace');
	      const isExtglob = tok.extglob === true || (extglobs.length && (tok.type === 'pipe' || tok.type === 'paren'));

	      if (tok.type !== 'slash' && tok.type !== 'paren' && !isBrace && !isExtglob) {
	        state.output = state.output.slice(0, -prev.output.length);
	        prev.type = 'star';
	        prev.value = '*';
	        prev.output = star;
	        state.output += prev.output;
	      }
	    }

	    if (extglobs.length && tok.type !== 'paren') {
	      extglobs[extglobs.length - 1].inner += tok.value;
	    }

	    if (tok.value || tok.output) append(tok);
	    if (prev && prev.type === 'text' && tok.type === 'text') {
	      prev.value += tok.value;
	      prev.output = (prev.output || '') + tok.value;
	      return;
	    }

	    tok.prev = prev;
	    tokens.push(tok);
	    prev = tok;
	  };

	  const extglobOpen = (type, value) => {
	    const token = { ...EXTGLOB_CHARS[value], conditions: 1, inner: '' };

	    token.prev = prev;
	    token.parens = state.parens;
	    token.output = state.output;
	    const output = (opts.capture ? '(' : '') + token.open;

	    increment('parens');
	    push({ type, value, output: state.output ? '' : ONE_CHAR });
	    push({ type: 'paren', extglob: true, value: advance(), output });
	    extglobs.push(token);
	  };

	  const extglobClose = token => {
	    let output = token.close + (opts.capture ? ')' : '');
	    let rest;

	    if (token.type === 'negate') {
	      let extglobStar = star;

	      if (token.inner && token.inner.length > 1 && token.inner.includes('/')) {
	        extglobStar = globstar(opts);
	      }

	      if (extglobStar !== star || eos() || /^\)+$/.test(remaining())) {
	        output = token.close = `)$))${extglobStar}`;
	      }

	      if (token.inner.includes('*') && (rest = remaining()) && /^\.[^\\/.]+$/.test(rest)) {
	        // Any non-magical string (`.ts`) or even nested expression (`.{ts,tsx}`) can follow after the closing parenthesis.
	        // In this case, we need to parse the string and use it in the output of the original pattern.
	        // Suitable patterns: `/!(*.d).ts`, `/!(*.d).{ts,tsx}`, `**/!(*-dbg).@(js)`.
	        //
	        // Disabling the `fastpaths` option due to a problem with parsing strings as `.ts` in the pattern like `**/!(*.d).ts`.
	        const expression = parse(rest, { ...options, fastpaths: false }).output;

	        output = token.close = `)${expression})${extglobStar})`;
	      }

	      if (token.prev.type === 'bos') {
	        state.negatedExtglob = true;
	      }
	    }

	    push({ type: 'paren', extglob: true, value, output });
	    decrement('parens');
	  };

	  /**
	   * Fast paths
	   */

	  if (opts.fastpaths !== false && !/(^[*!]|[/()[\]{}"])/.test(input)) {
	    let backslashes = false;

	    let output = input.replace(REGEX_SPECIAL_CHARS_BACKREF, (m, esc, chars, first, rest, index) => {
	      if (first === '\\') {
	        backslashes = true;
	        return m;
	      }

	      if (first === '?') {
	        if (esc) {
	          return esc + first + (rest ? QMARK.repeat(rest.length) : '');
	        }
	        if (index === 0) {
	          return qmarkNoDot + (rest ? QMARK.repeat(rest.length) : '');
	        }
	        return QMARK.repeat(chars.length);
	      }

	      if (first === '.') {
	        return DOT_LITERAL.repeat(chars.length);
	      }

	      if (first === '*') {
	        if (esc) {
	          return esc + first + (rest ? star : '');
	        }
	        return star;
	      }
	      return esc ? m : `\\${m}`;
	    });

	    if (backslashes === true) {
	      if (opts.unescape === true) {
	        output = output.replace(/\\/g, '');
	      } else {
	        output = output.replace(/\\+/g, m => {
	          return m.length % 2 === 0 ? '\\\\' : (m ? '\\' : '');
	        });
	      }
	    }

	    if (output === input && opts.contains === true) {
	      state.output = input;
	      return state;
	    }

	    state.output = utils.wrapOutput(output, state, options);
	    return state;
	  }

	  /**
	   * Tokenize input until we reach end-of-string
	   */

	  while (!eos()) {
	    value = advance();

	    if (value === '\u0000') {
	      continue;
	    }

	    /**
	     * Escaped characters
	     */

	    if (value === '\\') {
	      const next = peek();

	      if (next === '/' && opts.bash !== true) {
	        continue;
	      }

	      if (next === '.' || next === ';') {
	        continue;
	      }

	      if (!next) {
	        value += '\\';
	        push({ type: 'text', value });
	        continue;
	      }

	      // collapse slashes to reduce potential for exploits
	      const match = /^\\+/.exec(remaining());
	      let slashes = 0;

	      if (match && match[0].length > 2) {
	        slashes = match[0].length;
	        state.index += slashes;
	        if (slashes % 2 !== 0) {
	          value += '\\';
	        }
	      }

	      if (opts.unescape === true) {
	        value = advance();
	      } else {
	        value += advance();
	      }

	      if (state.brackets === 0) {
	        push({ type: 'text', value });
	        continue;
	      }
	    }

	    /**
	     * If we're inside a regex character class, continue
	     * until we reach the closing bracket.
	     */

	    if (state.brackets > 0 && (value !== ']' || prev.value === '[' || prev.value === '[^')) {
	      if (opts.posix !== false && value === ':') {
	        const inner = prev.value.slice(1);
	        if (inner.includes('[')) {
	          prev.posix = true;

	          if (inner.includes(':')) {
	            const idx = prev.value.lastIndexOf('[');
	            const pre = prev.value.slice(0, idx);
	            const rest = prev.value.slice(idx + 2);
	            const posix = POSIX_REGEX_SOURCE[rest];
	            if (posix) {
	              prev.value = pre + posix;
	              state.backtrack = true;
	              advance();

	              if (!bos.output && tokens.indexOf(prev) === 1) {
	                bos.output = ONE_CHAR;
	              }
	              continue;
	            }
	          }
	        }
	      }

	      if ((value === '[' && peek() !== ':') || (value === '-' && peek() === ']')) {
	        value = `\\${value}`;
	      }

	      if (value === ']' && (prev.value === '[' || prev.value === '[^')) {
	        value = `\\${value}`;
	      }

	      if (opts.posix === true && value === '!' && prev.value === '[') {
	        value = '^';
	      }

	      prev.value += value;
	      append({ value });
	      continue;
	    }

	    /**
	     * If we're inside a quoted string, continue
	     * until we reach the closing double quote.
	     */

	    if (state.quotes === 1 && value !== '"') {
	      value = utils.escapeRegex(value);
	      prev.value += value;
	      append({ value });
	      continue;
	    }

	    /**
	     * Double quotes
	     */

	    if (value === '"') {
	      state.quotes = state.quotes === 1 ? 0 : 1;
	      if (opts.keepQuotes === true) {
	        push({ type: 'text', value });
	      }
	      continue;
	    }

	    /**
	     * Parentheses
	     */

	    if (value === '(') {
	      increment('parens');
	      push({ type: 'paren', value });
	      continue;
	    }

	    if (value === ')') {
	      if (state.parens === 0 && opts.strictBrackets === true) {
	        throw new SyntaxError(syntaxError('opening', '('));
	      }

	      const extglob = extglobs[extglobs.length - 1];
	      if (extglob && state.parens === extglob.parens + 1) {
	        extglobClose(extglobs.pop());
	        continue;
	      }

	      push({ type: 'paren', value, output: state.parens ? ')' : '\\)' });
	      decrement('parens');
	      continue;
	    }

	    /**
	     * Square brackets
	     */

	    if (value === '[') {
	      if (opts.nobracket === true || !remaining().includes(']')) {
	        if (opts.nobracket !== true && opts.strictBrackets === true) {
	          throw new SyntaxError(syntaxError('closing', ']'));
	        }

	        value = `\\${value}`;
	      } else {
	        increment('brackets');
	      }

	      push({ type: 'bracket', value });
	      continue;
	    }

	    if (value === ']') {
	      if (opts.nobracket === true || (prev && prev.type === 'bracket' && prev.value.length === 1)) {
	        push({ type: 'text', value, output: `\\${value}` });
	        continue;
	      }

	      if (state.brackets === 0) {
	        if (opts.strictBrackets === true) {
	          throw new SyntaxError(syntaxError('opening', '['));
	        }

	        push({ type: 'text', value, output: `\\${value}` });
	        continue;
	      }

	      decrement('brackets');

	      const prevValue = prev.value.slice(1);
	      if (prev.posix !== true && prevValue[0] === '^' && !prevValue.includes('/')) {
	        value = `/${value}`;
	      }

	      prev.value += value;
	      append({ value });

	      // when literal brackets are explicitly disabled
	      // assume we should match with a regex character class
	      if (opts.literalBrackets === false || utils.hasRegexChars(prevValue)) {
	        continue;
	      }

	      const escaped = utils.escapeRegex(prev.value);
	      state.output = state.output.slice(0, -prev.value.length);

	      // when literal brackets are explicitly enabled
	      // assume we should escape the brackets to match literal characters
	      if (opts.literalBrackets === true) {
	        state.output += escaped;
	        prev.value = escaped;
	        continue;
	      }

	      // when the user specifies nothing, try to match both
	      prev.value = `(${capture}${escaped}|${prev.value})`;
	      state.output += prev.value;
	      continue;
	    }

	    /**
	     * Braces
	     */

	    if (value === '{' && opts.nobrace !== true) {
	      increment('braces');

	      const open = {
	        type: 'brace',
	        value,
	        output: '(',
	        outputIndex: state.output.length,
	        tokensIndex: state.tokens.length
	      };

	      braces.push(open);
	      push(open);
	      continue;
	    }

	    if (value === '}') {
	      const brace = braces[braces.length - 1];

	      if (opts.nobrace === true || !brace) {
	        push({ type: 'text', value, output: value });
	        continue;
	      }

	      let output = ')';

	      if (brace.dots === true) {
	        const arr = tokens.slice();
	        const range = [];

	        for (let i = arr.length - 1; i >= 0; i--) {
	          tokens.pop();
	          if (arr[i].type === 'brace') {
	            break;
	          }
	          if (arr[i].type !== 'dots') {
	            range.unshift(arr[i].value);
	          }
	        }

	        output = expandRange(range, opts);
	        state.backtrack = true;
	      }

	      if (brace.comma !== true && brace.dots !== true) {
	        const out = state.output.slice(0, brace.outputIndex);
	        const toks = state.tokens.slice(brace.tokensIndex);
	        brace.value = brace.output = '\\{';
	        value = output = '\\}';
	        state.output = out;
	        for (const t of toks) {
	          state.output += (t.output || t.value);
	        }
	      }

	      push({ type: 'brace', value, output });
	      decrement('braces');
	      braces.pop();
	      continue;
	    }

	    /**
	     * Pipes
	     */

	    if (value === '|') {
	      if (extglobs.length > 0) {
	        extglobs[extglobs.length - 1].conditions++;
	      }
	      push({ type: 'text', value });
	      continue;
	    }

	    /**
	     * Commas
	     */

	    if (value === ',') {
	      let output = value;

	      const brace = braces[braces.length - 1];
	      if (brace && stack[stack.length - 1] === 'braces') {
	        brace.comma = true;
	        output = '|';
	      }

	      push({ type: 'comma', value, output });
	      continue;
	    }

	    /**
	     * Slashes
	     */

	    if (value === '/') {
	      // if the beginning of the glob is "./", advance the start
	      // to the current index, and don't add the "./" characters
	      // to the state. This greatly simplifies lookbehinds when
	      // checking for BOS characters like "!" and "." (not "./")
	      if (prev.type === 'dot' && state.index === state.start + 1) {
	        state.start = state.index + 1;
	        state.consumed = '';
	        state.output = '';
	        tokens.pop();
	        prev = bos; // reset "prev" to the first token
	        continue;
	      }

	      push({ type: 'slash', value, output: SLASH_LITERAL });
	      continue;
	    }

	    /**
	     * Dots
	     */

	    if (value === '.') {
	      if (state.braces > 0 && prev.type === 'dot') {
	        if (prev.value === '.') prev.output = DOT_LITERAL;
	        const brace = braces[braces.length - 1];
	        prev.type = 'dots';
	        prev.output += value;
	        prev.value += value;
	        brace.dots = true;
	        continue;
	      }

	      if ((state.braces + state.parens) === 0 && prev.type !== 'bos' && prev.type !== 'slash') {
	        push({ type: 'text', value, output: DOT_LITERAL });
	        continue;
	      }

	      push({ type: 'dot', value, output: DOT_LITERAL });
	      continue;
	    }

	    /**
	     * Question marks
	     */

	    if (value === '?') {
	      const isGroup = prev && prev.value === '(';
	      if (!isGroup && opts.noextglob !== true && peek() === '(' && peek(2) !== '?') {
	        extglobOpen('qmark', value);
	        continue;
	      }

	      if (prev && prev.type === 'paren') {
	        const next = peek();
	        let output = value;

	        if (next === '<' && !utils.supportsLookbehinds()) {
	          throw new Error('Node.js v10 or higher is required for regex lookbehinds');
	        }

	        if ((prev.value === '(' && !/[!=<:]/.test(next)) || (next === '<' && !/<([!=]|\w+>)/.test(remaining()))) {
	          output = `\\${value}`;
	        }

	        push({ type: 'text', value, output });
	        continue;
	      }

	      if (opts.dot !== true && (prev.type === 'slash' || prev.type === 'bos')) {
	        push({ type: 'qmark', value, output: QMARK_NO_DOT });
	        continue;
	      }

	      push({ type: 'qmark', value, output: QMARK });
	      continue;
	    }

	    /**
	     * Exclamation
	     */

	    if (value === '!') {
	      if (opts.noextglob !== true && peek() === '(') {
	        if (peek(2) !== '?' || !/[!=<:]/.test(peek(3))) {
	          extglobOpen('negate', value);
	          continue;
	        }
	      }

	      if (opts.nonegate !== true && state.index === 0) {
	        negate();
	        continue;
	      }
	    }

	    /**
	     * Plus
	     */

	    if (value === '+') {
	      if (opts.noextglob !== true && peek() === '(' && peek(2) !== '?') {
	        extglobOpen('plus', value);
	        continue;
	      }

	      if ((prev && prev.value === '(') || opts.regex === false) {
	        push({ type: 'plus', value, output: PLUS_LITERAL });
	        continue;
	      }

	      if ((prev && (prev.type === 'bracket' || prev.type === 'paren' || prev.type === 'brace')) || state.parens > 0) {
	        push({ type: 'plus', value });
	        continue;
	      }

	      push({ type: 'plus', value: PLUS_LITERAL });
	      continue;
	    }

	    /**
	     * Plain text
	     */

	    if (value === '@') {
	      if (opts.noextglob !== true && peek() === '(' && peek(2) !== '?') {
	        push({ type: 'at', extglob: true, value, output: '' });
	        continue;
	      }

	      push({ type: 'text', value });
	      continue;
	    }

	    /**
	     * Plain text
	     */

	    if (value !== '*') {
	      if (value === '$' || value === '^') {
	        value = `\\${value}`;
	      }

	      const match = REGEX_NON_SPECIAL_CHARS.exec(remaining());
	      if (match) {
	        value += match[0];
	        state.index += match[0].length;
	      }

	      push({ type: 'text', value });
	      continue;
	    }

	    /**
	     * Stars
	     */

	    if (prev && (prev.type === 'globstar' || prev.star === true)) {
	      prev.type = 'star';
	      prev.star = true;
	      prev.value += value;
	      prev.output = star;
	      state.backtrack = true;
	      state.globstar = true;
	      consume(value);
	      continue;
	    }

	    let rest = remaining();
	    if (opts.noextglob !== true && /^\([^?]/.test(rest)) {
	      extglobOpen('star', value);
	      continue;
	    }

	    if (prev.type === 'star') {
	      if (opts.noglobstar === true) {
	        consume(value);
	        continue;
	      }

	      const prior = prev.prev;
	      const before = prior.prev;
	      const isStart = prior.type === 'slash' || prior.type === 'bos';
	      const afterStar = before && (before.type === 'star' || before.type === 'globstar');

	      if (opts.bash === true && (!isStart || (rest[0] && rest[0] !== '/'))) {
	        push({ type: 'star', value, output: '' });
	        continue;
	      }

	      const isBrace = state.braces > 0 && (prior.type === 'comma' || prior.type === 'brace');
	      const isExtglob = extglobs.length && (prior.type === 'pipe' || prior.type === 'paren');
	      if (!isStart && prior.type !== 'paren' && !isBrace && !isExtglob) {
	        push({ type: 'star', value, output: '' });
	        continue;
	      }

	      // strip consecutive `/**/`
	      while (rest.slice(0, 3) === '/**') {
	        const after = input[state.index + 4];
	        if (after && after !== '/') {
	          break;
	        }
	        rest = rest.slice(3);
	        consume('/**', 3);
	      }

	      if (prior.type === 'bos' && eos()) {
	        prev.type = 'globstar';
	        prev.value += value;
	        prev.output = globstar(opts);
	        state.output = prev.output;
	        state.globstar = true;
	        consume(value);
	        continue;
	      }

	      if (prior.type === 'slash' && prior.prev.type !== 'bos' && !afterStar && eos()) {
	        state.output = state.output.slice(0, -(prior.output + prev.output).length);
	        prior.output = `(?:${prior.output}`;

	        prev.type = 'globstar';
	        prev.output = globstar(opts) + (opts.strictSlashes ? ')' : '|$)');
	        prev.value += value;
	        state.globstar = true;
	        state.output += prior.output + prev.output;
	        consume(value);
	        continue;
	      }

	      if (prior.type === 'slash' && prior.prev.type !== 'bos' && rest[0] === '/') {
	        const end = rest[1] !== void 0 ? '|$' : '';

	        state.output = state.output.slice(0, -(prior.output + prev.output).length);
	        prior.output = `(?:${prior.output}`;

	        prev.type = 'globstar';
	        prev.output = `${globstar(opts)}${SLASH_LITERAL}|${SLASH_LITERAL}${end})`;
	        prev.value += value;

	        state.output += prior.output + prev.output;
	        state.globstar = true;

	        consume(value + advance());

	        push({ type: 'slash', value: '/', output: '' });
	        continue;
	      }

	      if (prior.type === 'bos' && rest[0] === '/') {
	        prev.type = 'globstar';
	        prev.value += value;
	        prev.output = `(?:^|${SLASH_LITERAL}|${globstar(opts)}${SLASH_LITERAL})`;
	        state.output = prev.output;
	        state.globstar = true;
	        consume(value + advance());
	        push({ type: 'slash', value: '/', output: '' });
	        continue;
	      }

	      // remove single star from output
	      state.output = state.output.slice(0, -prev.output.length);

	      // reset previous token to globstar
	      prev.type = 'globstar';
	      prev.output = globstar(opts);
	      prev.value += value;

	      // reset output with globstar
	      state.output += prev.output;
	      state.globstar = true;
	      consume(value);
	      continue;
	    }

	    const token = { type: 'star', value, output: star };

	    if (opts.bash === true) {
	      token.output = '.*?';
	      if (prev.type === 'bos' || prev.type === 'slash') {
	        token.output = nodot + token.output;
	      }
	      push(token);
	      continue;
	    }

	    if (prev && (prev.type === 'bracket' || prev.type === 'paren') && opts.regex === true) {
	      token.output = value;
	      push(token);
	      continue;
	    }

	    if (state.index === state.start || prev.type === 'slash' || prev.type === 'dot') {
	      if (prev.type === 'dot') {
	        state.output += NO_DOT_SLASH;
	        prev.output += NO_DOT_SLASH;

	      } else if (opts.dot === true) {
	        state.output += NO_DOTS_SLASH;
	        prev.output += NO_DOTS_SLASH;

	      } else {
	        state.output += nodot;
	        prev.output += nodot;
	      }

	      if (peek() !== '*') {
	        state.output += ONE_CHAR;
	        prev.output += ONE_CHAR;
	      }
	    }

	    push(token);
	  }

	  while (state.brackets > 0) {
	    if (opts.strictBrackets === true) throw new SyntaxError(syntaxError('closing', ']'));
	    state.output = utils.escapeLast(state.output, '[');
	    decrement('brackets');
	  }

	  while (state.parens > 0) {
	    if (opts.strictBrackets === true) throw new SyntaxError(syntaxError('closing', ')'));
	    state.output = utils.escapeLast(state.output, '(');
	    decrement('parens');
	  }

	  while (state.braces > 0) {
	    if (opts.strictBrackets === true) throw new SyntaxError(syntaxError('closing', '}'));
	    state.output = utils.escapeLast(state.output, '{');
	    decrement('braces');
	  }

	  if (opts.strictSlashes !== true && (prev.type === 'star' || prev.type === 'bracket')) {
	    push({ type: 'maybe_slash', value: '', output: `${SLASH_LITERAL}?` });
	  }

	  // rebuild the output if we had to backtrack at any point
	  if (state.backtrack === true) {
	    state.output = '';

	    for (const token of state.tokens) {
	      state.output += token.output != null ? token.output : token.value;

	      if (token.suffix) {
	        state.output += token.suffix;
	      }
	    }
	  }

	  return state;
	};

	/**
	 * Fast paths for creating regular expressions for common glob patterns.
	 * This can significantly speed up processing and has very little downside
	 * impact when none of the fast paths match.
	 */

	parse.fastpaths = (input, options) => {
	  const opts = { ...options };
	  const max = typeof opts.maxLength === 'number' ? Math.min(MAX_LENGTH, opts.maxLength) : MAX_LENGTH;
	  const len = input.length;
	  if (len > max) {
	    throw new SyntaxError(`Input length: ${len}, exceeds maximum allowed length: ${max}`);
	  }

	  input = REPLACEMENTS[input] || input;
	  const win32 = utils.isWindows(options);

	  // create constants based on platform, for windows or posix
	  const {
	    DOT_LITERAL,
	    SLASH_LITERAL,
	    ONE_CHAR,
	    DOTS_SLASH,
	    NO_DOT,
	    NO_DOTS,
	    NO_DOTS_SLASH,
	    STAR,
	    START_ANCHOR
	  } = constants.globChars(win32);

	  const nodot = opts.dot ? NO_DOTS : NO_DOT;
	  const slashDot = opts.dot ? NO_DOTS_SLASH : NO_DOT;
	  const capture = opts.capture ? '' : '?:';
	  const state = { negated: false, prefix: '' };
	  let star = opts.bash === true ? '.*?' : STAR;

	  if (opts.capture) {
	    star = `(${star})`;
	  }

	  const globstar = opts => {
	    if (opts.noglobstar === true) return star;
	    return `(${capture}(?:(?!${START_ANCHOR}${opts.dot ? DOTS_SLASH : DOT_LITERAL}).)*?)`;
	  };

	  const create = str => {
	    switch (str) {
	      case '*':
	        return `${nodot}${ONE_CHAR}${star}`;

	      case '.*':
	        return `${DOT_LITERAL}${ONE_CHAR}${star}`;

	      case '*.*':
	        return `${nodot}${star}${DOT_LITERAL}${ONE_CHAR}${star}`;

	      case '*/*':
	        return `${nodot}${star}${SLASH_LITERAL}${ONE_CHAR}${slashDot}${star}`;

	      case '**':
	        return nodot + globstar(opts);

	      case '**/*':
	        return `(?:${nodot}${globstar(opts)}${SLASH_LITERAL})?${slashDot}${ONE_CHAR}${star}`;

	      case '**/*.*':
	        return `(?:${nodot}${globstar(opts)}${SLASH_LITERAL})?${slashDot}${star}${DOT_LITERAL}${ONE_CHAR}${star}`;

	      case '**/.*':
	        return `(?:${nodot}${globstar(opts)}${SLASH_LITERAL})?${DOT_LITERAL}${ONE_CHAR}${star}`;

	      default: {
	        const match = /^(.*?)\.(\w+)$/.exec(str);
	        if (!match) return;

	        const source = create(match[1]);
	        if (!source) return;

	        return source + DOT_LITERAL + match[2];
	      }
	    }
	  };

	  const output = utils.removePrefix(input, state);
	  let source = create(output);

	  if (source && opts.strictSlashes !== true) {
	    source += `${SLASH_LITERAL}?`;
	  }

	  return source;
	};

	parse_1 = parse;
	return parse_1;
}

var picomatch_1;
var hasRequiredPicomatch$1;

function requirePicomatch$1 () {
	if (hasRequiredPicomatch$1) return picomatch_1;
	hasRequiredPicomatch$1 = 1;

	const path = require$$0$1;
	const scan = requireScan();
	const parse = requireParse();
	const utils = requireUtils$2();
	const constants = requireConstants$1();
	const isObject = val => val && typeof val === 'object' && !Array.isArray(val);

	/**
	 * Creates a matcher function from one or more glob patterns. The
	 * returned function takes a string to match as its first argument,
	 * and returns true if the string is a match. The returned matcher
	 * function also takes a boolean as the second argument that, when true,
	 * returns an object with additional information.
	 *
	 * ```js
	 * const picomatch = require('picomatch');
	 * // picomatch(glob[, options]);
	 *
	 * const isMatch = picomatch('*.!(*a)');
	 * console.log(isMatch('a.a')); //=> false
	 * console.log(isMatch('a.b')); //=> true
	 * ```
	 * @name picomatch
	 * @param {String|Array} `globs` One or more glob patterns.
	 * @param {Object=} `options`
	 * @return {Function=} Returns a matcher function.
	 * @api public
	 */

	const picomatch = (glob, options, returnState = false) => {
	  if (Array.isArray(glob)) {
	    const fns = glob.map(input => picomatch(input, options, returnState));
	    const arrayMatcher = str => {
	      for (const isMatch of fns) {
	        const state = isMatch(str);
	        if (state) return state;
	      }
	      return false;
	    };
	    return arrayMatcher;
	  }

	  const isState = isObject(glob) && glob.tokens && glob.input;

	  if (glob === '' || (typeof glob !== 'string' && !isState)) {
	    throw new TypeError('Expected pattern to be a non-empty string');
	  }

	  const opts = options || {};
	  const posix = utils.isWindows(options);
	  const regex = isState
	    ? picomatch.compileRe(glob, options)
	    : picomatch.makeRe(glob, options, false, true);

	  const state = regex.state;
	  delete regex.state;

	  let isIgnored = () => false;
	  if (opts.ignore) {
	    const ignoreOpts = { ...options, ignore: null, onMatch: null, onResult: null };
	    isIgnored = picomatch(opts.ignore, ignoreOpts, returnState);
	  }

	  const matcher = (input, returnObject = false) => {
	    const { isMatch, match, output } = picomatch.test(input, regex, options, { glob, posix });
	    const result = { glob, state, regex, posix, input, output, match, isMatch };

	    if (typeof opts.onResult === 'function') {
	      opts.onResult(result);
	    }

	    if (isMatch === false) {
	      result.isMatch = false;
	      return returnObject ? result : false;
	    }

	    if (isIgnored(input)) {
	      if (typeof opts.onIgnore === 'function') {
	        opts.onIgnore(result);
	      }
	      result.isMatch = false;
	      return returnObject ? result : false;
	    }

	    if (typeof opts.onMatch === 'function') {
	      opts.onMatch(result);
	    }
	    return returnObject ? result : true;
	  };

	  if (returnState) {
	    matcher.state = state;
	  }

	  return matcher;
	};

	/**
	 * Test `input` with the given `regex`. This is used by the main
	 * `picomatch()` function to test the input string.
	 *
	 * ```js
	 * const picomatch = require('picomatch');
	 * // picomatch.test(input, regex[, options]);
	 *
	 * console.log(picomatch.test('foo/bar', /^(?:([^/]*?)\/([^/]*?))$/));
	 * // { isMatch: true, match: [ 'foo/', 'foo', 'bar' ], output: 'foo/bar' }
	 * ```
	 * @param {String} `input` String to test.
	 * @param {RegExp} `regex`
	 * @return {Object} Returns an object with matching info.
	 * @api public
	 */

	picomatch.test = (input, regex, options, { glob, posix } = {}) => {
	  if (typeof input !== 'string') {
	    throw new TypeError('Expected input to be a string');
	  }

	  if (input === '') {
	    return { isMatch: false, output: '' };
	  }

	  const opts = options || {};
	  const format = opts.format || (posix ? utils.toPosixSlashes : null);
	  let match = input === glob;
	  let output = (match && format) ? format(input) : input;

	  if (match === false) {
	    output = format ? format(input) : input;
	    match = output === glob;
	  }

	  if (match === false || opts.capture === true) {
	    if (opts.matchBase === true || opts.basename === true) {
	      match = picomatch.matchBase(input, regex, options, posix);
	    } else {
	      match = regex.exec(output);
	    }
	  }

	  return { isMatch: Boolean(match), match, output };
	};

	/**
	 * Match the basename of a filepath.
	 *
	 * ```js
	 * const picomatch = require('picomatch');
	 * // picomatch.matchBase(input, glob[, options]);
	 * console.log(picomatch.matchBase('foo/bar.js', '*.js'); // true
	 * ```
	 * @param {String} `input` String to test.
	 * @param {RegExp|String} `glob` Glob pattern or regex created by [.makeRe](#makeRe).
	 * @return {Boolean}
	 * @api public
	 */

	picomatch.matchBase = (input, glob, options, posix = utils.isWindows(options)) => {
	  const regex = glob instanceof RegExp ? glob : picomatch.makeRe(glob, options);
	  return regex.test(path.basename(input));
	};

	/**
	 * Returns true if **any** of the given glob `patterns` match the specified `string`.
	 *
	 * ```js
	 * const picomatch = require('picomatch');
	 * // picomatch.isMatch(string, patterns[, options]);
	 *
	 * console.log(picomatch.isMatch('a.a', ['b.*', '*.a'])); //=> true
	 * console.log(picomatch.isMatch('a.a', 'b.*')); //=> false
	 * ```
	 * @param {String|Array} str The string to test.
	 * @param {String|Array} patterns One or more glob patterns to use for matching.
	 * @param {Object} [options] See available [options](#options).
	 * @return {Boolean} Returns true if any patterns match `str`
	 * @api public
	 */

	picomatch.isMatch = (str, patterns, options) => picomatch(patterns, options)(str);

	/**
	 * Parse a glob pattern to create the source string for a regular
	 * expression.
	 *
	 * ```js
	 * const picomatch = require('picomatch');
	 * const result = picomatch.parse(pattern[, options]);
	 * ```
	 * @param {String} `pattern`
	 * @param {Object} `options`
	 * @return {Object} Returns an object with useful properties and output to be used as a regex source string.
	 * @api public
	 */

	picomatch.parse = (pattern, options) => {
	  if (Array.isArray(pattern)) return pattern.map(p => picomatch.parse(p, options));
	  return parse(pattern, { ...options, fastpaths: false });
	};

	/**
	 * Scan a glob pattern to separate the pattern into segments.
	 *
	 * ```js
	 * const picomatch = require('picomatch');
	 * // picomatch.scan(input[, options]);
	 *
	 * const result = picomatch.scan('!./foo/*.js');
	 * console.log(result);
	 * { prefix: '!./',
	 *   input: '!./foo/*.js',
	 *   start: 3,
	 *   base: 'foo',
	 *   glob: '*.js',
	 *   isBrace: false,
	 *   isBracket: false,
	 *   isGlob: true,
	 *   isExtglob: false,
	 *   isGlobstar: false,
	 *   negated: true }
	 * ```
	 * @param {String} `input` Glob pattern to scan.
	 * @param {Object} `options`
	 * @return {Object} Returns an object with
	 * @api public
	 */

	picomatch.scan = (input, options) => scan(input, options);

	/**
	 * Compile a regular expression from the `state` object returned by the
	 * [parse()](#parse) method.
	 *
	 * @param {Object} `state`
	 * @param {Object} `options`
	 * @param {Boolean} `returnOutput` Intended for implementors, this argument allows you to return the raw output from the parser.
	 * @param {Boolean} `returnState` Adds the state to a `state` property on the returned regex. Useful for implementors and debugging.
	 * @return {RegExp}
	 * @api public
	 */

	picomatch.compileRe = (state, options, returnOutput = false, returnState = false) => {
	  if (returnOutput === true) {
	    return state.output;
	  }

	  const opts = options || {};
	  const prepend = opts.contains ? '' : '^';
	  const append = opts.contains ? '' : '$';

	  let source = `${prepend}(?:${state.output})${append}`;
	  if (state && state.negated === true) {
	    source = `^(?!${source}).*$`;
	  }

	  const regex = picomatch.toRegex(source, options);
	  if (returnState === true) {
	    regex.state = state;
	  }

	  return regex;
	};

	/**
	 * Create a regular expression from a parsed glob pattern.
	 *
	 * ```js
	 * const picomatch = require('picomatch');
	 * const state = picomatch.parse('*.js');
	 * // picomatch.compileRe(state[, options]);
	 *
	 * console.log(picomatch.compileRe(state));
	 * //=> /^(?:(?!\.)(?=.)[^/]*?\.js)$/
	 * ```
	 * @param {String} `state` The object returned from the `.parse` method.
	 * @param {Object} `options`
	 * @param {Boolean} `returnOutput` Implementors may use this argument to return the compiled output, instead of a regular expression. This is not exposed on the options to prevent end-users from mutating the result.
	 * @param {Boolean} `returnState` Implementors may use this argument to return the state from the parsed glob with the returned regular expression.
	 * @return {RegExp} Returns a regex created from the given pattern.
	 * @api public
	 */

	picomatch.makeRe = (input, options = {}, returnOutput = false, returnState = false) => {
	  if (!input || typeof input !== 'string') {
	    throw new TypeError('Expected a non-empty string');
	  }

	  let parsed = { negated: false, fastpaths: true };

	  if (options.fastpaths !== false && (input[0] === '.' || input[0] === '*')) {
	    parsed.output = parse.fastpaths(input, options);
	  }

	  if (!parsed.output) {
	    parsed = parse(input, options);
	  }

	  return picomatch.compileRe(parsed, options, returnOutput, returnState);
	};

	/**
	 * Create a regular expression from the given regex source string.
	 *
	 * ```js
	 * const picomatch = require('picomatch');
	 * // picomatch.toRegex(source[, options]);
	 *
	 * const { output } = picomatch.parse('*.js');
	 * console.log(picomatch.toRegex(output));
	 * //=> /^(?:(?!\.)(?=.)[^/]*?\.js)$/
	 * ```
	 * @param {String} `source` Regular expression source string.
	 * @param {Object} `options`
	 * @return {RegExp}
	 * @api public
	 */

	picomatch.toRegex = (source, options) => {
	  try {
	    const opts = options || {};
	    return new RegExp(source, opts.flags || (opts.nocase ? 'i' : ''));
	  } catch (err) {
	    if (options && options.debug === true) throw err;
	    return /$^/;
	  }
	};

	/**
	 * Picomatch constants.
	 * @return {Object}
	 */

	picomatch.constants = constants;

	/**
	 * Expose "picomatch"
	 */

	picomatch_1 = picomatch;
	return picomatch_1;
}

var picomatch;
var hasRequiredPicomatch;

function requirePicomatch () {
	if (hasRequiredPicomatch) return picomatch;
	hasRequiredPicomatch = 1;

	picomatch = requirePicomatch$1();
	return picomatch;
}

var micromatch_1;
var hasRequiredMicromatch;

function requireMicromatch () {
	if (hasRequiredMicromatch) return micromatch_1;
	hasRequiredMicromatch = 1;

	const util = require$$0$2;
	const braces = requireBraces();
	const picomatch = requirePicomatch();
	const utils = requireUtils$2();

	const isEmptyString = v => v === '' || v === './';
	const hasBraces = v => {
	  const index = v.indexOf('{');
	  return index > -1 && v.indexOf('}', index) > -1;
	};

	/**
	 * Returns an array of strings that match one or more glob patterns.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * // mm(list, patterns[, options]);
	 *
	 * console.log(mm(['a.js', 'a.txt'], ['*.js']));
	 * //=> [ 'a.js' ]
	 * ```
	 * @param {String|Array<string>} `list` List of strings to match.
	 * @param {String|Array<string>} `patterns` One or more glob patterns to use for matching.
	 * @param {Object} `options` See available [options](#options)
	 * @return {Array} Returns an array of matches
	 * @summary false
	 * @api public
	 */

	const micromatch = (list, patterns, options) => {
	  patterns = [].concat(patterns);
	  list = [].concat(list);

	  let omit = new Set();
	  let keep = new Set();
	  let items = new Set();
	  let negatives = 0;

	  let onResult = state => {
	    items.add(state.output);
	    if (options && options.onResult) {
	      options.onResult(state);
	    }
	  };

	  for (let i = 0; i < patterns.length; i++) {
	    let isMatch = picomatch(String(patterns[i]), { ...options, onResult }, true);
	    let negated = isMatch.state.negated || isMatch.state.negatedExtglob;
	    if (negated) negatives++;

	    for (let item of list) {
	      let matched = isMatch(item, true);

	      let match = negated ? !matched.isMatch : matched.isMatch;
	      if (!match) continue;

	      if (negated) {
	        omit.add(matched.output);
	      } else {
	        omit.delete(matched.output);
	        keep.add(matched.output);
	      }
	    }
	  }

	  let result = negatives === patterns.length ? [...items] : [...keep];
	  let matches = result.filter(item => !omit.has(item));

	  if (options && matches.length === 0) {
	    if (options.failglob === true) {
	      throw new Error(`No matches found for "${patterns.join(', ')}"`);
	    }

	    if (options.nonull === true || options.nullglob === true) {
	      return options.unescape ? patterns.map(p => p.replace(/\\/g, '')) : patterns;
	    }
	  }

	  return matches;
	};

	/**
	 * Backwards compatibility
	 */

	micromatch.match = micromatch;

	/**
	 * Returns a matcher function from the given glob `pattern` and `options`.
	 * The returned function takes a string to match as its only argument and returns
	 * true if the string is a match.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * // mm.matcher(pattern[, options]);
	 *
	 * const isMatch = mm.matcher('*.!(*a)');
	 * console.log(isMatch('a.a')); //=> false
	 * console.log(isMatch('a.b')); //=> true
	 * ```
	 * @param {String} `pattern` Glob pattern
	 * @param {Object} `options`
	 * @return {Function} Returns a matcher function.
	 * @api public
	 */

	micromatch.matcher = (pattern, options) => picomatch(pattern, options);

	/**
	 * Returns true if **any** of the given glob `patterns` match the specified `string`.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * // mm.isMatch(string, patterns[, options]);
	 *
	 * console.log(mm.isMatch('a.a', ['b.*', '*.a'])); //=> true
	 * console.log(mm.isMatch('a.a', 'b.*')); //=> false
	 * ```
	 * @param {String} `str` The string to test.
	 * @param {String|Array} `patterns` One or more glob patterns to use for matching.
	 * @param {Object} `[options]` See available [options](#options).
	 * @return {Boolean} Returns true if any patterns match `str`
	 * @api public
	 */

	micromatch.isMatch = (str, patterns, options) => picomatch(patterns, options)(str);

	/**
	 * Backwards compatibility
	 */

	micromatch.any = micromatch.isMatch;

	/**
	 * Returns a list of strings that _**do not match any**_ of the given `patterns`.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * // mm.not(list, patterns[, options]);
	 *
	 * console.log(mm.not(['a.a', 'b.b', 'c.c'], '*.a'));
	 * //=> ['b.b', 'c.c']
	 * ```
	 * @param {Array} `list` Array of strings to match.
	 * @param {String|Array} `patterns` One or more glob pattern to use for matching.
	 * @param {Object} `options` See available [options](#options) for changing how matches are performed
	 * @return {Array} Returns an array of strings that **do not match** the given patterns.
	 * @api public
	 */

	micromatch.not = (list, patterns, options = {}) => {
	  patterns = [].concat(patterns).map(String);
	  let result = new Set();
	  let items = [];

	  let onResult = state => {
	    if (options.onResult) options.onResult(state);
	    items.push(state.output);
	  };

	  let matches = new Set(micromatch(list, patterns, { ...options, onResult }));

	  for (let item of items) {
	    if (!matches.has(item)) {
	      result.add(item);
	    }
	  }
	  return [...result];
	};

	/**
	 * Returns true if the given `string` contains the given pattern. Similar
	 * to [.isMatch](#isMatch) but the pattern can match any part of the string.
	 *
	 * ```js
	 * var mm = require('micromatch');
	 * // mm.contains(string, pattern[, options]);
	 *
	 * console.log(mm.contains('aa/bb/cc', '*b'));
	 * //=> true
	 * console.log(mm.contains('aa/bb/cc', '*d'));
	 * //=> false
	 * ```
	 * @param {String} `str` The string to match.
	 * @param {String|Array} `patterns` Glob pattern to use for matching.
	 * @param {Object} `options` See available [options](#options) for changing how matches are performed
	 * @return {Boolean} Returns true if any of the patterns matches any part of `str`.
	 * @api public
	 */

	micromatch.contains = (str, pattern, options) => {
	  if (typeof str !== 'string') {
	    throw new TypeError(`Expected a string: "${util.inspect(str)}"`);
	  }

	  if (Array.isArray(pattern)) {
	    return pattern.some(p => micromatch.contains(str, p, options));
	  }

	  if (typeof pattern === 'string') {
	    if (isEmptyString(str) || isEmptyString(pattern)) {
	      return false;
	    }

	    if (str.includes(pattern) || (str.startsWith('./') && str.slice(2).includes(pattern))) {
	      return true;
	    }
	  }

	  return micromatch.isMatch(str, pattern, { ...options, contains: true });
	};

	/**
	 * Filter the keys of the given object with the given `glob` pattern
	 * and `options`. Does not attempt to match nested keys. If you need this feature,
	 * use [glob-object][] instead.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * // mm.matchKeys(object, patterns[, options]);
	 *
	 * const obj = { aa: 'a', ab: 'b', ac: 'c' };
	 * console.log(mm.matchKeys(obj, '*b'));
	 * //=> { ab: 'b' }
	 * ```
	 * @param {Object} `object` The object with keys to filter.
	 * @param {String|Array} `patterns` One or more glob patterns to use for matching.
	 * @param {Object} `options` See available [options](#options) for changing how matches are performed
	 * @return {Object} Returns an object with only keys that match the given patterns.
	 * @api public
	 */

	micromatch.matchKeys = (obj, patterns, options) => {
	  if (!utils.isObject(obj)) {
	    throw new TypeError('Expected the first argument to be an object');
	  }
	  let keys = micromatch(Object.keys(obj), patterns, options);
	  let res = {};
	  for (let key of keys) res[key] = obj[key];
	  return res;
	};

	/**
	 * Returns true if some of the strings in the given `list` match any of the given glob `patterns`.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * // mm.some(list, patterns[, options]);
	 *
	 * console.log(mm.some(['foo.js', 'bar.js'], ['*.js', '!foo.js']));
	 * // true
	 * console.log(mm.some(['foo.js'], ['*.js', '!foo.js']));
	 * // false
	 * ```
	 * @param {String|Array} `list` The string or array of strings to test. Returns as soon as the first match is found.
	 * @param {String|Array} `patterns` One or more glob patterns to use for matching.
	 * @param {Object} `options` See available [options](#options) for changing how matches are performed
	 * @return {Boolean} Returns true if any `patterns` matches any of the strings in `list`
	 * @api public
	 */

	micromatch.some = (list, patterns, options) => {
	  let items = [].concat(list);

	  for (let pattern of [].concat(patterns)) {
	    let isMatch = picomatch(String(pattern), options);
	    if (items.some(item => isMatch(item))) {
	      return true;
	    }
	  }
	  return false;
	};

	/**
	 * Returns true if every string in the given `list` matches
	 * any of the given glob `patterns`.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * // mm.every(list, patterns[, options]);
	 *
	 * console.log(mm.every('foo.js', ['foo.js']));
	 * // true
	 * console.log(mm.every(['foo.js', 'bar.js'], ['*.js']));
	 * // true
	 * console.log(mm.every(['foo.js', 'bar.js'], ['*.js', '!foo.js']));
	 * // false
	 * console.log(mm.every(['foo.js'], ['*.js', '!foo.js']));
	 * // false
	 * ```
	 * @param {String|Array} `list` The string or array of strings to test.
	 * @param {String|Array} `patterns` One or more glob patterns to use for matching.
	 * @param {Object} `options` See available [options](#options) for changing how matches are performed
	 * @return {Boolean} Returns true if all `patterns` matches all of the strings in `list`
	 * @api public
	 */

	micromatch.every = (list, patterns, options) => {
	  let items = [].concat(list);

	  for (let pattern of [].concat(patterns)) {
	    let isMatch = picomatch(String(pattern), options);
	    if (!items.every(item => isMatch(item))) {
	      return false;
	    }
	  }
	  return true;
	};

	/**
	 * Returns true if **all** of the given `patterns` match
	 * the specified string.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * // mm.all(string, patterns[, options]);
	 *
	 * console.log(mm.all('foo.js', ['foo.js']));
	 * // true
	 *
	 * console.log(mm.all('foo.js', ['*.js', '!foo.js']));
	 * // false
	 *
	 * console.log(mm.all('foo.js', ['*.js', 'foo.js']));
	 * // true
	 *
	 * console.log(mm.all('foo.js', ['*.js', 'f*', '*o*', '*o.js']));
	 * // true
	 * ```
	 * @param {String|Array} `str` The string to test.
	 * @param {String|Array} `patterns` One or more glob patterns to use for matching.
	 * @param {Object} `options` See available [options](#options) for changing how matches are performed
	 * @return {Boolean} Returns true if any patterns match `str`
	 * @api public
	 */

	micromatch.all = (str, patterns, options) => {
	  if (typeof str !== 'string') {
	    throw new TypeError(`Expected a string: "${util.inspect(str)}"`);
	  }

	  return [].concat(patterns).every(p => picomatch(p, options)(str));
	};

	/**
	 * Returns an array of matches captured by `pattern` in `string, or `null` if the pattern did not match.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * // mm.capture(pattern, string[, options]);
	 *
	 * console.log(mm.capture('test/*.js', 'test/foo.js'));
	 * //=> ['foo']
	 * console.log(mm.capture('test/*.js', 'foo/bar.css'));
	 * //=> null
	 * ```
	 * @param {String} `glob` Glob pattern to use for matching.
	 * @param {String} `input` String to match
	 * @param {Object} `options` See available [options](#options) for changing how matches are performed
	 * @return {Array|null} Returns an array of captures if the input matches the glob pattern, otherwise `null`.
	 * @api public
	 */

	micromatch.capture = (glob, input, options) => {
	  let posix = utils.isWindows(options);
	  let regex = picomatch.makeRe(String(glob), { ...options, capture: true });
	  let match = regex.exec(posix ? utils.toPosixSlashes(input) : input);

	  if (match) {
	    return match.slice(1).map(v => v === void 0 ? '' : v);
	  }
	};

	/**
	 * Create a regular expression from the given glob `pattern`.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * // mm.makeRe(pattern[, options]);
	 *
	 * console.log(mm.makeRe('*.js'));
	 * //=> /^(?:(\.[\\\/])?(?!\.)(?=.)[^\/]*?\.js)$/
	 * ```
	 * @param {String} `pattern` A glob pattern to convert to regex.
	 * @param {Object} `options`
	 * @return {RegExp} Returns a regex created from the given pattern.
	 * @api public
	 */

	micromatch.makeRe = (...args) => picomatch.makeRe(...args);

	/**
	 * Scan a glob pattern to separate the pattern into segments. Used
	 * by the [split](#split) method.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * const state = mm.scan(pattern[, options]);
	 * ```
	 * @param {String} `pattern`
	 * @param {Object} `options`
	 * @return {Object} Returns an object with
	 * @api public
	 */

	micromatch.scan = (...args) => picomatch.scan(...args);

	/**
	 * Parse a glob pattern to create the source string for a regular
	 * expression.
	 *
	 * ```js
	 * const mm = require('micromatch');
	 * const state = mm.parse(pattern[, options]);
	 * ```
	 * @param {String} `glob`
	 * @param {Object} `options`
	 * @return {Object} Returns an object with useful properties and output to be used as regex source string.
	 * @api public
	 */

	micromatch.parse = (patterns, options) => {
	  let res = [];
	  for (let pattern of [].concat(patterns || [])) {
	    for (let str of braces(String(pattern), options)) {
	      res.push(picomatch.parse(str, options));
	    }
	  }
	  return res;
	};

	/**
	 * Process the given brace `pattern`.
	 *
	 * ```js
	 * const { braces } = require('micromatch');
	 * console.log(braces('foo/{a,b,c}/bar'));
	 * //=> [ 'foo/(a|b|c)/bar' ]
	 *
	 * console.log(braces('foo/{a,b,c}/bar', { expand: true }));
	 * //=> [ 'foo/a/bar', 'foo/b/bar', 'foo/c/bar' ]
	 * ```
	 * @param {String} `pattern` String with brace pattern to process.
	 * @param {Object} `options` Any [options](#options) to change how expansion is performed. See the [braces][] library for all available options.
	 * @return {Array}
	 * @api public
	 */

	micromatch.braces = (pattern, options) => {
	  if (typeof pattern !== 'string') throw new TypeError('Expected a string');
	  if ((options && options.nobrace === true) || !hasBraces(pattern)) {
	    return [pattern];
	  }
	  return braces(pattern, options);
	};

	/**
	 * Expand braces
	 */

	micromatch.braceExpand = (pattern, options) => {
	  if (typeof pattern !== 'string') throw new TypeError('Expected a string');
	  return micromatch.braces(pattern, { ...options, expand: true });
	};

	/**
	 * Expose micromatch
	 */

	// exposed for tests
	micromatch.hasBraces = hasBraces;
	micromatch_1 = micromatch;
	return micromatch_1;
}

var hasRequiredPattern;

function requirePattern () {
	if (hasRequiredPattern) return pattern;
	hasRequiredPattern = 1;
	Object.defineProperty(pattern, "__esModule", { value: true });
	pattern.isAbsolute = pattern.partitionAbsoluteAndRelative = pattern.removeDuplicateSlashes = pattern.matchAny = pattern.convertPatternsToRe = pattern.makeRe = pattern.getPatternParts = pattern.expandBraceExpansion = pattern.expandPatternsWithBraceExpansion = pattern.isAffectDepthOfReadingPattern = pattern.endsWithSlashGlobStar = pattern.hasGlobStar = pattern.getBaseDirectory = pattern.isPatternRelatedToParentDirectory = pattern.getPatternsOutsideCurrentDirectory = pattern.getPatternsInsideCurrentDirectory = pattern.getPositivePatterns = pattern.getNegativePatterns = pattern.isPositivePattern = pattern.isNegativePattern = pattern.convertToNegativePattern = pattern.convertToPositivePattern = pattern.isDynamicPattern = pattern.isStaticPattern = void 0;
	const path = require$$0$1;
	const globParent = requireGlobParent();
	const micromatch = requireMicromatch();
	const GLOBSTAR = '**';
	const ESCAPE_SYMBOL = '\\';
	const COMMON_GLOB_SYMBOLS_RE = /[*?]|^!/;
	const REGEX_CHARACTER_CLASS_SYMBOLS_RE = /\[[^[]*]/;
	const REGEX_GROUP_SYMBOLS_RE = /(?:^|[^!*+?@])\([^(]*\|[^|]*\)/;
	const GLOB_EXTENSION_SYMBOLS_RE = /[!*+?@]\([^(]*\)/;
	const BRACE_EXPANSION_SEPARATORS_RE = /,|\.\./;
	/**
	 * Matches a sequence of two or more consecutive slashes, excluding the first two slashes at the beginning of the string.
	 * The latter is due to the presence of the device path at the beginning of the UNC path.
	 */
	const DOUBLE_SLASH_RE = /(?!^)\/{2,}/g;
	function isStaticPattern(pattern, options = {}) {
	    return !isDynamicPattern(pattern, options);
	}
	pattern.isStaticPattern = isStaticPattern;
	function isDynamicPattern(pattern, options = {}) {
	    /**
	     * A special case with an empty string is necessary for matching patterns that start with a forward slash.
	     * An empty string cannot be a dynamic pattern.
	     * For example, the pattern `/lib/*` will be spread into parts: '', 'lib', '*'.
	     */
	    if (pattern === '') {
	        return false;
	    }
	    /**
	     * When the `caseSensitiveMatch` option is disabled, all patterns must be marked as dynamic, because we cannot check
	     * filepath directly (without read directory).
	     */
	    if (options.caseSensitiveMatch === false || pattern.includes(ESCAPE_SYMBOL)) {
	        return true;
	    }
	    if (COMMON_GLOB_SYMBOLS_RE.test(pattern) || REGEX_CHARACTER_CLASS_SYMBOLS_RE.test(pattern) || REGEX_GROUP_SYMBOLS_RE.test(pattern)) {
	        return true;
	    }
	    if (options.extglob !== false && GLOB_EXTENSION_SYMBOLS_RE.test(pattern)) {
	        return true;
	    }
	    if (options.braceExpansion !== false && hasBraceExpansion(pattern)) {
	        return true;
	    }
	    return false;
	}
	pattern.isDynamicPattern = isDynamicPattern;
	function hasBraceExpansion(pattern) {
	    const openingBraceIndex = pattern.indexOf('{');
	    if (openingBraceIndex === -1) {
	        return false;
	    }
	    const closingBraceIndex = pattern.indexOf('}', openingBraceIndex + 1);
	    if (closingBraceIndex === -1) {
	        return false;
	    }
	    const braceContent = pattern.slice(openingBraceIndex, closingBraceIndex);
	    return BRACE_EXPANSION_SEPARATORS_RE.test(braceContent);
	}
	function convertToPositivePattern(pattern) {
	    return isNegativePattern(pattern) ? pattern.slice(1) : pattern;
	}
	pattern.convertToPositivePattern = convertToPositivePattern;
	function convertToNegativePattern(pattern) {
	    return '!' + pattern;
	}
	pattern.convertToNegativePattern = convertToNegativePattern;
	function isNegativePattern(pattern) {
	    return pattern.startsWith('!') && pattern[1] !== '(';
	}
	pattern.isNegativePattern = isNegativePattern;
	function isPositivePattern(pattern) {
	    return !isNegativePattern(pattern);
	}
	pattern.isPositivePattern = isPositivePattern;
	function getNegativePatterns(patterns) {
	    return patterns.filter(isNegativePattern);
	}
	pattern.getNegativePatterns = getNegativePatterns;
	function getPositivePatterns(patterns) {
	    return patterns.filter(isPositivePattern);
	}
	pattern.getPositivePatterns = getPositivePatterns;
	/**
	 * Returns patterns that can be applied inside the current directory.
	 *
	 * @example
	 * // ['./*', '*', 'a/*']
	 * getPatternsInsideCurrentDirectory(['./*', '*', 'a/*', '../*', './../*'])
	 */
	function getPatternsInsideCurrentDirectory(patterns) {
	    return patterns.filter((pattern) => !isPatternRelatedToParentDirectory(pattern));
	}
	pattern.getPatternsInsideCurrentDirectory = getPatternsInsideCurrentDirectory;
	/**
	 * Returns patterns to be expanded relative to (outside) the current directory.
	 *
	 * @example
	 * // ['../*', './../*']
	 * getPatternsInsideCurrentDirectory(['./*', '*', 'a/*', '../*', './../*'])
	 */
	function getPatternsOutsideCurrentDirectory(patterns) {
	    return patterns.filter(isPatternRelatedToParentDirectory);
	}
	pattern.getPatternsOutsideCurrentDirectory = getPatternsOutsideCurrentDirectory;
	function isPatternRelatedToParentDirectory(pattern) {
	    return pattern.startsWith('..') || pattern.startsWith('./..');
	}
	pattern.isPatternRelatedToParentDirectory = isPatternRelatedToParentDirectory;
	function getBaseDirectory(pattern) {
	    return globParent(pattern, { flipBackslashes: false });
	}
	pattern.getBaseDirectory = getBaseDirectory;
	function hasGlobStar(pattern) {
	    return pattern.includes(GLOBSTAR);
	}
	pattern.hasGlobStar = hasGlobStar;
	function endsWithSlashGlobStar(pattern) {
	    return pattern.endsWith('/' + GLOBSTAR);
	}
	pattern.endsWithSlashGlobStar = endsWithSlashGlobStar;
	function isAffectDepthOfReadingPattern(pattern) {
	    const basename = path.basename(pattern);
	    return endsWithSlashGlobStar(pattern) || isStaticPattern(basename);
	}
	pattern.isAffectDepthOfReadingPattern = isAffectDepthOfReadingPattern;
	function expandPatternsWithBraceExpansion(patterns) {
	    return patterns.reduce((collection, pattern) => {
	        return collection.concat(expandBraceExpansion(pattern));
	    }, []);
	}
	pattern.expandPatternsWithBraceExpansion = expandPatternsWithBraceExpansion;
	function expandBraceExpansion(pattern) {
	    const patterns = micromatch.braces(pattern, { expand: true, nodupes: true, keepEscaping: true });
	    /**
	     * Sort the patterns by length so that the same depth patterns are processed side by side.
	     * `a/{b,}/{c,}/*` – `['a///*', 'a/b//*', 'a//c/*', 'a/b/c/*']`
	     */
	    patterns.sort((a, b) => a.length - b.length);
	    /**
	     * Micromatch can return an empty string in the case of patterns like `{a,}`.
	     */
	    return patterns.filter((pattern) => pattern !== '');
	}
	pattern.expandBraceExpansion = expandBraceExpansion;
	function getPatternParts(pattern, options) {
	    let { parts } = micromatch.scan(pattern, Object.assign(Object.assign({}, options), { parts: true }));
	    /**
	     * The scan method returns an empty array in some cases.
	     * See micromatch/picomatch#58 for more details.
	     */
	    if (parts.length === 0) {
	        parts = [pattern];
	    }
	    /**
	     * The scan method does not return an empty part for the pattern with a forward slash.
	     * This is another part of micromatch/picomatch#58.
	     */
	    if (parts[0].startsWith('/')) {
	        parts[0] = parts[0].slice(1);
	        parts.unshift('');
	    }
	    return parts;
	}
	pattern.getPatternParts = getPatternParts;
	function makeRe(pattern, options) {
	    return micromatch.makeRe(pattern, options);
	}
	pattern.makeRe = makeRe;
	function convertPatternsToRe(patterns, options) {
	    return patterns.map((pattern) => makeRe(pattern, options));
	}
	pattern.convertPatternsToRe = convertPatternsToRe;
	function matchAny(entry, patternsRe) {
	    return patternsRe.some((patternRe) => patternRe.test(entry));
	}
	pattern.matchAny = matchAny;
	/**
	 * This package only works with forward slashes as a path separator.
	 * Because of this, we cannot use the standard `path.normalize` method, because on Windows platform it will use of backslashes.
	 */
	function removeDuplicateSlashes(pattern) {
	    return pattern.replace(DOUBLE_SLASH_RE, '/');
	}
	pattern.removeDuplicateSlashes = removeDuplicateSlashes;
	function partitionAbsoluteAndRelative(patterns) {
	    const absolute = [];
	    const relative = [];
	    for (const pattern of patterns) {
	        if (isAbsolute(pattern)) {
	            absolute.push(pattern);
	        }
	        else {
	            relative.push(pattern);
	        }
	    }
	    return [absolute, relative];
	}
	pattern.partitionAbsoluteAndRelative = partitionAbsoluteAndRelative;
	function isAbsolute(pattern) {
	    return path.isAbsolute(pattern);
	}
	pattern.isAbsolute = isAbsolute;
	return pattern;
}

var stream$3 = {};

var merge2_1;
var hasRequiredMerge2;

function requireMerge2 () {
	if (hasRequiredMerge2) return merge2_1;
	hasRequiredMerge2 = 1;
	/*
	 * merge2
	 * https://github.com/teambition/merge2
	 *
	 * Copyright (c) 2014-2020 Teambition
	 * Licensed under the MIT license.
	 */
	const Stream = require$$0$3;
	const PassThrough = Stream.PassThrough;
	const slice = Array.prototype.slice;

	merge2_1 = merge2;

	function merge2 () {
	  const streamsQueue = [];
	  const args = slice.call(arguments);
	  let merging = false;
	  let options = args[args.length - 1];

	  if (options && !Array.isArray(options) && options.pipe == null) {
	    args.pop();
	  } else {
	    options = {};
	  }

	  const doEnd = options.end !== false;
	  const doPipeError = options.pipeError === true;
	  if (options.objectMode == null) {
	    options.objectMode = true;
	  }
	  if (options.highWaterMark == null) {
	    options.highWaterMark = 64 * 1024;
	  }
	  const mergedStream = PassThrough(options);

	  function addStream () {
	    for (let i = 0, len = arguments.length; i < len; i++) {
	      streamsQueue.push(pauseStreams(arguments[i], options));
	    }
	    mergeStream();
	    return this
	  }

	  function mergeStream () {
	    if (merging) {
	      return
	    }
	    merging = true;

	    let streams = streamsQueue.shift();
	    if (!streams) {
	      process.nextTick(endStream);
	      return
	    }
	    if (!Array.isArray(streams)) {
	      streams = [streams];
	    }

	    let pipesCount = streams.length + 1;

	    function next () {
	      if (--pipesCount > 0) {
	        return
	      }
	      merging = false;
	      mergeStream();
	    }

	    function pipe (stream) {
	      function onend () {
	        stream.removeListener('merge2UnpipeEnd', onend);
	        stream.removeListener('end', onend);
	        if (doPipeError) {
	          stream.removeListener('error', onerror);
	        }
	        next();
	      }
	      function onerror (err) {
	        mergedStream.emit('error', err);
	      }
	      // skip ended stream
	      if (stream._readableState.endEmitted) {
	        return next()
	      }

	      stream.on('merge2UnpipeEnd', onend);
	      stream.on('end', onend);

	      if (doPipeError) {
	        stream.on('error', onerror);
	      }

	      stream.pipe(mergedStream, { end: false });
	      // compatible for old stream
	      stream.resume();
	    }

	    for (let i = 0; i < streams.length; i++) {
	      pipe(streams[i]);
	    }

	    next();
	  }

	  function endStream () {
	    merging = false;
	    // emit 'queueDrain' when all streams merged.
	    mergedStream.emit('queueDrain');
	    if (doEnd) {
	      mergedStream.end();
	    }
	  }

	  mergedStream.setMaxListeners(0);
	  mergedStream.add = addStream;
	  mergedStream.on('unpipe', function (stream) {
	    stream.emit('merge2UnpipeEnd');
	  });

	  if (args.length) {
	    addStream.apply(null, args);
	  }
	  return mergedStream
	}

	// check and pause streams for pipe.
	function pauseStreams (streams, options) {
	  if (!Array.isArray(streams)) {
	    // Backwards-compat with old-style streams
	    if (!streams._readableState && streams.pipe) {
	      streams = streams.pipe(PassThrough(options));
	    }
	    if (!streams._readableState || !streams.pause || !streams.pipe) {
	      throw new Error('Only readable stream can be merged.')
	    }
	    streams.pause();
	  } else {
	    for (let i = 0, len = streams.length; i < len; i++) {
	      streams[i] = pauseStreams(streams[i], options);
	    }
	  }
	  return streams
	}
	return merge2_1;
}

var hasRequiredStream$3;

function requireStream$3 () {
	if (hasRequiredStream$3) return stream$3;
	hasRequiredStream$3 = 1;
	Object.defineProperty(stream$3, "__esModule", { value: true });
	stream$3.merge = void 0;
	const merge2 = requireMerge2();
	function merge(streams) {
	    const mergedStream = merge2(streams);
	    streams.forEach((stream) => {
	        stream.once('error', (error) => mergedStream.emit('error', error));
	    });
	    mergedStream.once('close', () => propagateCloseEventToSources(streams));
	    mergedStream.once('end', () => propagateCloseEventToSources(streams));
	    return mergedStream;
	}
	stream$3.merge = merge;
	function propagateCloseEventToSources(streams) {
	    streams.forEach((stream) => stream.emit('close'));
	}
	return stream$3;
}

var string = {};

var hasRequiredString;

function requireString () {
	if (hasRequiredString) return string;
	hasRequiredString = 1;
	Object.defineProperty(string, "__esModule", { value: true });
	string.isEmpty = string.isString = void 0;
	function isString(input) {
	    return typeof input === 'string';
	}
	string.isString = isString;
	function isEmpty(input) {
	    return input === '';
	}
	string.isEmpty = isEmpty;
	return string;
}

var hasRequiredUtils$1;

function requireUtils$1 () {
	if (hasRequiredUtils$1) return utils$3;
	hasRequiredUtils$1 = 1;
	Object.defineProperty(utils$3, "__esModule", { value: true });
	utils$3.string = utils$3.stream = utils$3.pattern = utils$3.path = utils$3.fs = utils$3.errno = utils$3.array = void 0;
	const array = requireArray();
	utils$3.array = array;
	const errno = requireErrno();
	utils$3.errno = errno;
	const fs = requireFs$3();
	utils$3.fs = fs;
	const path = requirePath();
	utils$3.path = path;
	const pattern = requirePattern();
	utils$3.pattern = pattern;
	const stream = requireStream$3();
	utils$3.stream = stream;
	const string = requireString();
	utils$3.string = string;
	return utils$3;
}

var hasRequiredTasks;

function requireTasks () {
	if (hasRequiredTasks) return tasks;
	hasRequiredTasks = 1;
	Object.defineProperty(tasks, "__esModule", { value: true });
	tasks.convertPatternGroupToTask = tasks.convertPatternGroupsToTasks = tasks.groupPatternsByBaseDirectory = tasks.getNegativePatternsAsPositive = tasks.getPositivePatterns = tasks.convertPatternsToTasks = tasks.generate = void 0;
	const utils = requireUtils$1();
	function generate(input, settings) {
	    const patterns = processPatterns(input, settings);
	    const ignore = processPatterns(settings.ignore, settings);
	    const positivePatterns = getPositivePatterns(patterns);
	    const negativePatterns = getNegativePatternsAsPositive(patterns, ignore);
	    const staticPatterns = positivePatterns.filter((pattern) => utils.pattern.isStaticPattern(pattern, settings));
	    const dynamicPatterns = positivePatterns.filter((pattern) => utils.pattern.isDynamicPattern(pattern, settings));
	    const staticTasks = convertPatternsToTasks(staticPatterns, negativePatterns, /* dynamic */ false);
	    const dynamicTasks = convertPatternsToTasks(dynamicPatterns, negativePatterns, /* dynamic */ true);
	    return staticTasks.concat(dynamicTasks);
	}
	tasks.generate = generate;
	function processPatterns(input, settings) {
	    let patterns = input;
	    /**
	     * The original pattern like `{,*,**,a/*}` can lead to problems checking the depth when matching entry
	     * and some problems with the micromatch package (see fast-glob issues: #365, #394).
	     *
	     * To solve this problem, we expand all patterns containing brace expansion. This can lead to a slight slowdown
	     * in matching in the case of a large set of patterns after expansion.
	     */
	    if (settings.braceExpansion) {
	        patterns = utils.pattern.expandPatternsWithBraceExpansion(patterns);
	    }
	    /**
	     * If the `baseNameMatch` option is enabled, we must add globstar to patterns, so that they can be used
	     * at any nesting level.
	     *
	     * We do this here, because otherwise we have to complicate the filtering logic. For example, we need to change
	     * the pattern in the filter before creating a regular expression. There is no need to change the patterns
	     * in the application. Only on the input.
	     */
	    if (settings.baseNameMatch) {
	        patterns = patterns.map((pattern) => pattern.includes('/') ? pattern : `**/${pattern}`);
	    }
	    /**
	     * This method also removes duplicate slashes that may have been in the pattern or formed as a result of expansion.
	     */
	    return patterns.map((pattern) => utils.pattern.removeDuplicateSlashes(pattern));
	}
	/**
	 * Returns tasks grouped by basic pattern directories.
	 *
	 * Patterns that can be found inside (`./`) and outside (`../`) the current directory are handled separately.
	 * This is necessary because directory traversal starts at the base directory and goes deeper.
	 */
	function convertPatternsToTasks(positive, negative, dynamic) {
	    const tasks = [];
	    const patternsOutsideCurrentDirectory = utils.pattern.getPatternsOutsideCurrentDirectory(positive);
	    const patternsInsideCurrentDirectory = utils.pattern.getPatternsInsideCurrentDirectory(positive);
	    const outsideCurrentDirectoryGroup = groupPatternsByBaseDirectory(patternsOutsideCurrentDirectory);
	    const insideCurrentDirectoryGroup = groupPatternsByBaseDirectory(patternsInsideCurrentDirectory);
	    tasks.push(...convertPatternGroupsToTasks(outsideCurrentDirectoryGroup, negative, dynamic));
	    /*
	     * For the sake of reducing future accesses to the file system, we merge all tasks within the current directory
	     * into a global task, if at least one pattern refers to the root (`.`). In this case, the global task covers the rest.
	     */
	    if ('.' in insideCurrentDirectoryGroup) {
	        tasks.push(convertPatternGroupToTask('.', patternsInsideCurrentDirectory, negative, dynamic));
	    }
	    else {
	        tasks.push(...convertPatternGroupsToTasks(insideCurrentDirectoryGroup, negative, dynamic));
	    }
	    return tasks;
	}
	tasks.convertPatternsToTasks = convertPatternsToTasks;
	function getPositivePatterns(patterns) {
	    return utils.pattern.getPositivePatterns(patterns);
	}
	tasks.getPositivePatterns = getPositivePatterns;
	function getNegativePatternsAsPositive(patterns, ignore) {
	    const negative = utils.pattern.getNegativePatterns(patterns).concat(ignore);
	    const positive = negative.map(utils.pattern.convertToPositivePattern);
	    return positive;
	}
	tasks.getNegativePatternsAsPositive = getNegativePatternsAsPositive;
	function groupPatternsByBaseDirectory(patterns) {
	    const group = {};
	    return patterns.reduce((collection, pattern) => {
	        const base = utils.pattern.getBaseDirectory(pattern);
	        if (base in collection) {
	            collection[base].push(pattern);
	        }
	        else {
	            collection[base] = [pattern];
	        }
	        return collection;
	    }, group);
	}
	tasks.groupPatternsByBaseDirectory = groupPatternsByBaseDirectory;
	function convertPatternGroupsToTasks(positive, negative, dynamic) {
	    return Object.keys(positive).map((base) => {
	        return convertPatternGroupToTask(base, positive[base], negative, dynamic);
	    });
	}
	tasks.convertPatternGroupsToTasks = convertPatternGroupsToTasks;
	function convertPatternGroupToTask(base, positive, negative, dynamic) {
	    return {
	        dynamic,
	        positive,
	        negative,
	        base,
	        patterns: [].concat(positive, negative.map(utils.pattern.convertToNegativePattern))
	    };
	}
	tasks.convertPatternGroupToTask = convertPatternGroupToTask;
	return tasks;
}

var async$5 = {};

var async$4 = {};

var out$3 = {};

var async$3 = {};

var async$2 = {};

var out$2 = {};

var async$1 = {};

var out$1 = {};

var async = {};

var hasRequiredAsync$5;

function requireAsync$5 () {
	if (hasRequiredAsync$5) return async;
	hasRequiredAsync$5 = 1;
	Object.defineProperty(async, "__esModule", { value: true });
	async.read = void 0;
	function read(path, settings, callback) {
	    settings.fs.lstat(path, (lstatError, lstat) => {
	        if (lstatError !== null) {
	            callFailureCallback(callback, lstatError);
	            return;
	        }
	        if (!lstat.isSymbolicLink() || !settings.followSymbolicLink) {
	            callSuccessCallback(callback, lstat);
	            return;
	        }
	        settings.fs.stat(path, (statError, stat) => {
	            if (statError !== null) {
	                if (settings.throwErrorOnBrokenSymbolicLink) {
	                    callFailureCallback(callback, statError);
	                    return;
	                }
	                callSuccessCallback(callback, lstat);
	                return;
	            }
	            if (settings.markSymbolicLink) {
	                stat.isSymbolicLink = () => true;
	            }
	            callSuccessCallback(callback, stat);
	        });
	    });
	}
	async.read = read;
	function callFailureCallback(callback, error) {
	    callback(error);
	}
	function callSuccessCallback(callback, result) {
	    callback(null, result);
	}
	return async;
}

var sync$5 = {};

var hasRequiredSync$5;

function requireSync$5 () {
	if (hasRequiredSync$5) return sync$5;
	hasRequiredSync$5 = 1;
	Object.defineProperty(sync$5, "__esModule", { value: true });
	sync$5.read = void 0;
	function read(path, settings) {
	    const lstat = settings.fs.lstatSync(path);
	    if (!lstat.isSymbolicLink() || !settings.followSymbolicLink) {
	        return lstat;
	    }
	    try {
	        const stat = settings.fs.statSync(path);
	        if (settings.markSymbolicLink) {
	            stat.isSymbolicLink = () => true;
	        }
	        return stat;
	    }
	    catch (error) {
	        if (!settings.throwErrorOnBrokenSymbolicLink) {
	            return lstat;
	        }
	        throw error;
	    }
	}
	sync$5.read = read;
	return sync$5;
}

var settings$3 = {};

var fs$2 = {};

var hasRequiredFs$2;

function requireFs$2 () {
	if (hasRequiredFs$2) return fs$2;
	hasRequiredFs$2 = 1;
	(function (exports) {
		Object.defineProperty(exports, "__esModule", { value: true });
		exports.createFileSystemAdapter = exports.FILE_SYSTEM_ADAPTER = void 0;
		const fs = require$$0$4;
		exports.FILE_SYSTEM_ADAPTER = {
		    lstat: fs.lstat,
		    stat: fs.stat,
		    lstatSync: fs.lstatSync,
		    statSync: fs.statSync
		};
		function createFileSystemAdapter(fsMethods) {
		    if (fsMethods === undefined) {
		        return exports.FILE_SYSTEM_ADAPTER;
		    }
		    return Object.assign(Object.assign({}, exports.FILE_SYSTEM_ADAPTER), fsMethods);
		}
		exports.createFileSystemAdapter = createFileSystemAdapter; 
	} (fs$2));
	return fs$2;
}

var hasRequiredSettings$3;

function requireSettings$3 () {
	if (hasRequiredSettings$3) return settings$3;
	hasRequiredSettings$3 = 1;
	Object.defineProperty(settings$3, "__esModule", { value: true });
	const fs = requireFs$2();
	class Settings {
	    constructor(_options = {}) {
	        this._options = _options;
	        this.followSymbolicLink = this._getValue(this._options.followSymbolicLink, true);
	        this.fs = fs.createFileSystemAdapter(this._options.fs);
	        this.markSymbolicLink = this._getValue(this._options.markSymbolicLink, false);
	        this.throwErrorOnBrokenSymbolicLink = this._getValue(this._options.throwErrorOnBrokenSymbolicLink, true);
	    }
	    _getValue(option, value) {
	        return option !== null && option !== void 0 ? option : value;
	    }
	}
	settings$3.default = Settings;
	return settings$3;
}

var hasRequiredOut$3;

function requireOut$3 () {
	if (hasRequiredOut$3) return out$1;
	hasRequiredOut$3 = 1;
	Object.defineProperty(out$1, "__esModule", { value: true });
	out$1.statSync = out$1.stat = out$1.Settings = void 0;
	const async = requireAsync$5();
	const sync = requireSync$5();
	const settings_1 = requireSettings$3();
	out$1.Settings = settings_1.default;
	function stat(path, optionsOrSettingsOrCallback, callback) {
	    if (typeof optionsOrSettingsOrCallback === 'function') {
	        async.read(path, getSettings(), optionsOrSettingsOrCallback);
	        return;
	    }
	    async.read(path, getSettings(optionsOrSettingsOrCallback), callback);
	}
	out$1.stat = stat;
	function statSync(path, optionsOrSettings) {
	    const settings = getSettings(optionsOrSettings);
	    return sync.read(path, settings);
	}
	out$1.statSync = statSync;
	function getSettings(settingsOrOptions = {}) {
	    if (settingsOrOptions instanceof settings_1.default) {
	        return settingsOrOptions;
	    }
	    return new settings_1.default(settingsOrOptions);
	}
	return out$1;
}

/*! queue-microtask. MIT License. Feross Aboukhadijeh <https://feross.org/opensource> */

var queueMicrotask_1;
var hasRequiredQueueMicrotask;

function requireQueueMicrotask () {
	if (hasRequiredQueueMicrotask) return queueMicrotask_1;
	hasRequiredQueueMicrotask = 1;
	let promise;

	queueMicrotask_1 = typeof queueMicrotask === 'function'
	  ? queueMicrotask.bind(typeof window !== 'undefined' ? window : commonjsGlobal)
	  // reuse resolved promise, and allocate it lazily
	  : cb => (promise || (promise = Promise.resolve()))
	    .then(cb)
	    .catch(err => setTimeout(() => { throw err }, 0));
	return queueMicrotask_1;
}

/*! run-parallel. MIT License. Feross Aboukhadijeh <https://feross.org/opensource> */

var runParallel_1;
var hasRequiredRunParallel;

function requireRunParallel () {
	if (hasRequiredRunParallel) return runParallel_1;
	hasRequiredRunParallel = 1;
	runParallel_1 = runParallel;

	const queueMicrotask = requireQueueMicrotask();

	function runParallel (tasks, cb) {
	  let results, pending, keys;
	  let isSync = true;

	  if (Array.isArray(tasks)) {
	    results = [];
	    pending = tasks.length;
	  } else {
	    keys = Object.keys(tasks);
	    results = {};
	    pending = keys.length;
	  }

	  function done (err) {
	    function end () {
	      if (cb) cb(err, results);
	      cb = null;
	    }
	    if (isSync) queueMicrotask(end);
	    else end();
	  }

	  function each (i, err, result) {
	    results[i] = result;
	    if (--pending === 0 || err) {
	      done(err);
	    }
	  }

	  if (!pending) {
	    // empty
	    done(null);
	  } else if (keys) {
	    // object
	    keys.forEach(function (key) {
	      tasks[key](function (err, result) { each(key, err, result); });
	    });
	  } else {
	    // array
	    tasks.forEach(function (task, i) {
	      task(function (err, result) { each(i, err, result); });
	    });
	  }

	  isSync = false;
	}
	return runParallel_1;
}

var constants = {};

var hasRequiredConstants;

function requireConstants () {
	if (hasRequiredConstants) return constants;
	hasRequiredConstants = 1;
	Object.defineProperty(constants, "__esModule", { value: true });
	constants.IS_SUPPORT_READDIR_WITH_FILE_TYPES = void 0;
	const NODE_PROCESS_VERSION_PARTS = process.versions.node.split('.');
	if (NODE_PROCESS_VERSION_PARTS[0] === undefined || NODE_PROCESS_VERSION_PARTS[1] === undefined) {
	    throw new Error(`Unexpected behavior. The 'process.versions.node' variable has invalid value: ${process.versions.node}`);
	}
	const MAJOR_VERSION = Number.parseInt(NODE_PROCESS_VERSION_PARTS[0], 10);
	const MINOR_VERSION = Number.parseInt(NODE_PROCESS_VERSION_PARTS[1], 10);
	const SUPPORTED_MAJOR_VERSION = 10;
	const SUPPORTED_MINOR_VERSION = 10;
	const IS_MATCHED_BY_MAJOR = MAJOR_VERSION > SUPPORTED_MAJOR_VERSION;
	const IS_MATCHED_BY_MAJOR_AND_MINOR = MAJOR_VERSION === SUPPORTED_MAJOR_VERSION && MINOR_VERSION >= SUPPORTED_MINOR_VERSION;
	/**
	 * IS `true` for Node.js 10.10 and greater.
	 */
	constants.IS_SUPPORT_READDIR_WITH_FILE_TYPES = IS_MATCHED_BY_MAJOR || IS_MATCHED_BY_MAJOR_AND_MINOR;
	return constants;
}

var utils = {};

var fs$1 = {};

var hasRequiredFs$1;

function requireFs$1 () {
	if (hasRequiredFs$1) return fs$1;
	hasRequiredFs$1 = 1;
	Object.defineProperty(fs$1, "__esModule", { value: true });
	fs$1.createDirentFromStats = void 0;
	class DirentFromStats {
	    constructor(name, stats) {
	        this.name = name;
	        this.isBlockDevice = stats.isBlockDevice.bind(stats);
	        this.isCharacterDevice = stats.isCharacterDevice.bind(stats);
	        this.isDirectory = stats.isDirectory.bind(stats);
	        this.isFIFO = stats.isFIFO.bind(stats);
	        this.isFile = stats.isFile.bind(stats);
	        this.isSocket = stats.isSocket.bind(stats);
	        this.isSymbolicLink = stats.isSymbolicLink.bind(stats);
	    }
	}
	function createDirentFromStats(name, stats) {
	    return new DirentFromStats(name, stats);
	}
	fs$1.createDirentFromStats = createDirentFromStats;
	return fs$1;
}

var hasRequiredUtils;

function requireUtils () {
	if (hasRequiredUtils) return utils;
	hasRequiredUtils = 1;
	Object.defineProperty(utils, "__esModule", { value: true });
	utils.fs = void 0;
	const fs = requireFs$1();
	utils.fs = fs;
	return utils;
}

var common$1 = {};

var hasRequiredCommon$1;

function requireCommon$1 () {
	if (hasRequiredCommon$1) return common$1;
	hasRequiredCommon$1 = 1;
	Object.defineProperty(common$1, "__esModule", { value: true });
	common$1.joinPathSegments = void 0;
	function joinPathSegments(a, b, separator) {
	    /**
	     * The correct handling of cases when the first segment is a root (`/`, `C:/`) or UNC path (`//?/C:/`).
	     */
	    if (a.endsWith(separator)) {
	        return a + b;
	    }
	    return a + separator + b;
	}
	common$1.joinPathSegments = joinPathSegments;
	return common$1;
}

var hasRequiredAsync$4;

function requireAsync$4 () {
	if (hasRequiredAsync$4) return async$1;
	hasRequiredAsync$4 = 1;
	Object.defineProperty(async$1, "__esModule", { value: true });
	async$1.readdir = async$1.readdirWithFileTypes = async$1.read = void 0;
	const fsStat = requireOut$3();
	const rpl = requireRunParallel();
	const constants_1 = requireConstants();
	const utils = requireUtils();
	const common = requireCommon$1();
	function read(directory, settings, callback) {
	    if (!settings.stats && constants_1.IS_SUPPORT_READDIR_WITH_FILE_TYPES) {
	        readdirWithFileTypes(directory, settings, callback);
	        return;
	    }
	    readdir(directory, settings, callback);
	}
	async$1.read = read;
	function readdirWithFileTypes(directory, settings, callback) {
	    settings.fs.readdir(directory, { withFileTypes: true }, (readdirError, dirents) => {
	        if (readdirError !== null) {
	            callFailureCallback(callback, readdirError);
	            return;
	        }
	        const entries = dirents.map((dirent) => ({
	            dirent,
	            name: dirent.name,
	            path: common.joinPathSegments(directory, dirent.name, settings.pathSegmentSeparator)
	        }));
	        if (!settings.followSymbolicLinks) {
	            callSuccessCallback(callback, entries);
	            return;
	        }
	        const tasks = entries.map((entry) => makeRplTaskEntry(entry, settings));
	        rpl(tasks, (rplError, rplEntries) => {
	            if (rplError !== null) {
	                callFailureCallback(callback, rplError);
	                return;
	            }
	            callSuccessCallback(callback, rplEntries);
	        });
	    });
	}
	async$1.readdirWithFileTypes = readdirWithFileTypes;
	function makeRplTaskEntry(entry, settings) {
	    return (done) => {
	        if (!entry.dirent.isSymbolicLink()) {
	            done(null, entry);
	            return;
	        }
	        settings.fs.stat(entry.path, (statError, stats) => {
	            if (statError !== null) {
	                if (settings.throwErrorOnBrokenSymbolicLink) {
	                    done(statError);
	                    return;
	                }
	                done(null, entry);
	                return;
	            }
	            entry.dirent = utils.fs.createDirentFromStats(entry.name, stats);
	            done(null, entry);
	        });
	    };
	}
	function readdir(directory, settings, callback) {
	    settings.fs.readdir(directory, (readdirError, names) => {
	        if (readdirError !== null) {
	            callFailureCallback(callback, readdirError);
	            return;
	        }
	        const tasks = names.map((name) => {
	            const path = common.joinPathSegments(directory, name, settings.pathSegmentSeparator);
	            return (done) => {
	                fsStat.stat(path, settings.fsStatSettings, (error, stats) => {
	                    if (error !== null) {
	                        done(error);
	                        return;
	                    }
	                    const entry = {
	                        name,
	                        path,
	                        dirent: utils.fs.createDirentFromStats(name, stats)
	                    };
	                    if (settings.stats) {
	                        entry.stats = stats;
	                    }
	                    done(null, entry);
	                });
	            };
	        });
	        rpl(tasks, (rplError, entries) => {
	            if (rplError !== null) {
	                callFailureCallback(callback, rplError);
	                return;
	            }
	            callSuccessCallback(callback, entries);
	        });
	    });
	}
	async$1.readdir = readdir;
	function callFailureCallback(callback, error) {
	    callback(error);
	}
	function callSuccessCallback(callback, result) {
	    callback(null, result);
	}
	return async$1;
}

var sync$4 = {};

var hasRequiredSync$4;

function requireSync$4 () {
	if (hasRequiredSync$4) return sync$4;
	hasRequiredSync$4 = 1;
	Object.defineProperty(sync$4, "__esModule", { value: true });
	sync$4.readdir = sync$4.readdirWithFileTypes = sync$4.read = void 0;
	const fsStat = requireOut$3();
	const constants_1 = requireConstants();
	const utils = requireUtils();
	const common = requireCommon$1();
	function read(directory, settings) {
	    if (!settings.stats && constants_1.IS_SUPPORT_READDIR_WITH_FILE_TYPES) {
	        return readdirWithFileTypes(directory, settings);
	    }
	    return readdir(directory, settings);
	}
	sync$4.read = read;
	function readdirWithFileTypes(directory, settings) {
	    const dirents = settings.fs.readdirSync(directory, { withFileTypes: true });
	    return dirents.map((dirent) => {
	        const entry = {
	            dirent,
	            name: dirent.name,
	            path: common.joinPathSegments(directory, dirent.name, settings.pathSegmentSeparator)
	        };
	        if (entry.dirent.isSymbolicLink() && settings.followSymbolicLinks) {
	            try {
	                const stats = settings.fs.statSync(entry.path);
	                entry.dirent = utils.fs.createDirentFromStats(entry.name, stats);
	            }
	            catch (error) {
	                if (settings.throwErrorOnBrokenSymbolicLink) {
	                    throw error;
	                }
	            }
	        }
	        return entry;
	    });
	}
	sync$4.readdirWithFileTypes = readdirWithFileTypes;
	function readdir(directory, settings) {
	    const names = settings.fs.readdirSync(directory);
	    return names.map((name) => {
	        const entryPath = common.joinPathSegments(directory, name, settings.pathSegmentSeparator);
	        const stats = fsStat.statSync(entryPath, settings.fsStatSettings);
	        const entry = {
	            name,
	            path: entryPath,
	            dirent: utils.fs.createDirentFromStats(name, stats)
	        };
	        if (settings.stats) {
	            entry.stats = stats;
	        }
	        return entry;
	    });
	}
	sync$4.readdir = readdir;
	return sync$4;
}

var settings$2 = {};

var fs = {};

var hasRequiredFs;

function requireFs () {
	if (hasRequiredFs) return fs;
	hasRequiredFs = 1;
	(function (exports) {
		Object.defineProperty(exports, "__esModule", { value: true });
		exports.createFileSystemAdapter = exports.FILE_SYSTEM_ADAPTER = void 0;
		const fs = require$$0$4;
		exports.FILE_SYSTEM_ADAPTER = {
		    lstat: fs.lstat,
		    stat: fs.stat,
		    lstatSync: fs.lstatSync,
		    statSync: fs.statSync,
		    readdir: fs.readdir,
		    readdirSync: fs.readdirSync
		};
		function createFileSystemAdapter(fsMethods) {
		    if (fsMethods === undefined) {
		        return exports.FILE_SYSTEM_ADAPTER;
		    }
		    return Object.assign(Object.assign({}, exports.FILE_SYSTEM_ADAPTER), fsMethods);
		}
		exports.createFileSystemAdapter = createFileSystemAdapter; 
	} (fs));
	return fs;
}

var hasRequiredSettings$2;

function requireSettings$2 () {
	if (hasRequiredSettings$2) return settings$2;
	hasRequiredSettings$2 = 1;
	Object.defineProperty(settings$2, "__esModule", { value: true });
	const path = require$$0$1;
	const fsStat = requireOut$3();
	const fs = requireFs();
	class Settings {
	    constructor(_options = {}) {
	        this._options = _options;
	        this.followSymbolicLinks = this._getValue(this._options.followSymbolicLinks, false);
	        this.fs = fs.createFileSystemAdapter(this._options.fs);
	        this.pathSegmentSeparator = this._getValue(this._options.pathSegmentSeparator, path.sep);
	        this.stats = this._getValue(this._options.stats, false);
	        this.throwErrorOnBrokenSymbolicLink = this._getValue(this._options.throwErrorOnBrokenSymbolicLink, true);
	        this.fsStatSettings = new fsStat.Settings({
	            followSymbolicLink: this.followSymbolicLinks,
	            fs: this.fs,
	            throwErrorOnBrokenSymbolicLink: this.throwErrorOnBrokenSymbolicLink
	        });
	    }
	    _getValue(option, value) {
	        return option !== null && option !== void 0 ? option : value;
	    }
	}
	settings$2.default = Settings;
	return settings$2;
}

var hasRequiredOut$2;

function requireOut$2 () {
	if (hasRequiredOut$2) return out$2;
	hasRequiredOut$2 = 1;
	Object.defineProperty(out$2, "__esModule", { value: true });
	out$2.Settings = out$2.scandirSync = out$2.scandir = void 0;
	const async = requireAsync$4();
	const sync = requireSync$4();
	const settings_1 = requireSettings$2();
	out$2.Settings = settings_1.default;
	function scandir(path, optionsOrSettingsOrCallback, callback) {
	    if (typeof optionsOrSettingsOrCallback === 'function') {
	        async.read(path, getSettings(), optionsOrSettingsOrCallback);
	        return;
	    }
	    async.read(path, getSettings(optionsOrSettingsOrCallback), callback);
	}
	out$2.scandir = scandir;
	function scandirSync(path, optionsOrSettings) {
	    const settings = getSettings(optionsOrSettings);
	    return sync.read(path, settings);
	}
	out$2.scandirSync = scandirSync;
	function getSettings(settingsOrOptions = {}) {
	    if (settingsOrOptions instanceof settings_1.default) {
	        return settingsOrOptions;
	    }
	    return new settings_1.default(settingsOrOptions);
	}
	return out$2;
}

var queue = {exports: {}};

var reusify_1;
var hasRequiredReusify;

function requireReusify () {
	if (hasRequiredReusify) return reusify_1;
	hasRequiredReusify = 1;

	function reusify (Constructor) {
	  var head = new Constructor();
	  var tail = head;

	  function get () {
	    var current = head;

	    if (current.next) {
	      head = current.next;
	    } else {
	      head = new Constructor();
	      tail = head;
	    }

	    current.next = null;

	    return current
	  }

	  function release (obj) {
	    tail.next = obj;
	    tail = obj;
	  }

	  return {
	    get: get,
	    release: release
	  }
	}

	reusify_1 = reusify;
	return reusify_1;
}

var hasRequiredQueue;

function requireQueue () {
	if (hasRequiredQueue) return queue.exports;
	hasRequiredQueue = 1;

	/* eslint-disable no-var */

	var reusify = requireReusify();

	function fastqueue (context, worker, _concurrency) {
	  if (typeof context === 'function') {
	    _concurrency = worker;
	    worker = context;
	    context = null;
	  }

	  if (!(_concurrency >= 1)) {
	    throw new Error('fastqueue concurrency must be equal to or greater than 1')
	  }

	  var cache = reusify(Task);
	  var queueHead = null;
	  var queueTail = null;
	  var _running = 0;
	  var errorHandler = null;

	  var self = {
	    push: push,
	    drain: noop,
	    saturated: noop,
	    pause: pause,
	    paused: false,

	    get concurrency () {
	      return _concurrency
	    },
	    set concurrency (value) {
	      if (!(value >= 1)) {
	        throw new Error('fastqueue concurrency must be equal to or greater than 1')
	      }
	      _concurrency = value;

	      if (self.paused) return
	      for (; queueHead && _running < _concurrency;) {
	        _running++;
	        release();
	      }
	    },

	    running: running,
	    resume: resume,
	    idle: idle,
	    length: length,
	    getQueue: getQueue,
	    unshift: unshift,
	    empty: noop,
	    kill: kill,
	    killAndDrain: killAndDrain,
	    error: error
	  };

	  return self

	  function running () {
	    return _running
	  }

	  function pause () {
	    self.paused = true;
	  }

	  function length () {
	    var current = queueHead;
	    var counter = 0;

	    while (current) {
	      current = current.next;
	      counter++;
	    }

	    return counter
	  }

	  function getQueue () {
	    var current = queueHead;
	    var tasks = [];

	    while (current) {
	      tasks.push(current.value);
	      current = current.next;
	    }

	    return tasks
	  }

	  function resume () {
	    if (!self.paused) return
	    self.paused = false;
	    if (queueHead === null) {
	      _running++;
	      release();
	      return
	    }
	    for (; queueHead && _running < _concurrency;) {
	      _running++;
	      release();
	    }
	  }

	  function idle () {
	    return _running === 0 && self.length() === 0
	  }

	  function push (value, done) {
	    var current = cache.get();

	    current.context = context;
	    current.release = release;
	    current.value = value;
	    current.callback = done || noop;
	    current.errorHandler = errorHandler;

	    if (_running >= _concurrency || self.paused) {
	      if (queueTail) {
	        queueTail.next = current;
	        queueTail = current;
	      } else {
	        queueHead = current;
	        queueTail = current;
	        self.saturated();
	      }
	    } else {
	      _running++;
	      worker.call(context, current.value, current.worked);
	    }
	  }

	  function unshift (value, done) {
	    var current = cache.get();

	    current.context = context;
	    current.release = release;
	    current.value = value;
	    current.callback = done || noop;
	    current.errorHandler = errorHandler;

	    if (_running >= _concurrency || self.paused) {
	      if (queueHead) {
	        current.next = queueHead;
	        queueHead = current;
	      } else {
	        queueHead = current;
	        queueTail = current;
	        self.saturated();
	      }
	    } else {
	      _running++;
	      worker.call(context, current.value, current.worked);
	    }
	  }

	  function release (holder) {
	    if (holder) {
	      cache.release(holder);
	    }
	    var next = queueHead;
	    if (next && _running <= _concurrency) {
	      if (!self.paused) {
	        if (queueTail === queueHead) {
	          queueTail = null;
	        }
	        queueHead = next.next;
	        next.next = null;
	        worker.call(context, next.value, next.worked);
	        if (queueTail === null) {
	          self.empty();
	        }
	      } else {
	        _running--;
	      }
	    } else if (--_running === 0) {
	      self.drain();
	    }
	  }

	  function kill () {
	    queueHead = null;
	    queueTail = null;
	    self.drain = noop;
	  }

	  function killAndDrain () {
	    queueHead = null;
	    queueTail = null;
	    self.drain();
	    self.drain = noop;
	  }

	  function error (handler) {
	    errorHandler = handler;
	  }
	}

	function noop () {}

	function Task () {
	  this.value = null;
	  this.callback = noop;
	  this.next = null;
	  this.release = noop;
	  this.context = null;
	  this.errorHandler = null;

	  var self = this;

	  this.worked = function worked (err, result) {
	    var callback = self.callback;
	    var errorHandler = self.errorHandler;
	    var val = self.value;
	    self.value = null;
	    self.callback = noop;
	    if (self.errorHandler) {
	      errorHandler(err, val);
	    }
	    callback.call(self.context, err, result);
	    self.release(self);
	  };
	}

	function queueAsPromised (context, worker, _concurrency) {
	  if (typeof context === 'function') {
	    _concurrency = worker;
	    worker = context;
	    context = null;
	  }

	  function asyncWrapper (arg, cb) {
	    worker.call(this, arg)
	      .then(function (res) {
	        cb(null, res);
	      }, cb);
	  }

	  var queue = fastqueue(context, asyncWrapper, _concurrency);

	  var pushCb = queue.push;
	  var unshiftCb = queue.unshift;

	  queue.push = push;
	  queue.unshift = unshift;
	  queue.drained = drained;

	  return queue

	  function push (value) {
	    var p = new Promise(function (resolve, reject) {
	      pushCb(value, function (err, result) {
	        if (err) {
	          reject(err);
	          return
	        }
	        resolve(result);
	      });
	    });

	    // Let's fork the promise chain to
	    // make the error bubble up to the user but
	    // not lead to a unhandledRejection
	    p.catch(noop);

	    return p
	  }

	  function unshift (value) {
	    var p = new Promise(function (resolve, reject) {
	      unshiftCb(value, function (err, result) {
	        if (err) {
	          reject(err);
	          return
	        }
	        resolve(result);
	      });
	    });

	    // Let's fork the promise chain to
	    // make the error bubble up to the user but
	    // not lead to a unhandledRejection
	    p.catch(noop);

	    return p
	  }

	  function drained () {
	    var p = new Promise(function (resolve) {
	      process.nextTick(function () {
	        if (queue.idle()) {
	          resolve();
	        } else {
	          var previousDrain = queue.drain;
	          queue.drain = function () {
	            if (typeof previousDrain === 'function') previousDrain();
	            resolve();
	            queue.drain = previousDrain;
	          };
	        }
	      });
	    });

	    return p
	  }
	}

	queue.exports = fastqueue;
	queue.exports.promise = queueAsPromised;
	return queue.exports;
}

var common = {};

var hasRequiredCommon;

function requireCommon () {
	if (hasRequiredCommon) return common;
	hasRequiredCommon = 1;
	Object.defineProperty(common, "__esModule", { value: true });
	common.joinPathSegments = common.replacePathSegmentSeparator = common.isAppliedFilter = common.isFatalError = void 0;
	function isFatalError(settings, error) {
	    if (settings.errorFilter === null) {
	        return true;
	    }
	    return !settings.errorFilter(error);
	}
	common.isFatalError = isFatalError;
	function isAppliedFilter(filter, value) {
	    return filter === null || filter(value);
	}
	common.isAppliedFilter = isAppliedFilter;
	function replacePathSegmentSeparator(filepath, separator) {
	    return filepath.split(/[/\\]/).join(separator);
	}
	common.replacePathSegmentSeparator = replacePathSegmentSeparator;
	function joinPathSegments(a, b, separator) {
	    if (a === '') {
	        return b;
	    }
	    /**
	     * The correct handling of cases when the first segment is a root (`/`, `C:/`) or UNC path (`//?/C:/`).
	     */
	    if (a.endsWith(separator)) {
	        return a + b;
	    }
	    return a + separator + b;
	}
	common.joinPathSegments = joinPathSegments;
	return common;
}

var reader$1 = {};

var hasRequiredReader$1;

function requireReader$1 () {
	if (hasRequiredReader$1) return reader$1;
	hasRequiredReader$1 = 1;
	Object.defineProperty(reader$1, "__esModule", { value: true });
	const common = requireCommon();
	class Reader {
	    constructor(_root, _settings) {
	        this._root = _root;
	        this._settings = _settings;
	        this._root = common.replacePathSegmentSeparator(_root, _settings.pathSegmentSeparator);
	    }
	}
	reader$1.default = Reader;
	return reader$1;
}

var hasRequiredAsync$3;

function requireAsync$3 () {
	if (hasRequiredAsync$3) return async$2;
	hasRequiredAsync$3 = 1;
	Object.defineProperty(async$2, "__esModule", { value: true });
	const events_1 = require$$0$5;
	const fsScandir = requireOut$2();
	const fastq = requireQueue();
	const common = requireCommon();
	const reader_1 = requireReader$1();
	class AsyncReader extends reader_1.default {
	    constructor(_root, _settings) {
	        super(_root, _settings);
	        this._settings = _settings;
	        this._scandir = fsScandir.scandir;
	        this._emitter = new events_1.EventEmitter();
	        this._queue = fastq(this._worker.bind(this), this._settings.concurrency);
	        this._isFatalError = false;
	        this._isDestroyed = false;
	        this._queue.drain = () => {
	            if (!this._isFatalError) {
	                this._emitter.emit('end');
	            }
	        };
	    }
	    read() {
	        this._isFatalError = false;
	        this._isDestroyed = false;
	        setImmediate(() => {
	            this._pushToQueue(this._root, this._settings.basePath);
	        });
	        return this._emitter;
	    }
	    get isDestroyed() {
	        return this._isDestroyed;
	    }
	    destroy() {
	        if (this._isDestroyed) {
	            throw new Error('The reader is already destroyed');
	        }
	        this._isDestroyed = true;
	        this._queue.killAndDrain();
	    }
	    onEntry(callback) {
	        this._emitter.on('entry', callback);
	    }
	    onError(callback) {
	        this._emitter.once('error', callback);
	    }
	    onEnd(callback) {
	        this._emitter.once('end', callback);
	    }
	    _pushToQueue(directory, base) {
	        const queueItem = { directory, base };
	        this._queue.push(queueItem, (error) => {
	            if (error !== null) {
	                this._handleError(error);
	            }
	        });
	    }
	    _worker(item, done) {
	        this._scandir(item.directory, this._settings.fsScandirSettings, (error, entries) => {
	            if (error !== null) {
	                done(error, undefined);
	                return;
	            }
	            for (const entry of entries) {
	                this._handleEntry(entry, item.base);
	            }
	            done(null, undefined);
	        });
	    }
	    _handleError(error) {
	        if (this._isDestroyed || !common.isFatalError(this._settings, error)) {
	            return;
	        }
	        this._isFatalError = true;
	        this._isDestroyed = true;
	        this._emitter.emit('error', error);
	    }
	    _handleEntry(entry, base) {
	        if (this._isDestroyed || this._isFatalError) {
	            return;
	        }
	        const fullpath = entry.path;
	        if (base !== undefined) {
	            entry.path = common.joinPathSegments(base, entry.name, this._settings.pathSegmentSeparator);
	        }
	        if (common.isAppliedFilter(this._settings.entryFilter, entry)) {
	            this._emitEntry(entry);
	        }
	        if (entry.dirent.isDirectory() && common.isAppliedFilter(this._settings.deepFilter, entry)) {
	            this._pushToQueue(fullpath, base === undefined ? undefined : entry.path);
	        }
	    }
	    _emitEntry(entry) {
	        this._emitter.emit('entry', entry);
	    }
	}
	async$2.default = AsyncReader;
	return async$2;
}

var hasRequiredAsync$2;

function requireAsync$2 () {
	if (hasRequiredAsync$2) return async$3;
	hasRequiredAsync$2 = 1;
	Object.defineProperty(async$3, "__esModule", { value: true });
	const async_1 = requireAsync$3();
	class AsyncProvider {
	    constructor(_root, _settings) {
	        this._root = _root;
	        this._settings = _settings;
	        this._reader = new async_1.default(this._root, this._settings);
	        this._storage = [];
	    }
	    read(callback) {
	        this._reader.onError((error) => {
	            callFailureCallback(callback, error);
	        });
	        this._reader.onEntry((entry) => {
	            this._storage.push(entry);
	        });
	        this._reader.onEnd(() => {
	            callSuccessCallback(callback, this._storage);
	        });
	        this._reader.read();
	    }
	}
	async$3.default = AsyncProvider;
	function callFailureCallback(callback, error) {
	    callback(error);
	}
	function callSuccessCallback(callback, entries) {
	    callback(null, entries);
	}
	return async$3;
}

var stream$2 = {};

var hasRequiredStream$2;

function requireStream$2 () {
	if (hasRequiredStream$2) return stream$2;
	hasRequiredStream$2 = 1;
	Object.defineProperty(stream$2, "__esModule", { value: true });
	const stream_1 = require$$0$3;
	const async_1 = requireAsync$3();
	class StreamProvider {
	    constructor(_root, _settings) {
	        this._root = _root;
	        this._settings = _settings;
	        this._reader = new async_1.default(this._root, this._settings);
	        this._stream = new stream_1.Readable({
	            objectMode: true,
	            read: () => { },
	            destroy: () => {
	                if (!this._reader.isDestroyed) {
	                    this._reader.destroy();
	                }
	            }
	        });
	    }
	    read() {
	        this._reader.onError((error) => {
	            this._stream.emit('error', error);
	        });
	        this._reader.onEntry((entry) => {
	            this._stream.push(entry);
	        });
	        this._reader.onEnd(() => {
	            this._stream.push(null);
	        });
	        this._reader.read();
	        return this._stream;
	    }
	}
	stream$2.default = StreamProvider;
	return stream$2;
}

var sync$3 = {};

var sync$2 = {};

var hasRequiredSync$3;

function requireSync$3 () {
	if (hasRequiredSync$3) return sync$2;
	hasRequiredSync$3 = 1;
	Object.defineProperty(sync$2, "__esModule", { value: true });
	const fsScandir = requireOut$2();
	const common = requireCommon();
	const reader_1 = requireReader$1();
	class SyncReader extends reader_1.default {
	    constructor() {
	        super(...arguments);
	        this._scandir = fsScandir.scandirSync;
	        this._storage = [];
	        this._queue = new Set();
	    }
	    read() {
	        this._pushToQueue(this._root, this._settings.basePath);
	        this._handleQueue();
	        return this._storage;
	    }
	    _pushToQueue(directory, base) {
	        this._queue.add({ directory, base });
	    }
	    _handleQueue() {
	        for (const item of this._queue.values()) {
	            this._handleDirectory(item.directory, item.base);
	        }
	    }
	    _handleDirectory(directory, base) {
	        try {
	            const entries = this._scandir(directory, this._settings.fsScandirSettings);
	            for (const entry of entries) {
	                this._handleEntry(entry, base);
	            }
	        }
	        catch (error) {
	            this._handleError(error);
	        }
	    }
	    _handleError(error) {
	        if (!common.isFatalError(this._settings, error)) {
	            return;
	        }
	        throw error;
	    }
	    _handleEntry(entry, base) {
	        const fullpath = entry.path;
	        if (base !== undefined) {
	            entry.path = common.joinPathSegments(base, entry.name, this._settings.pathSegmentSeparator);
	        }
	        if (common.isAppliedFilter(this._settings.entryFilter, entry)) {
	            this._pushToStorage(entry);
	        }
	        if (entry.dirent.isDirectory() && common.isAppliedFilter(this._settings.deepFilter, entry)) {
	            this._pushToQueue(fullpath, base === undefined ? undefined : entry.path);
	        }
	    }
	    _pushToStorage(entry) {
	        this._storage.push(entry);
	    }
	}
	sync$2.default = SyncReader;
	return sync$2;
}

var hasRequiredSync$2;

function requireSync$2 () {
	if (hasRequiredSync$2) return sync$3;
	hasRequiredSync$2 = 1;
	Object.defineProperty(sync$3, "__esModule", { value: true });
	const sync_1 = requireSync$3();
	class SyncProvider {
	    constructor(_root, _settings) {
	        this._root = _root;
	        this._settings = _settings;
	        this._reader = new sync_1.default(this._root, this._settings);
	    }
	    read() {
	        return this._reader.read();
	    }
	}
	sync$3.default = SyncProvider;
	return sync$3;
}

var settings$1 = {};

var hasRequiredSettings$1;

function requireSettings$1 () {
	if (hasRequiredSettings$1) return settings$1;
	hasRequiredSettings$1 = 1;
	Object.defineProperty(settings$1, "__esModule", { value: true });
	const path = require$$0$1;
	const fsScandir = requireOut$2();
	class Settings {
	    constructor(_options = {}) {
	        this._options = _options;
	        this.basePath = this._getValue(this._options.basePath, undefined);
	        this.concurrency = this._getValue(this._options.concurrency, Number.POSITIVE_INFINITY);
	        this.deepFilter = this._getValue(this._options.deepFilter, null);
	        this.entryFilter = this._getValue(this._options.entryFilter, null);
	        this.errorFilter = this._getValue(this._options.errorFilter, null);
	        this.pathSegmentSeparator = this._getValue(this._options.pathSegmentSeparator, path.sep);
	        this.fsScandirSettings = new fsScandir.Settings({
	            followSymbolicLinks: this._options.followSymbolicLinks,
	            fs: this._options.fs,
	            pathSegmentSeparator: this._options.pathSegmentSeparator,
	            stats: this._options.stats,
	            throwErrorOnBrokenSymbolicLink: this._options.throwErrorOnBrokenSymbolicLink
	        });
	    }
	    _getValue(option, value) {
	        return option !== null && option !== void 0 ? option : value;
	    }
	}
	settings$1.default = Settings;
	return settings$1;
}

var hasRequiredOut$1;

function requireOut$1 () {
	if (hasRequiredOut$1) return out$3;
	hasRequiredOut$1 = 1;
	Object.defineProperty(out$3, "__esModule", { value: true });
	out$3.Settings = out$3.walkStream = out$3.walkSync = out$3.walk = void 0;
	const async_1 = requireAsync$2();
	const stream_1 = requireStream$2();
	const sync_1 = requireSync$2();
	const settings_1 = requireSettings$1();
	out$3.Settings = settings_1.default;
	function walk(directory, optionsOrSettingsOrCallback, callback) {
	    if (typeof optionsOrSettingsOrCallback === 'function') {
	        new async_1.default(directory, getSettings()).read(optionsOrSettingsOrCallback);
	        return;
	    }
	    new async_1.default(directory, getSettings(optionsOrSettingsOrCallback)).read(callback);
	}
	out$3.walk = walk;
	function walkSync(directory, optionsOrSettings) {
	    const settings = getSettings(optionsOrSettings);
	    const provider = new sync_1.default(directory, settings);
	    return provider.read();
	}
	out$3.walkSync = walkSync;
	function walkStream(directory, optionsOrSettings) {
	    const settings = getSettings(optionsOrSettings);
	    const provider = new stream_1.default(directory, settings);
	    return provider.read();
	}
	out$3.walkStream = walkStream;
	function getSettings(settingsOrOptions = {}) {
	    if (settingsOrOptions instanceof settings_1.default) {
	        return settingsOrOptions;
	    }
	    return new settings_1.default(settingsOrOptions);
	}
	return out$3;
}

var reader = {};

var hasRequiredReader;

function requireReader () {
	if (hasRequiredReader) return reader;
	hasRequiredReader = 1;
	Object.defineProperty(reader, "__esModule", { value: true });
	const path = require$$0$1;
	const fsStat = requireOut$3();
	const utils = requireUtils$1();
	class Reader {
	    constructor(_settings) {
	        this._settings = _settings;
	        this._fsStatSettings = new fsStat.Settings({
	            followSymbolicLink: this._settings.followSymbolicLinks,
	            fs: this._settings.fs,
	            throwErrorOnBrokenSymbolicLink: this._settings.followSymbolicLinks
	        });
	    }
	    _getFullEntryPath(filepath) {
	        return path.resolve(this._settings.cwd, filepath);
	    }
	    _makeEntry(stats, pattern) {
	        const entry = {
	            name: pattern,
	            path: pattern,
	            dirent: utils.fs.createDirentFromStats(pattern, stats)
	        };
	        if (this._settings.stats) {
	            entry.stats = stats;
	        }
	        return entry;
	    }
	    _isFatalError(error) {
	        return !utils.errno.isEnoentCodeError(error) && !this._settings.suppressErrors;
	    }
	}
	reader.default = Reader;
	return reader;
}

var stream$1 = {};

var hasRequiredStream$1;

function requireStream$1 () {
	if (hasRequiredStream$1) return stream$1;
	hasRequiredStream$1 = 1;
	Object.defineProperty(stream$1, "__esModule", { value: true });
	const stream_1 = require$$0$3;
	const fsStat = requireOut$3();
	const fsWalk = requireOut$1();
	const reader_1 = requireReader();
	class ReaderStream extends reader_1.default {
	    constructor() {
	        super(...arguments);
	        this._walkStream = fsWalk.walkStream;
	        this._stat = fsStat.stat;
	    }
	    dynamic(root, options) {
	        return this._walkStream(root, options);
	    }
	    static(patterns, options) {
	        const filepaths = patterns.map(this._getFullEntryPath, this);
	        const stream = new stream_1.PassThrough({ objectMode: true });
	        stream._write = (index, _enc, done) => {
	            return this._getEntry(filepaths[index], patterns[index], options)
	                .then((entry) => {
	                if (entry !== null && options.entryFilter(entry)) {
	                    stream.push(entry);
	                }
	                if (index === filepaths.length - 1) {
	                    stream.end();
	                }
	                done();
	            })
	                .catch(done);
	        };
	        for (let i = 0; i < filepaths.length; i++) {
	            stream.write(i);
	        }
	        return stream;
	    }
	    _getEntry(filepath, pattern, options) {
	        return this._getStat(filepath)
	            .then((stats) => this._makeEntry(stats, pattern))
	            .catch((error) => {
	            if (options.errorFilter(error)) {
	                return null;
	            }
	            throw error;
	        });
	    }
	    _getStat(filepath) {
	        return new Promise((resolve, reject) => {
	            this._stat(filepath, this._fsStatSettings, (error, stats) => {
	                return error === null ? resolve(stats) : reject(error);
	            });
	        });
	    }
	}
	stream$1.default = ReaderStream;
	return stream$1;
}

var hasRequiredAsync$1;

function requireAsync$1 () {
	if (hasRequiredAsync$1) return async$4;
	hasRequiredAsync$1 = 1;
	Object.defineProperty(async$4, "__esModule", { value: true });
	const fsWalk = requireOut$1();
	const reader_1 = requireReader();
	const stream_1 = requireStream$1();
	class ReaderAsync extends reader_1.default {
	    constructor() {
	        super(...arguments);
	        this._walkAsync = fsWalk.walk;
	        this._readerStream = new stream_1.default(this._settings);
	    }
	    dynamic(root, options) {
	        return new Promise((resolve, reject) => {
	            this._walkAsync(root, options, (error, entries) => {
	                if (error === null) {
	                    resolve(entries);
	                }
	                else {
	                    reject(error);
	                }
	            });
	        });
	    }
	    async static(patterns, options) {
	        const entries = [];
	        const stream = this._readerStream.static(patterns, options);
	        // After #235, replace it with an asynchronous iterator.
	        return new Promise((resolve, reject) => {
	            stream.once('error', reject);
	            stream.on('data', (entry) => entries.push(entry));
	            stream.once('end', () => resolve(entries));
	        });
	    }
	}
	async$4.default = ReaderAsync;
	return async$4;
}

var provider = {};

var deep = {};

var partial = {};

var matcher = {};

var hasRequiredMatcher;

function requireMatcher () {
	if (hasRequiredMatcher) return matcher;
	hasRequiredMatcher = 1;
	Object.defineProperty(matcher, "__esModule", { value: true });
	const utils = requireUtils$1();
	class Matcher {
	    constructor(_patterns, _settings, _micromatchOptions) {
	        this._patterns = _patterns;
	        this._settings = _settings;
	        this._micromatchOptions = _micromatchOptions;
	        this._storage = [];
	        this._fillStorage();
	    }
	    _fillStorage() {
	        for (const pattern of this._patterns) {
	            const segments = this._getPatternSegments(pattern);
	            const sections = this._splitSegmentsIntoSections(segments);
	            this._storage.push({
	                complete: sections.length <= 1,
	                pattern,
	                segments,
	                sections
	            });
	        }
	    }
	    _getPatternSegments(pattern) {
	        const parts = utils.pattern.getPatternParts(pattern, this._micromatchOptions);
	        return parts.map((part) => {
	            const dynamic = utils.pattern.isDynamicPattern(part, this._settings);
	            if (!dynamic) {
	                return {
	                    dynamic: false,
	                    pattern: part
	                };
	            }
	            return {
	                dynamic: true,
	                pattern: part,
	                patternRe: utils.pattern.makeRe(part, this._micromatchOptions)
	            };
	        });
	    }
	    _splitSegmentsIntoSections(segments) {
	        return utils.array.splitWhen(segments, (segment) => segment.dynamic && utils.pattern.hasGlobStar(segment.pattern));
	    }
	}
	matcher.default = Matcher;
	return matcher;
}

var hasRequiredPartial;

function requirePartial () {
	if (hasRequiredPartial) return partial;
	hasRequiredPartial = 1;
	Object.defineProperty(partial, "__esModule", { value: true });
	const matcher_1 = requireMatcher();
	class PartialMatcher extends matcher_1.default {
	    match(filepath) {
	        const parts = filepath.split('/');
	        const levels = parts.length;
	        const patterns = this._storage.filter((info) => !info.complete || info.segments.length > levels);
	        for (const pattern of patterns) {
	            const section = pattern.sections[0];
	            /**
	             * In this case, the pattern has a globstar and we must read all directories unconditionally,
	             * but only if the level has reached the end of the first group.
	             *
	             * fixtures/{a,b}/**
	             *  ^ true/false  ^ always true
	            */
	            if (!pattern.complete && levels > section.length) {
	                return true;
	            }
	            const match = parts.every((part, index) => {
	                const segment = pattern.segments[index];
	                if (segment.dynamic && segment.patternRe.test(part)) {
	                    return true;
	                }
	                if (!segment.dynamic && segment.pattern === part) {
	                    return true;
	                }
	                return false;
	            });
	            if (match) {
	                return true;
	            }
	        }
	        return false;
	    }
	}
	partial.default = PartialMatcher;
	return partial;
}

var hasRequiredDeep;

function requireDeep () {
	if (hasRequiredDeep) return deep;
	hasRequiredDeep = 1;
	Object.defineProperty(deep, "__esModule", { value: true });
	const utils = requireUtils$1();
	const partial_1 = requirePartial();
	class DeepFilter {
	    constructor(_settings, _micromatchOptions) {
	        this._settings = _settings;
	        this._micromatchOptions = _micromatchOptions;
	    }
	    getFilter(basePath, positive, negative) {
	        const matcher = this._getMatcher(positive);
	        const negativeRe = this._getNegativePatternsRe(negative);
	        return (entry) => this._filter(basePath, entry, matcher, negativeRe);
	    }
	    _getMatcher(patterns) {
	        return new partial_1.default(patterns, this._settings, this._micromatchOptions);
	    }
	    _getNegativePatternsRe(patterns) {
	        const affectDepthOfReadingPatterns = patterns.filter(utils.pattern.isAffectDepthOfReadingPattern);
	        return utils.pattern.convertPatternsToRe(affectDepthOfReadingPatterns, this._micromatchOptions);
	    }
	    _filter(basePath, entry, matcher, negativeRe) {
	        if (this._isSkippedByDeep(basePath, entry.path)) {
	            return false;
	        }
	        if (this._isSkippedSymbolicLink(entry)) {
	            return false;
	        }
	        const filepath = utils.path.removeLeadingDotSegment(entry.path);
	        if (this._isSkippedByPositivePatterns(filepath, matcher)) {
	            return false;
	        }
	        return this._isSkippedByNegativePatterns(filepath, negativeRe);
	    }
	    _isSkippedByDeep(basePath, entryPath) {
	        /**
	         * Avoid unnecessary depth calculations when it doesn't matter.
	         */
	        if (this._settings.deep === Infinity) {
	            return false;
	        }
	        return this._getEntryLevel(basePath, entryPath) >= this._settings.deep;
	    }
	    _getEntryLevel(basePath, entryPath) {
	        const entryPathDepth = entryPath.split('/').length;
	        if (basePath === '') {
	            return entryPathDepth;
	        }
	        const basePathDepth = basePath.split('/').length;
	        return entryPathDepth - basePathDepth;
	    }
	    _isSkippedSymbolicLink(entry) {
	        return !this._settings.followSymbolicLinks && entry.dirent.isSymbolicLink();
	    }
	    _isSkippedByPositivePatterns(entryPath, matcher) {
	        return !this._settings.baseNameMatch && !matcher.match(entryPath);
	    }
	    _isSkippedByNegativePatterns(entryPath, patternsRe) {
	        return !utils.pattern.matchAny(entryPath, patternsRe);
	    }
	}
	deep.default = DeepFilter;
	return deep;
}

var entry$1 = {};

var hasRequiredEntry$1;

function requireEntry$1 () {
	if (hasRequiredEntry$1) return entry$1;
	hasRequiredEntry$1 = 1;
	Object.defineProperty(entry$1, "__esModule", { value: true });
	const utils = requireUtils$1();
	class EntryFilter {
	    constructor(_settings, _micromatchOptions) {
	        this._settings = _settings;
	        this._micromatchOptions = _micromatchOptions;
	        this.index = new Map();
	    }
	    getFilter(positive, negative) {
	        const [absoluteNegative, relativeNegative] = utils.pattern.partitionAbsoluteAndRelative(negative);
	        const patterns = {
	            positive: {
	                all: utils.pattern.convertPatternsToRe(positive, this._micromatchOptions)
	            },
	            negative: {
	                absolute: utils.pattern.convertPatternsToRe(absoluteNegative, Object.assign(Object.assign({}, this._micromatchOptions), { dot: true })),
	                relative: utils.pattern.convertPatternsToRe(relativeNegative, Object.assign(Object.assign({}, this._micromatchOptions), { dot: true }))
	            }
	        };
	        return (entry) => this._filter(entry, patterns);
	    }
	    _filter(entry, patterns) {
	        const filepath = utils.path.removeLeadingDotSegment(entry.path);
	        if (this._settings.unique && this._isDuplicateEntry(filepath)) {
	            return false;
	        }
	        if (this._onlyFileFilter(entry) || this._onlyDirectoryFilter(entry)) {
	            return false;
	        }
	        const isMatched = this._isMatchToPatternsSet(filepath, patterns, entry.dirent.isDirectory());
	        if (this._settings.unique && isMatched) {
	            this._createIndexRecord(filepath);
	        }
	        return isMatched;
	    }
	    _isDuplicateEntry(filepath) {
	        return this.index.has(filepath);
	    }
	    _createIndexRecord(filepath) {
	        this.index.set(filepath, undefined);
	    }
	    _onlyFileFilter(entry) {
	        return this._settings.onlyFiles && !entry.dirent.isFile();
	    }
	    _onlyDirectoryFilter(entry) {
	        return this._settings.onlyDirectories && !entry.dirent.isDirectory();
	    }
	    _isMatchToPatternsSet(filepath, patterns, isDirectory) {
	        const isMatched = this._isMatchToPatterns(filepath, patterns.positive.all, isDirectory);
	        if (!isMatched) {
	            return false;
	        }
	        const isMatchedByRelativeNegative = this._isMatchToPatterns(filepath, patterns.negative.relative, isDirectory);
	        if (isMatchedByRelativeNegative) {
	            return false;
	        }
	        const isMatchedByAbsoluteNegative = this._isMatchToAbsoluteNegative(filepath, patterns.negative.absolute, isDirectory);
	        if (isMatchedByAbsoluteNegative) {
	            return false;
	        }
	        return true;
	    }
	    _isMatchToAbsoluteNegative(filepath, patternsRe, isDirectory) {
	        if (patternsRe.length === 0) {
	            return false;
	        }
	        const fullpath = utils.path.makeAbsolute(this._settings.cwd, filepath);
	        return this._isMatchToPatterns(fullpath, patternsRe, isDirectory);
	    }
	    _isMatchToPatterns(filepath, patternsRe, isDirectory) {
	        if (patternsRe.length === 0) {
	            return false;
	        }
	        // Trying to match files and directories by patterns.
	        const isMatched = utils.pattern.matchAny(filepath, patternsRe);
	        // A pattern with a trailling slash can be used for directory matching.
	        // To apply such pattern, we need to add a tralling slash to the path.
	        if (!isMatched && isDirectory) {
	            return utils.pattern.matchAny(filepath + '/', patternsRe);
	        }
	        return isMatched;
	    }
	}
	entry$1.default = EntryFilter;
	return entry$1;
}

var error = {};

var hasRequiredError;

function requireError () {
	if (hasRequiredError) return error;
	hasRequiredError = 1;
	Object.defineProperty(error, "__esModule", { value: true });
	const utils = requireUtils$1();
	class ErrorFilter {
	    constructor(_settings) {
	        this._settings = _settings;
	    }
	    getFilter() {
	        return (error) => this._isNonFatalError(error);
	    }
	    _isNonFatalError(error) {
	        return utils.errno.isEnoentCodeError(error) || this._settings.suppressErrors;
	    }
	}
	error.default = ErrorFilter;
	return error;
}

var entry = {};

var hasRequiredEntry;

function requireEntry () {
	if (hasRequiredEntry) return entry;
	hasRequiredEntry = 1;
	Object.defineProperty(entry, "__esModule", { value: true });
	const utils = requireUtils$1();
	class EntryTransformer {
	    constructor(_settings) {
	        this._settings = _settings;
	    }
	    getTransformer() {
	        return (entry) => this._transform(entry);
	    }
	    _transform(entry) {
	        let filepath = entry.path;
	        if (this._settings.absolute) {
	            filepath = utils.path.makeAbsolute(this._settings.cwd, filepath);
	            filepath = utils.path.unixify(filepath);
	        }
	        if (this._settings.markDirectories && entry.dirent.isDirectory()) {
	            filepath += '/';
	        }
	        if (!this._settings.objectMode) {
	            return filepath;
	        }
	        return Object.assign(Object.assign({}, entry), { path: filepath });
	    }
	}
	entry.default = EntryTransformer;
	return entry;
}

var hasRequiredProvider;

function requireProvider () {
	if (hasRequiredProvider) return provider;
	hasRequiredProvider = 1;
	Object.defineProperty(provider, "__esModule", { value: true });
	const path = require$$0$1;
	const deep_1 = requireDeep();
	const entry_1 = requireEntry$1();
	const error_1 = requireError();
	const entry_2 = requireEntry();
	class Provider {
	    constructor(_settings) {
	        this._settings = _settings;
	        this.errorFilter = new error_1.default(this._settings);
	        this.entryFilter = new entry_1.default(this._settings, this._getMicromatchOptions());
	        this.deepFilter = new deep_1.default(this._settings, this._getMicromatchOptions());
	        this.entryTransformer = new entry_2.default(this._settings);
	    }
	    _getRootDirectory(task) {
	        return path.resolve(this._settings.cwd, task.base);
	    }
	    _getReaderOptions(task) {
	        const basePath = task.base === '.' ? '' : task.base;
	        return {
	            basePath,
	            pathSegmentSeparator: '/',
	            concurrency: this._settings.concurrency,
	            deepFilter: this.deepFilter.getFilter(basePath, task.positive, task.negative),
	            entryFilter: this.entryFilter.getFilter(task.positive, task.negative),
	            errorFilter: this.errorFilter.getFilter(),
	            followSymbolicLinks: this._settings.followSymbolicLinks,
	            fs: this._settings.fs,
	            stats: this._settings.stats,
	            throwErrorOnBrokenSymbolicLink: this._settings.throwErrorOnBrokenSymbolicLink,
	            transform: this.entryTransformer.getTransformer()
	        };
	    }
	    _getMicromatchOptions() {
	        return {
	            dot: this._settings.dot,
	            matchBase: this._settings.baseNameMatch,
	            nobrace: !this._settings.braceExpansion,
	            nocase: !this._settings.caseSensitiveMatch,
	            noext: !this._settings.extglob,
	            noglobstar: !this._settings.globstar,
	            posix: true,
	            strictSlashes: false
	        };
	    }
	}
	provider.default = Provider;
	return provider;
}

var hasRequiredAsync;

function requireAsync () {
	if (hasRequiredAsync) return async$5;
	hasRequiredAsync = 1;
	Object.defineProperty(async$5, "__esModule", { value: true });
	const async_1 = requireAsync$1();
	const provider_1 = requireProvider();
	class ProviderAsync extends provider_1.default {
	    constructor() {
	        super(...arguments);
	        this._reader = new async_1.default(this._settings);
	    }
	    async read(task) {
	        const root = this._getRootDirectory(task);
	        const options = this._getReaderOptions(task);
	        const entries = await this.api(root, task, options);
	        return entries.map((entry) => options.transform(entry));
	    }
	    api(root, task, options) {
	        if (task.dynamic) {
	            return this._reader.dynamic(root, options);
	        }
	        return this._reader.static(task.patterns, options);
	    }
	}
	async$5.default = ProviderAsync;
	return async$5;
}

var stream = {};

var hasRequiredStream;

function requireStream () {
	if (hasRequiredStream) return stream;
	hasRequiredStream = 1;
	Object.defineProperty(stream, "__esModule", { value: true });
	const stream_1 = require$$0$3;
	const stream_2 = requireStream$1();
	const provider_1 = requireProvider();
	class ProviderStream extends provider_1.default {
	    constructor() {
	        super(...arguments);
	        this._reader = new stream_2.default(this._settings);
	    }
	    read(task) {
	        const root = this._getRootDirectory(task);
	        const options = this._getReaderOptions(task);
	        const source = this.api(root, task, options);
	        const destination = new stream_1.Readable({ objectMode: true, read: () => { } });
	        source
	            .once('error', (error) => destination.emit('error', error))
	            .on('data', (entry) => destination.emit('data', options.transform(entry)))
	            .once('end', () => destination.emit('end'));
	        destination
	            .once('close', () => source.destroy());
	        return destination;
	    }
	    api(root, task, options) {
	        if (task.dynamic) {
	            return this._reader.dynamic(root, options);
	        }
	        return this._reader.static(task.patterns, options);
	    }
	}
	stream.default = ProviderStream;
	return stream;
}

var sync$1 = {};

var sync = {};

var hasRequiredSync$1;

function requireSync$1 () {
	if (hasRequiredSync$1) return sync;
	hasRequiredSync$1 = 1;
	Object.defineProperty(sync, "__esModule", { value: true });
	const fsStat = requireOut$3();
	const fsWalk = requireOut$1();
	const reader_1 = requireReader();
	class ReaderSync extends reader_1.default {
	    constructor() {
	        super(...arguments);
	        this._walkSync = fsWalk.walkSync;
	        this._statSync = fsStat.statSync;
	    }
	    dynamic(root, options) {
	        return this._walkSync(root, options);
	    }
	    static(patterns, options) {
	        const entries = [];
	        for (const pattern of patterns) {
	            const filepath = this._getFullEntryPath(pattern);
	            const entry = this._getEntry(filepath, pattern, options);
	            if (entry === null || !options.entryFilter(entry)) {
	                continue;
	            }
	            entries.push(entry);
	        }
	        return entries;
	    }
	    _getEntry(filepath, pattern, options) {
	        try {
	            const stats = this._getStat(filepath);
	            return this._makeEntry(stats, pattern);
	        }
	        catch (error) {
	            if (options.errorFilter(error)) {
	                return null;
	            }
	            throw error;
	        }
	    }
	    _getStat(filepath) {
	        return this._statSync(filepath, this._fsStatSettings);
	    }
	}
	sync.default = ReaderSync;
	return sync;
}

var hasRequiredSync;

function requireSync () {
	if (hasRequiredSync) return sync$1;
	hasRequiredSync = 1;
	Object.defineProperty(sync$1, "__esModule", { value: true });
	const sync_1 = requireSync$1();
	const provider_1 = requireProvider();
	class ProviderSync extends provider_1.default {
	    constructor() {
	        super(...arguments);
	        this._reader = new sync_1.default(this._settings);
	    }
	    read(task) {
	        const root = this._getRootDirectory(task);
	        const options = this._getReaderOptions(task);
	        const entries = this.api(root, task, options);
	        return entries.map(options.transform);
	    }
	    api(root, task, options) {
	        if (task.dynamic) {
	            return this._reader.dynamic(root, options);
	        }
	        return this._reader.static(task.patterns, options);
	    }
	}
	sync$1.default = ProviderSync;
	return sync$1;
}

var settings = {};

var hasRequiredSettings;

function requireSettings () {
	if (hasRequiredSettings) return settings;
	hasRequiredSettings = 1;
	(function (exports) {
		Object.defineProperty(exports, "__esModule", { value: true });
		exports.DEFAULT_FILE_SYSTEM_ADAPTER = void 0;
		const fs = require$$0$4;
		const os = require$$0;
		/**
		 * The `os.cpus` method can return zero. We expect the number of cores to be greater than zero.
		 * https://github.com/nodejs/node/blob/7faeddf23a98c53896f8b574a6e66589e8fb1eb8/lib/os.js#L106-L107
		 */
		const CPU_COUNT = Math.max(os.cpus().length, 1);
		exports.DEFAULT_FILE_SYSTEM_ADAPTER = {
		    lstat: fs.lstat,
		    lstatSync: fs.lstatSync,
		    stat: fs.stat,
		    statSync: fs.statSync,
		    readdir: fs.readdir,
		    readdirSync: fs.readdirSync
		};
		class Settings {
		    constructor(_options = {}) {
		        this._options = _options;
		        this.absolute = this._getValue(this._options.absolute, false);
		        this.baseNameMatch = this._getValue(this._options.baseNameMatch, false);
		        this.braceExpansion = this._getValue(this._options.braceExpansion, true);
		        this.caseSensitiveMatch = this._getValue(this._options.caseSensitiveMatch, true);
		        this.concurrency = this._getValue(this._options.concurrency, CPU_COUNT);
		        this.cwd = this._getValue(this._options.cwd, process.cwd());
		        this.deep = this._getValue(this._options.deep, Infinity);
		        this.dot = this._getValue(this._options.dot, false);
		        this.extglob = this._getValue(this._options.extglob, true);
		        this.followSymbolicLinks = this._getValue(this._options.followSymbolicLinks, true);
		        this.fs = this._getFileSystemMethods(this._options.fs);
		        this.globstar = this._getValue(this._options.globstar, true);
		        this.ignore = this._getValue(this._options.ignore, []);
		        this.markDirectories = this._getValue(this._options.markDirectories, false);
		        this.objectMode = this._getValue(this._options.objectMode, false);
		        this.onlyDirectories = this._getValue(this._options.onlyDirectories, false);
		        this.onlyFiles = this._getValue(this._options.onlyFiles, true);
		        this.stats = this._getValue(this._options.stats, false);
		        this.suppressErrors = this._getValue(this._options.suppressErrors, false);
		        this.throwErrorOnBrokenSymbolicLink = this._getValue(this._options.throwErrorOnBrokenSymbolicLink, false);
		        this.unique = this._getValue(this._options.unique, true);
		        if (this.onlyDirectories) {
		            this.onlyFiles = false;
		        }
		        if (this.stats) {
		            this.objectMode = true;
		        }
		        // Remove the cast to the array in the next major (#404).
		        this.ignore = [].concat(this.ignore);
		    }
		    _getValue(option, value) {
		        return option === undefined ? value : option;
		    }
		    _getFileSystemMethods(methods = {}) {
		        return Object.assign(Object.assign({}, exports.DEFAULT_FILE_SYSTEM_ADAPTER), methods);
		    }
		}
		exports.default = Settings; 
	} (settings));
	return settings;
}

var out;
var hasRequiredOut;

function requireOut () {
	if (hasRequiredOut) return out;
	hasRequiredOut = 1;
	const taskManager = requireTasks();
	const async_1 = requireAsync();
	const stream_1 = requireStream();
	const sync_1 = requireSync();
	const settings_1 = requireSettings();
	const utils = requireUtils$1();
	async function FastGlob(source, options) {
	    assertPatternsInput(source);
	    const works = getWorks(source, async_1.default, options);
	    const result = await Promise.all(works);
	    return utils.array.flatten(result);
	}
	// https://github.com/typescript-eslint/typescript-eslint/issues/60
	// eslint-disable-next-line no-redeclare
	(function (FastGlob) {
	    FastGlob.glob = FastGlob;
	    FastGlob.globSync = sync;
	    FastGlob.globStream = stream;
	    FastGlob.async = FastGlob;
	    function sync(source, options) {
	        assertPatternsInput(source);
	        const works = getWorks(source, sync_1.default, options);
	        return utils.array.flatten(works);
	    }
	    FastGlob.sync = sync;
	    function stream(source, options) {
	        assertPatternsInput(source);
	        const works = getWorks(source, stream_1.default, options);
	        /**
	         * The stream returned by the provider cannot work with an asynchronous iterator.
	         * To support asynchronous iterators, regardless of the number of tasks, we always multiplex streams.
	         * This affects performance (+25%). I don't see best solution right now.
	         */
	        return utils.stream.merge(works);
	    }
	    FastGlob.stream = stream;
	    function generateTasks(source, options) {
	        assertPatternsInput(source);
	        const patterns = [].concat(source);
	        const settings = new settings_1.default(options);
	        return taskManager.generate(patterns, settings);
	    }
	    FastGlob.generateTasks = generateTasks;
	    function isDynamicPattern(source, options) {
	        assertPatternsInput(source);
	        const settings = new settings_1.default(options);
	        return utils.pattern.isDynamicPattern(source, settings);
	    }
	    FastGlob.isDynamicPattern = isDynamicPattern;
	    function escapePath(source) {
	        assertPatternsInput(source);
	        return utils.path.escape(source);
	    }
	    FastGlob.escapePath = escapePath;
	    function convertPathToPattern(source) {
	        assertPatternsInput(source);
	        return utils.path.convertPathToPattern(source);
	    }
	    FastGlob.convertPathToPattern = convertPathToPattern;
	    (function (posix) {
	        function escapePath(source) {
	            assertPatternsInput(source);
	            return utils.path.escapePosixPath(source);
	        }
	        posix.escapePath = escapePath;
	        function convertPathToPattern(source) {
	            assertPatternsInput(source);
	            return utils.path.convertPosixPathToPattern(source);
	        }
	        posix.convertPathToPattern = convertPathToPattern;
	    })(FastGlob.posix || (FastGlob.posix = {}));
	    (function (win32) {
	        function escapePath(source) {
	            assertPatternsInput(source);
	            return utils.path.escapeWindowsPath(source);
	        }
	        win32.escapePath = escapePath;
	        function convertPathToPattern(source) {
	            assertPatternsInput(source);
	            return utils.path.convertWindowsPathToPattern(source);
	        }
	        win32.convertPathToPattern = convertPathToPattern;
	    })(FastGlob.win32 || (FastGlob.win32 = {}));
	})(FastGlob);
	function getWorks(source, _Provider, options) {
	    const patterns = [].concat(source);
	    const settings = new settings_1.default(options);
	    const tasks = taskManager.generate(patterns, settings);
	    const provider = new _Provider(settings);
	    return tasks.map(provider.read, provider);
	}
	function assertPatternsInput(input) {
	    const source = [].concat(input);
	    const isValidSource = source.every((item) => utils.string.isString(item) && !utils.string.isEmpty(item));
	    if (!isValidSource) {
	        throw new TypeError('Patterns must be a string (non empty) or an array of strings');
	    }
	}
	out = FastGlob;
	return out;
}

var outExports = requireOut();
const fg = /*@__PURE__*/getDefaultExportFromCjs(outExports);

const decoder = new TextDecoder();
const toUTF8String = (input, start = 0, end = input.length) => decoder.decode(input.slice(start, end));
const toHexString = (input, start = 0, end = input.length) => input.slice(start, end).reduce((memo, i) => memo + ("0" + i.toString(16)).slice(-2), "");
const readInt16LE = (input, offset = 0) => {
  const val = input[offset] + input[offset + 1] * 2 ** 8;
  return val | (val & 2 ** 15) * 131070;
};
const readUInt16BE = (input, offset = 0) => input[offset] * 2 ** 8 + input[offset + 1];
const readUInt16LE = (input, offset = 0) => input[offset] + input[offset + 1] * 2 ** 8;
const readUInt24LE = (input, offset = 0) => input[offset] + input[offset + 1] * 2 ** 8 + input[offset + 2] * 2 ** 16;
const readInt32LE = (input, offset = 0) => input[offset] + input[offset + 1] * 2 ** 8 + input[offset + 2] * 2 ** 16 + (input[offset + 3] << 24);
const readUInt32BE = (input, offset = 0) => input[offset] * 2 ** 24 + input[offset + 1] * 2 ** 16 + input[offset + 2] * 2 ** 8 + input[offset + 3];
const readUInt32LE = (input, offset = 0) => input[offset] + input[offset + 1] * 2 ** 8 + input[offset + 2] * 2 ** 16 + input[offset + 3] * 2 ** 24;
const methods = {
  readUInt16BE,
  readUInt16LE,
  readUInt32BE,
  readUInt32LE
};
function readUInt(input, bits, offset, isBigEndian) {
  offset = offset || 0;
  const endian = isBigEndian ? "BE" : "LE";
  const methodName = "readUInt" + bits + endian;
  return methods[methodName](input, offset);
}

const BMP = {
  validate: (input) => toUTF8String(input, 0, 2) === "BM",
  calculate: (input) => ({
    height: Math.abs(readInt32LE(input, 22)),
    width: readUInt32LE(input, 18)
  })
};

const TYPE_ICON = 1;
const SIZE_HEADER$1 = 2 + 2 + 2;
const SIZE_IMAGE_ENTRY = 1 + 1 + 1 + 1 + 2 + 2 + 4 + 4;
function getSizeFromOffset(input, offset) {
  const value = input[offset];
  return value === 0 ? 256 : value;
}
function getImageSize$1(input, imageIndex) {
  const offset = SIZE_HEADER$1 + imageIndex * SIZE_IMAGE_ENTRY;
  return {
    height: getSizeFromOffset(input, offset + 1),
    width: getSizeFromOffset(input, offset)
  };
}
const ICO = {
  validate(input) {
    const reserved = readUInt16LE(input, 0);
    const imageCount = readUInt16LE(input, 4);
    if (reserved !== 0 || imageCount === 0) {
      return false;
    }
    const imageType = readUInt16LE(input, 2);
    return imageType === TYPE_ICON;
  },
  calculate(input) {
    const nbImages = readUInt16LE(input, 4);
    const imageSize = getImageSize$1(input, 0);
    if (nbImages === 1) {
      return imageSize;
    }
    const imgs = [imageSize];
    for (let imageIndex = 1; imageIndex < nbImages; imageIndex += 1) {
      imgs.push(getImageSize$1(input, imageIndex));
    }
    return {
      height: imageSize.height,
      images: imgs,
      width: imageSize.width
    };
  }
};

const TYPE_CURSOR = 2;
const CUR = {
  validate(input) {
    const reserved = readUInt16LE(input, 0);
    const imageCount = readUInt16LE(input, 4);
    if (reserved !== 0 || imageCount === 0) {
      return false;
    }
    const imageType = readUInt16LE(input, 2);
    return imageType === TYPE_CURSOR;
  },
  calculate: (input) => ICO.calculate(input)
};

const DDS = {
  validate: (input) => readUInt32LE(input, 0) === 542327876,
  calculate: (input) => ({
    height: readUInt32LE(input, 12),
    width: readUInt32LE(input, 16)
  })
};

const gifRegexp = /^GIF8[79]a/;
const GIF = {
  validate: (input) => gifRegexp.test(toUTF8String(input, 0, 6)),
  calculate: (input) => ({
    height: readUInt16LE(input, 8),
    width: readUInt16LE(input, 6)
  })
};

const SIZE_HEADER = 4 + 4;
const FILE_LENGTH_OFFSET = 4;
const ENTRY_LENGTH_OFFSET = 4;
const ICON_TYPE_SIZE = {
  ICON: 32,
  "ICN#": 32,
  // m => 16 x 16
  "icm#": 16,
  icm4: 16,
  icm8: 16,
  // s => 16 x 16
  "ics#": 16,
  ics4: 16,
  ics8: 16,
  is32: 16,
  s8mk: 16,
  icp4: 16,
  // l => 32 x 32
  icl4: 32,
  icl8: 32,
  il32: 32,
  l8mk: 32,
  icp5: 32,
  ic11: 32,
  // h => 48 x 48
  ich4: 48,
  ich8: 48,
  ih32: 48,
  h8mk: 48,
  // . => 64 x 64
  icp6: 64,
  ic12: 32,
  // t => 128 x 128
  it32: 128,
  t8mk: 128,
  ic07: 128,
  // . => 256 x 256
  ic08: 256,
  ic13: 256,
  // . => 512 x 512
  ic09: 512,
  ic14: 512,
  // . => 1024 x 1024
  ic10: 1024
};
function readImageHeader(input, imageOffset) {
  const imageLengthOffset = imageOffset + ENTRY_LENGTH_OFFSET;
  return [
    toUTF8String(input, imageOffset, imageLengthOffset),
    readUInt32BE(input, imageLengthOffset)
  ];
}
function getImageSize(type) {
  const size = ICON_TYPE_SIZE[type];
  return { width: size, height: size, type };
}
const ICNS = {
  validate: (input) => toUTF8String(input, 0, 4) === "icns",
  calculate(input) {
    const inputLength = input.length;
    const fileLength = readUInt32BE(input, FILE_LENGTH_OFFSET);
    let imageOffset = SIZE_HEADER;
    let imageHeader = readImageHeader(input, imageOffset);
    let imageSize = getImageSize(imageHeader[0]);
    imageOffset += imageHeader[1];
    if (imageOffset === fileLength) {
      return imageSize;
    }
    const result = {
      height: imageSize.height,
      images: [imageSize],
      width: imageSize.width
    };
    while (imageOffset < fileLength && imageOffset < inputLength) {
      imageHeader = readImageHeader(input, imageOffset);
      imageSize = getImageSize(imageHeader[0]);
      imageOffset += imageHeader[1];
      result.images.push(imageSize);
    }
    return result;
  }
};

const J2C = {
  // TODO: this doesn't seem right. SIZ marker doesn't have to be right after the SOC
  validate: (input) => toHexString(input, 0, 4) === "ff4fff51",
  calculate: (input) => ({
    height: readUInt32BE(input, 12),
    width: readUInt32BE(input, 8)
  })
};

const BoxTypes = {
  ftyp: "66747970",
  ihdr: "69686472",
  jp2h: "6a703268",
  jp__: "6a502020",
  rreq: "72726571",
  xml_: "786d6c20"
};
const calculateRREQLength = (box) => {
  const unit = box[0];
  let offset = 1 + 2 * unit;
  const numStdFlags = readUInt16BE(box, offset);
  const flagsLength = numStdFlags * (2 + unit);
  offset = offset + 2 + flagsLength;
  const numVendorFeatures = readUInt16BE(box, offset);
  const featuresLength = numVendorFeatures * (16 + unit);
  return offset + 2 + featuresLength;
};
const parseIHDR = (box) => {
  return {
    height: readUInt32BE(box, 4),
    width: readUInt32BE(box, 8)
  };
};
const JP2 = {
  validate(input) {
    const signature = toHexString(input, 4, 8);
    const signatureLength = readUInt32BE(input, 0);
    if (signature !== BoxTypes.jp__ || signatureLength < 1) {
      return false;
    }
    const ftypeBoxStart = signatureLength + 4;
    const ftypBoxLength = readUInt32BE(input, signatureLength);
    const ftypBox = input.slice(ftypeBoxStart, ftypeBoxStart + ftypBoxLength);
    return toHexString(ftypBox, 0, 4) === BoxTypes.ftyp;
  },
  calculate(input) {
    const signatureLength = readUInt32BE(input, 0);
    const ftypBoxLength = readUInt16BE(input, signatureLength + 2);
    let offset = signatureLength + 4 + ftypBoxLength;
    const nextBoxType = toHexString(input, offset, offset + 4);
    switch (nextBoxType) {
      case BoxTypes.rreq: {
        const MAGIC = 4;
        offset = offset + 4 + MAGIC + calculateRREQLength(input.slice(offset + 4));
        return parseIHDR(input.slice(offset + 8, offset + 24));
      }
      case BoxTypes.jp2h: {
        return parseIHDR(input.slice(offset + 8, offset + 24));
      }
      default: {
        throw new TypeError(
          "Unsupported header found: " + toUTF8String(input, offset, offset + 4)
        );
      }
    }
  }
};

const EXIF_MARKER = "45786966";
const APP1_DATA_SIZE_BYTES = 2;
const EXIF_HEADER_BYTES = 6;
const TIFF_BYTE_ALIGN_BYTES = 2;
const BIG_ENDIAN_BYTE_ALIGN = "4d4d";
const LITTLE_ENDIAN_BYTE_ALIGN = "4949";
const IDF_ENTRY_BYTES = 12;
const NUM_DIRECTORY_ENTRIES_BYTES = 2;
function isEXIF(input) {
  return toHexString(input, 2, 6) === EXIF_MARKER;
}
function extractSize(input, index) {
  return {
    height: readUInt16BE(input, index),
    width: readUInt16BE(input, index + 2)
  };
}
function extractOrientation(exifBlock, isBigEndian) {
  const idfOffset = 8;
  const offset = EXIF_HEADER_BYTES + idfOffset;
  const idfDirectoryEntries = readUInt(exifBlock, 16, offset, isBigEndian);
  for (let directoryEntryNumber = 0; directoryEntryNumber < idfDirectoryEntries; directoryEntryNumber++) {
    const start = offset + NUM_DIRECTORY_ENTRIES_BYTES + directoryEntryNumber * IDF_ENTRY_BYTES;
    const end = start + IDF_ENTRY_BYTES;
    if (start > exifBlock.length) {
      return;
    }
    const block = exifBlock.slice(start, end);
    const tagNumber = readUInt(block, 16, 0, isBigEndian);
    if (tagNumber === 274) {
      const dataFormat = readUInt(block, 16, 2, isBigEndian);
      if (dataFormat !== 3) {
        return;
      }
      const numberOfComponents = readUInt(block, 32, 4, isBigEndian);
      if (numberOfComponents !== 1) {
        return;
      }
      return readUInt(block, 16, 8, isBigEndian);
    }
  }
}
function validateExifBlock(input, index) {
  const exifBlock = input.slice(APP1_DATA_SIZE_BYTES, index);
  const byteAlign = toHexString(
    exifBlock,
    EXIF_HEADER_BYTES,
    EXIF_HEADER_BYTES + TIFF_BYTE_ALIGN_BYTES
  );
  const isBigEndian = byteAlign === BIG_ENDIAN_BYTE_ALIGN;
  const isLittleEndian = byteAlign === LITTLE_ENDIAN_BYTE_ALIGN;
  if (isBigEndian || isLittleEndian) {
    return extractOrientation(exifBlock, isBigEndian);
  }
}
function validateInput(input, index) {
  if (index > input.length) {
    throw new TypeError("Corrupt JPG, exceeded buffer limits");
  }
  if (input[index] !== 255) {
    throw new TypeError("Invalid JPG, marker table corrupted");
  }
}
const JPG = {
  validate: (input) => toHexString(input, 0, 2) === "ffd8",
  calculate(input) {
    input = input.slice(4);
    let orientation;
    let next;
    while (input.length > 0) {
      const i = readUInt16BE(input, 0);
      if (isEXIF(input)) {
        orientation = validateExifBlock(input, i);
      }
      validateInput(input, i);
      next = input[i + 1];
      if (next === 192 || next === 193 || next === 194) {
        const size = extractSize(input, i + 5);
        if (!orientation) {
          return size;
        }
        return {
          height: size.height,
          orientation,
          width: size.width
        };
      }
      input = input.slice(i + 2);
    }
    throw new TypeError("Invalid JPG, no size found");
  }
};

const KTX = {
  validate: (input) => toUTF8String(input, 1, 7) === "KTX 11",
  calculate: (input) => ({
    height: readUInt32LE(input, 40),
    width: readUInt32LE(input, 36)
  })
};

const pngSignature = "PNG\r\n\n";
const pngImageHeaderChunkName = "IHDR";
const pngFriedChunkName = "CgBI";
const PNG = {
  validate(input) {
    if (pngSignature === toUTF8String(input, 1, 8)) {
      let chunkName = toUTF8String(input, 12, 16);
      if (chunkName === pngFriedChunkName) {
        chunkName = toUTF8String(input, 28, 32);
      }
      if (chunkName !== pngImageHeaderChunkName) {
        throw new TypeError("Invalid PNG");
      }
      return true;
    }
    return false;
  },
  calculate(input) {
    if (toUTF8String(input, 12, 16) === pngFriedChunkName) {
      return {
        height: readUInt32BE(input, 36),
        width: readUInt32BE(input, 32)
      };
    }
    return {
      height: readUInt32BE(input, 20),
      width: readUInt32BE(input, 16)
    };
  }
};

const PNMTypes = {
  P1: "pbm/ascii",
  P2: "pgm/ascii",
  P3: "ppm/ascii",
  P4: "pbm",
  P5: "pgm",
  P6: "ppm",
  P7: "pam",
  PF: "pfm"
};
const handlers = {
  default: (lines) => {
    let dimensions = [];
    while (lines.length > 0) {
      const line = lines.shift();
      if (line[0] === "#") {
        continue;
      }
      dimensions = line.split(" ");
      break;
    }
    if (dimensions.length === 2) {
      return {
        height: Number.parseInt(dimensions[1], 10),
        width: Number.parseInt(dimensions[0], 10)
      };
    } else {
      throw new TypeError("Invalid PNM");
    }
  },
  pam: (lines) => {
    const size = {};
    while (lines.length > 0) {
      const line = lines.shift();
      if (line.length > 16 || (line.codePointAt(0) || 0) > 128) {
        continue;
      }
      const [key, value] = line.split(" ");
      if (key && value) {
        size[key.toLowerCase()] = Number.parseInt(value, 10);
      }
      if (size.height && size.width) {
        break;
      }
    }
    if (size.height && size.width) {
      return {
        height: size.height,
        width: size.width
      };
    } else {
      throw new TypeError("Invalid PAM");
    }
  }
};
const PNM = {
  validate: (input) => toUTF8String(input, 0, 2) in PNMTypes,
  calculate(input) {
    const signature = toUTF8String(input, 0, 2);
    const type = PNMTypes[signature];
    const lines = toUTF8String(input, 3).split(/[\n\r]+/);
    const handler = handlers[type] || handlers.default;
    return handler(lines);
  }
};

const PSD = {
  validate: (input) => toUTF8String(input, 0, 4) === "8BPS",
  calculate: (input) => ({
    height: readUInt32BE(input, 14),
    width: readUInt32BE(input, 18)
  })
};

const svgReg = /<svg\s([^"'>]|"[^"]*"|'[^']*')*>/;
const extractorRegExps = {
  height: /\sheight=(["'])([^%]+?)\1/,
  root: svgReg,
  viewbox: /\sviewbox=(["'])(.+?)\1/i,
  width: /\swidth=(["'])([^%]+?)\1/
};
const INCH_CM = 2.54;
const units = {
  in: 96,
  cm: 96 / INCH_CM,
  em: 16,
  ex: 8,
  m: 96 / INCH_CM * 100,
  mm: 96 / INCH_CM / 10,
  pc: 96 / 72 / 12,
  pt: 96 / 72,
  px: 1
};
const unitsReg = new RegExp(
  `^([0-9.]+(?:e\\d+)?)(${Object.keys(units).join("|")})?$`
);
function parseLength(len) {
  const m = unitsReg.exec(len);
  if (!m) {
    return void 0;
  }
  return Math.round(Number(m[1]) * (units[m[2]] || 1));
}
function parseViewbox(viewbox) {
  const bounds = viewbox.split(" ");
  return {
    height: parseLength(bounds[3]),
    width: parseLength(bounds[2])
  };
}
function parseAttributes(root) {
  const width = root.match(extractorRegExps.width);
  const height = root.match(extractorRegExps.height);
  const viewbox = root.match(extractorRegExps.viewbox);
  return {
    height: height && parseLength(height[2]),
    viewbox: viewbox && parseViewbox(viewbox[2]),
    width: width && parseLength(width[2])
  };
}
function calculateByDimensions(attrs) {
  return {
    height: attrs.height,
    width: attrs.width
  };
}
function calculateByViewbox(attrs, viewbox) {
  const ratio = viewbox.width / viewbox.height;
  if (attrs.width) {
    return {
      height: Math.floor(attrs.width / ratio),
      width: attrs.width
    };
  }
  if (attrs.height) {
    return {
      height: attrs.height,
      width: Math.floor(attrs.height * ratio)
    };
  }
  return {
    height: viewbox.height,
    width: viewbox.width
  };
}
const SVG = {
  // Scan only the first kilo-byte to speed up the check on larger files
  validate: (input) => svgReg.test(toUTF8String(input, 0, 1e3)),
  calculate(input) {
    const root = toUTF8String(input).match(extractorRegExps.root);
    if (root) {
      const attrs = parseAttributes(root[0]);
      if (attrs.width && attrs.height) {
        return calculateByDimensions(attrs);
      }
      if (attrs.viewbox) {
        return calculateByViewbox(attrs, attrs.viewbox);
      }
    }
    throw new TypeError("Invalid SVG");
  }
};

const TGA = {
  validate(input) {
    return readUInt16LE(input, 0) === 0 && readUInt16LE(input, 4) === 0;
  },
  calculate(input) {
    return {
      height: readUInt16LE(input, 14),
      width: readUInt16LE(input, 12)
    };
  }
};

function readIFD(buffer, isBigEndian) {
  const ifdOffset = readUInt(buffer, 32, 4, isBigEndian);
  let bufferSize = 1024;
  const fileSize = buffer.length;
  if (ifdOffset + bufferSize > fileSize) {
    bufferSize = fileSize - ifdOffset - 10;
  }
  return buffer.slice(ifdOffset + 2, ifdOffset + 2 + bufferSize);
}
function readValue(buffer, isBigEndian) {
  const low = readUInt(buffer, 16, 8, isBigEndian);
  const high = readUInt(buffer, 16, 10, isBigEndian);
  return (high << 16) + low;
}
function nextTag(buffer) {
  if (buffer.length > 24) {
    return buffer.slice(12);
  }
}
function extractTags(buffer, isBigEndian) {
  const tags = {};
  let temp = buffer;
  while (temp && temp.length > 0) {
    const code = readUInt(temp, 16, 0, isBigEndian);
    const type = readUInt(temp, 16, 2, isBigEndian);
    const length = readUInt(temp, 32, 4, isBigEndian);
    if (code === 0) {
      break;
    } else {
      if (length === 1 && (type === 3 || type === 4)) {
        tags[code] = readValue(temp, isBigEndian);
      }
      temp = nextTag(temp);
    }
  }
  return tags;
}
function determineEndianness(input) {
  const signature = toUTF8String(input, 0, 2);
  if (signature === "II") {
    return "LE";
  } else if (signature === "MM") {
    return "BE";
  }
}
const signatures = /* @__PURE__ */ new Set([
  // '492049', // currently not supported
  "49492a00",
  // Little endian
  "4d4d002a"
  // Big Endian
  // '4d4d002a', // BigTIFF > 4GB. currently not supported
]);
const TIFF = {
  validate: (input) => signatures.has(toHexString(input, 0, 4)),
  calculate(input) {
    const isBigEndian = determineEndianness(input) === "BE";
    const ifdBuffer = readIFD(input, isBigEndian);
    const tags = extractTags(ifdBuffer, isBigEndian);
    const width = tags[256];
    const height = tags[257];
    if (!width || !height) {
      throw new TypeError("Invalid Tiff. Missing tags");
    }
    return { height, width };
  }
};

function calculateExtended(input) {
  return {
    height: 1 + readUInt24LE(input, 7),
    width: 1 + readUInt24LE(input, 4)
  };
}
function calculateLossless(input) {
  return {
    height: 1 + ((input[4] & 15) << 10 | input[3] << 2 | (input[2] & 192) >> 6),
    width: 1 + ((input[2] & 63) << 8 | input[1])
  };
}
function calculateLossy(input) {
  return {
    height: readInt16LE(input, 8) & 16383,
    width: readInt16LE(input, 6) & 16383
  };
}
const WEBP = {
  validate(input) {
    const riffHeader = toUTF8String(input, 0, 4) === "RIFF";
    const webpHeader = toUTF8String(input, 8, 12) === "WEBP";
    const vp8Header = toUTF8String(input, 12, 15) === "VP8";
    return riffHeader && webpHeader && vp8Header;
  },
  calculate(input) {
    const chunkHeader = toUTF8String(input, 12, 16);
    input = input.slice(20, 30);
    if (chunkHeader === "VP8X") {
      const extendedHeader = input[0];
      const validStart = (extendedHeader & 192) === 0;
      const validEnd = (extendedHeader & 1) === 0;
      if (validStart && validEnd) {
        return calculateExtended(input);
      } else {
        throw new TypeError("Invalid WebP");
      }
    }
    if (chunkHeader === "VP8 " && input[0] !== 47) {
      return calculateLossy(input);
    }
    const signature = toHexString(input, 3, 6);
    if (chunkHeader === "VP8L" && signature !== "9d012a") {
      return calculateLossless(input);
    }
    throw new TypeError("Invalid WebP");
  }
};

const AVIF = {
  validate: (input) => toUTF8String(input, 8, 12) === "avif",
  calculate: (input) => {
    const metaBox = findBox(input, "meta");
    const iprpBox = findBox(
      input,
      "iprp",
      metaBox.offset + 12,
      metaBox.offset + metaBox.size
    );
    const ipcoBox = findBox(
      input,
      "ipco",
      iprpBox.offset + 8,
      iprpBox.offset + iprpBox.size
    );
    const ispeBox = findBox(
      input,
      "ispe",
      ipcoBox.offset + 8,
      ipcoBox.offset + ipcoBox.size
    );
    const width = readUInt32BE(input, ispeBox.offset + 12);
    const height = readUInt32BE(input, ispeBox.offset + 16);
    return { width, height };
  }
};
function findBox(input, type, startOffset = 0, endOffset = input.length) {
  for (let offset = startOffset; offset < endOffset; ) {
    const size = readUInt32BE(input, offset);
    const boxType = toUTF8String(input, offset + 4, offset + 8);
    if (boxType === type) {
      return { offset, size };
    }
    if (size <= 0 || offset + size > endOffset) {
      break;
    }
    offset += size;
  }
  throw new Error(`${type} box not found`);
}

const typeHandlers = {
  bmp: BMP,
  cur: CUR,
  dds: DDS,
  gif: GIF,
  icns: ICNS,
  ico: ICO,
  j2c: J2C,
  jp2: JP2,
  jpg: JPG,
  ktx: KTX,
  png: PNG,
  pnm: PNM,
  psd: PSD,
  svg: SVG,
  tga: TGA,
  tiff: TIFF,
  webp: WEBP,
  avif: AVIF
};

const keys = Object.keys(typeHandlers);
const firstBytes = {
  56: "psd",
  66: "bmp",
  68: "dds",
  71: "gif",
  73: "tiff",
  77: "tiff",
  82: "webp",
  105: "icns",
  137: "png",
  255: "jpg"
};
function detector(input) {
  const byte = input[0];
  if (byte in firstBytes) {
    const type = firstBytes[byte];
    if (type && typeHandlers[type].validate(input)) {
      return type;
    }
  }
  return keys.find((key) => typeHandlers[key].validate(input));
}

function imageMeta(input) {
  if (!(input instanceof Uint8Array)) {
    throw new TypeError("Input should be a Uint8Array");
  }
  const type = detector(input);
  if (type !== void 0 && type in typeHandlers) {
    const size = typeHandlers[type].calculate(input);
    if (size !== void 0) {
      size.type = type;
      return size;
    }
  }
  throw new TypeError(`Unsupported file type: ${type}`);
}

const _DRIVE_LETTER_START_RE = /^[A-Za-z]:\//;
function normalizeWindowsPath(input = "") {
  if (!input) {
    return input;
  }
  return input.replace(/\\/g, "/").replace(_DRIVE_LETTER_START_RE, (r) => r.toUpperCase());
}

const _UNC_REGEX = /^[/\\]{2}/;
const _IS_ABSOLUTE_RE = /^[/\\](?![/\\])|^[/\\]{2}(?!\.)|^[A-Za-z]:[/\\]/;
const _DRIVE_LETTER_RE = /^[A-Za-z]:$/;
const _ROOT_FOLDER_RE = /^\/([A-Za-z]:)?$/;
const normalize = function(path) {
  if (path.length === 0) {
    return ".";
  }
  path = normalizeWindowsPath(path);
  const isUNCPath = path.match(_UNC_REGEX);
  const isPathAbsolute = isAbsolute(path);
  const trailingSeparator = path[path.length - 1] === "/";
  path = normalizeString(path, !isPathAbsolute);
  if (path.length === 0) {
    if (isPathAbsolute) {
      return "/";
    }
    return trailingSeparator ? "./" : ".";
  }
  if (trailingSeparator) {
    path += "/";
  }
  if (_DRIVE_LETTER_RE.test(path)) {
    path += "/";
  }
  if (isUNCPath) {
    if (!isPathAbsolute) {
      return `//./${path}`;
    }
    return `//${path}`;
  }
  return isPathAbsolute && !isAbsolute(path) ? `/${path}` : path;
};
const join = function(...segments) {
  let path = "";
  for (const seg of segments) {
    if (!seg) {
      continue;
    }
    if (path.length > 0) {
      const pathTrailing = path[path.length - 1] === "/";
      const segLeading = seg[0] === "/";
      const both = pathTrailing && segLeading;
      if (both) {
        path += seg.slice(1);
      } else {
        path += pathTrailing || segLeading ? seg : `/${seg}`;
      }
    } else {
      path += seg;
    }
  }
  return normalize(path);
};
function cwd() {
  if (typeof process !== "undefined" && typeof process.cwd === "function") {
    return process.cwd().replace(/\\/g, "/");
  }
  return "/";
}
const resolve = function(...arguments_) {
  arguments_ = arguments_.map((argument) => normalizeWindowsPath(argument));
  let resolvedPath = "";
  let resolvedAbsolute = false;
  for (let index = arguments_.length - 1; index >= -1 && !resolvedAbsolute; index--) {
    const path = index >= 0 ? arguments_[index] : cwd();
    if (!path || path.length === 0) {
      continue;
    }
    resolvedPath = `${path}/${resolvedPath}`;
    resolvedAbsolute = isAbsolute(path);
  }
  resolvedPath = normalizeString(resolvedPath, !resolvedAbsolute);
  if (resolvedAbsolute && !isAbsolute(resolvedPath)) {
    return `/${resolvedPath}`;
  }
  return resolvedPath.length > 0 ? resolvedPath : ".";
};
function normalizeString(path, allowAboveRoot) {
  let res = "";
  let lastSegmentLength = 0;
  let lastSlash = -1;
  let dots = 0;
  let char = null;
  for (let index = 0; index <= path.length; ++index) {
    if (index < path.length) {
      char = path[index];
    } else if (char === "/") {
      break;
    } else {
      char = "/";
    }
    if (char === "/") {
      if (lastSlash === index - 1 || dots === 1) ; else if (dots === 2) {
        if (res.length < 2 || lastSegmentLength !== 2 || res[res.length - 1] !== "." || res[res.length - 2] !== ".") {
          if (res.length > 2) {
            const lastSlashIndex = res.lastIndexOf("/");
            if (lastSlashIndex === -1) {
              res = "";
              lastSegmentLength = 0;
            } else {
              res = res.slice(0, lastSlashIndex);
              lastSegmentLength = res.length - 1 - res.lastIndexOf("/");
            }
            lastSlash = index;
            dots = 0;
            continue;
          } else if (res.length > 0) {
            res = "";
            lastSegmentLength = 0;
            lastSlash = index;
            dots = 0;
            continue;
          }
        }
        if (allowAboveRoot) {
          res += res.length > 0 ? "/.." : "..";
          lastSegmentLength = 2;
        }
      } else {
        if (res.length > 0) {
          res += `/${path.slice(lastSlash + 1, index)}`;
        } else {
          res = path.slice(lastSlash + 1, index);
        }
        lastSegmentLength = index - lastSlash - 1;
      }
      lastSlash = index;
      dots = 0;
    } else if (char === "." && dots !== -1) {
      ++dots;
    } else {
      dots = -1;
    }
  }
  return res;
}
const isAbsolute = function(p) {
  return _IS_ABSOLUTE_RE.test(p);
};
const relative = function(from, to) {
  const _from = resolve(from).replace(_ROOT_FOLDER_RE, "$1").split("/");
  const _to = resolve(to).replace(_ROOT_FOLDER_RE, "$1").split("/");
  if (_to[0][1] === ":" && _from[0][1] === ":" && _from[0] !== _to[0]) {
    return _to.join("/");
  }
  const _fromCopy = [..._from];
  for (const segment of _fromCopy) {
    if (_to[0] !== segment) {
      break;
    }
    _from.shift();
    _to.shift();
  }
  return [..._from.map(() => ".."), ..._to].join("/");
};

const DEBOUNCE_DEFAULTS = {
  trailing: true
};
function debounce(fn, wait = 25, options = {}) {
  options = { ...DEBOUNCE_DEFAULTS, ...options };
  if (!Number.isFinite(wait)) {
    throw new TypeError("Expected `wait` to be a finite number");
  }
  let leadingValue;
  let timeout;
  let resolveList = [];
  let currentPromise;
  let trailingArgs;
  const applyFn = (_this, args) => {
    currentPromise = _applyPromised(fn, _this, args);
    currentPromise.finally(() => {
      currentPromise = null;
      if (options.trailing && trailingArgs && !timeout) {
        const promise = applyFn(_this, trailingArgs);
        trailingArgs = null;
        return promise;
      }
    });
    return currentPromise;
  };
  return function(...args) {
    if (currentPromise) {
      if (options.trailing) {
        trailingArgs = args;
      }
      return currentPromise;
    }
    return new Promise((resolve) => {
      const shouldCallNow = !timeout && options.leading;
      clearTimeout(timeout);
      timeout = setTimeout(() => {
        timeout = null;
        const promise = options.leading ? leadingValue : applyFn(this, args);
        for (const _resolve of resolveList) {
          _resolve(promise);
        }
        resolveList = [];
      }, wait);
      if (shouldCallNow) {
        leadingValue = applyFn(this, args);
        resolve(leadingValue);
      } else {
        resolveList.push(resolve);
      }
    });
  };
}
async function _applyPromised(fn, _this, args) {
  return await fn.apply(_this, args);
}

function guessType(path) {
  if (/\.(?:png|jpe?g|jxl|gif|svg|webp|avif|ico|bmp|tiff?)$/i.test(path))
    return "image";
  if (/\.(?:mp4|webm|ogv|mov|avi|flv|wmv|mpg|mpeg|mkv|3gp|3g2|ts|mts|m2ts|vob|ogm|ogx|rm|rmvb|asf|amv|divx|m4v|svi|viv|f4v|f4p|f4a|f4b)$/i.test(path))
    return "video";
  if (/\.(?:mp3|wav|ogg|flac|aac|wma|alac|ape|ac3|dts|tta|opus|amr|aiff|au|mid|midi|ra|rm|wv|weba|dss|spx|vox|tak|dsf|dff|dsd|cda)$/i.test(path))
    return "audio";
  if (/\.(?:woff2?|eot|ttf|otf|ttc|pfa|pfb|pfm|afm)/i.test(path))
    return "font";
  if (/\.(?:json[5c]?|te?xt|[mc]?[jt]sx?|md[cx]?|markdown|ya?ml|toml)/i.test(path))
    return "text";
  if (/\.wasm/i.test(path))
    return "wasm";
  return "other";
}
function getAssetsFunctions(ctx) {
  const { server, config } = ctx;
  const _imageMetaCache = /* @__PURE__ */ new Map();
  let cache = null;
  async function scan() {
    const dir = resolve(config.root);
    const baseURL = config.base;
    const publicDir = config.publicDir;
    const relativePublicDir = publicDir === "" ? "" : `${relative(dir, publicDir)}/`;
    const files = await fg([
      // image
      "**/*.(png|jpg|jpeg|gif|svg|webp|avif|ico|bmp|tiff)",
      // video
      "**/*.(mp4|webm|ogv|mov|avi|flv|wmv|mpg|mpeg|mkv|3gp|3g2|m2ts|vob|ogm|ogx|rm|rmvb|asf|amv|divx|m4v|svi|viv|f4v|f4p|f4a|f4b)",
      // audio
      "**/*.(mp3|wav|ogg|flac|aac|wma|alac|ape|ac3|dts|tta|opus|amr|aiff|au|mid|midi|ra|rm|wv|weba|dss|spx|vox|tak|dsf|dff|dsd|cda)",
      // font
      "**/*.(woff2?|eot|ttf|otf|ttc|pfa|pfb|pfm|afm)",
      // text
      "**/*.(json|json5|jsonc|txt|text|tsx|jsx|md|mdx|mdc|markdown|yaml|yml|toml)",
      // wasm
      "**/*.wasm"
    ], {
      cwd: dir,
      onlyFiles: true,
      caseSensitiveMatch: false,
      ignore: [
        "**/node_modules/**",
        "**/dist/**",
        "**/package-lock.*",
        "**/pnpm-lock.*",
        "**/pnpm-workspace.*"
      ]
    });
    cache = await Promise.all(files.map(async (relativePath) => {
      const filePath = resolve(dir, relativePath);
      const stat = await fsp.lstat(filePath);
      const path = relativePath.replace(relativePublicDir, "");
      return {
        path,
        relativePath,
        publicPath: join(baseURL, path),
        filePath,
        type: guessType(relativePath),
        size: stat.size,
        mtime: stat.mtimeMs
      };
    }));
    return cache;
  }
  async function getAssetImporters(url) {
    const importers = [];
    const moduleGraph = server.moduleGraph;
    const module = await moduleGraph.getModuleByUrl(url);
    if (module) {
      for (const importer of module.importers) {
        importers.push({
          url: importer.url,
          id: importer.id
        });
      }
    }
    return importers;
  }
  const debouncedAssetsUpdated = debounce(() => {
    getViteRpcServer?.()?.broadcast?.emit("assetsUpdated");
  }, 100);
  server.watcher.on("all", (event) => {
    if (event !== "change")
      debouncedAssetsUpdated();
  });
  return {
    async getStaticAssets() {
      return await scan();
    },
    async getAssetImporters(url) {
      return await getAssetImporters(url);
    },
    async getImageMeta(filepath) {
      if (_imageMetaCache.has(filepath))
        return _imageMetaCache.get(filepath);
      try {
        const meta = imageMeta(await fsp.readFile(filepath));
        _imageMetaCache.set(filepath, meta);
        return meta;
      } catch (e) {
        _imageMetaCache.set(filepath, void 0);
        console.error(e);
        return void 0;
      }
    },
    async getTextAssetContent(filepath, limit = 300) {
      try {
        const content = await fsp.readFile(filepath, "utf-8");
        return content.slice(0, limit);
      } catch (e) {
        console.error(e);
        return void 0;
      }
    }
  };
}

function getConfigFunctions(ctx) {
  return {
    getRoot() {
      return ctx.config.root;
    }
  };
}

function getGraphFunctions(ctx) {
  const { rpc, server } = ctx;
  const debouncedModuleUpdated = debounce(() => {
    getViteRpcServer?.()?.broadcast?.emit("graphModuleUpdated");
  }, 100);
  server.middlewares.use((_, __, next) => {
    debouncedModuleUpdated();
    next();
  });
  return {
    async getGraphModules() {
      const list = await rpc.list();
      const modules = list?.modules || [];
      return modules;
    }
  };
}

function getRpcFunctions(ctx) {
  return {
    heartbeat() {
      return true;
    },
    ...getAssetsFunctions(ctx),
    ...getConfigFunctions(ctx),
    ...getGraphFunctions(ctx)
  };
}

function removeUrlQuery(url) {
  return url.replace(/\?.*$/, "");
}

function getVueDevtoolsPath() {
  const pluginPath = normalizePath(path$1.dirname(fileURLToPath(import.meta.url)));
  return pluginPath.replace(/\/dist$/, "//src");
}
const toggleComboKeysMap = {
  option: process.platform === "darwin" ? "Option(\u2325)" : "Alt(\u2325)",
  meta: "Command(\u2318)",
  shift: "Shift(\u21E7)"
};
function normalizeComboKeyPrint(toggleComboKey) {
  return toggleComboKey.split("-").map((key) => toggleComboKeysMap[key] || key[0].toUpperCase() + key.slice(1)).join(dim("+"));
}
const devtoolsNextResourceSymbol = "?__vue-devtools-next-resource";
const defaultOptions = {
  appendTo: "",
  componentInspector: true,
  launchEditor: process.env.LAUNCH_EDITOR ?? "code"
};
function mergeOptions(options) {
  return Object.assign({}, defaultOptions, options);
}
function VitePluginVueDevTools(options) {
  const vueDevtoolsPath = getVueDevtoolsPath();
  const inspect = Inspect({
    silent: true
  });
  const pluginOptions = mergeOptions(options ?? {});
  let config;
  function configureServer(server) {
    const base = server.config.base || "/";
    server.middlewares.use(`${base}__devtools__`, sirv(DIR_CLIENT, {
      single: true,
      dev: true,
      setHeaders(response) {
        if (config.server.headers == null)
          return;
        Object.entries(config.server.headers).forEach(([key, value]) => {
          if (value == null)
            return;
          response.setHeader(key, value);
        });
      }
    }));
    setViteServerContext(server);
    const rpcFunctions = getRpcFunctions({
      rpc: inspect.api.rpc,
      server,
      config
    });
    createViteServerRpc(rpcFunctions);
    const _printUrls = server.printUrls;
    const colorUrl = (url) => cyan(url.replace(/:(\d+)\//, (_, port) => `:${bold(port)}/`));
    server.printUrls = () => {
      const urls = server.resolvedUrls;
      const keys = normalizeComboKeyPrint("option-shift-d");
      _printUrls();
      for (const url of urls.local) {
        const devtoolsUrl = url.endsWith("/") ? `${url}__devtools__/` : `${url}/__devtools__/`;
        console.log(`  ${green("\u279C")}  ${bold("Vue DevTools")}: ${green(`Open ${colorUrl(`${devtoolsUrl}`)} as a separate window`)}`);
      }
      console.log(`  ${green("\u279C")}  ${bold("Vue DevTools")}: ${green(`Press ${yellow(keys)} in App to toggle the Vue DevTools`)}`);
    };
  }
  const devtoolsOptionsImportee = "virtual:vue-devtools-options";
  const resolvedDevtoolsOptions = `\0${devtoolsOptionsImportee}`;
  const plugin = {
    name: "vite-plugin-vue-devtools",
    enforce: "pre",
    apply: "serve",
    configResolved(resolvedConfig) {
      config = resolvedConfig;
    },
    configureServer(server) {
      configureServer(server);
    },
    async resolveId(importee) {
      if (importee === devtoolsOptionsImportee) {
        return resolvedDevtoolsOptions;
      } else if (importee.startsWith("virtual:vue-devtools-path:")) {
        const resolved = importee.replace("virtual:vue-devtools-path:", `${vueDevtoolsPath}/`);
        return `${resolved}${devtoolsNextResourceSymbol}`;
      }
    },
    async load(id) {
      if (id === resolvedDevtoolsOptions) {
        return `export default ${JSON.stringify({ base: config.base, componentInspector: pluginOptions.componentInspector })}`;
      } else if (id.endsWith(devtoolsNextResourceSymbol)) {
        const filename = removeUrlQuery(id);
        return await fs$4.promises.readFile(filename, "utf-8");
      }
    },
    transform(code, id, options2) {
      if (options2?.ssr)
        return;
      const { appendTo } = pluginOptions;
      const [filename] = id.split("?", 2);
      if (appendTo && (typeof appendTo === "string" && filename.endsWith(appendTo) || appendTo instanceof RegExp && appendTo.test(filename))) {
        code = `import 'virtual:vue-devtools-path:overlay.js';
${code}`;
      }
      return code;
    },
    transformIndexHtml(html) {
      if (pluginOptions.appendTo)
        return;
      return {
        html,
        tags: [
          {
            tag: "script",
            injectTo: "head-prepend",
            attrs: {
              type: "module",
              src: `${config.base || "/"}@id/virtual:vue-devtools-path:overlay.js`
            }
          },
          // inject inspector script manually to ensure it's loaded after vue-devtools
          pluginOptions.componentInspector && {
            tag: "script",
            injectTo: "head-prepend",
            launchEditor: pluginOptions.launchEditor,
            attrs: {
              type: "module",
              src: `${config.base || "/"}@id/virtual:vue-inspector-path:load.js`
            }
          }
        ].filter(Boolean)
      };
    },
    async buildEnd() {
    }
  };
  return [
    inspect,
    pluginOptions.componentInspector && VueInspector({
      toggleComboKey: "",
      toggleButtonVisibility: "never",
      launchEditor: pluginOptions.launchEditor,
      ...typeof pluginOptions.componentInspector === "boolean" ? {} : pluginOptions.componentInspector,
      appendTo: pluginOptions.appendTo || "manually"
    }),
    plugin
  ].filter(Boolean);
}

export { VitePluginVueDevTools as default };
