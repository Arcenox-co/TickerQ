import { createHash } from 'node:crypto';

/**
 * Byte-level canonical JSON + request-contract fingerprint (transport blocker 1).
 *
 * This is the SINGLE canonical format shared with the .NET canonicalizer
 * (`src/TickerQ.Utilities/Serialization/JsonSchemaCanonicalizer.cs` and
 * `TickerRequestContractFingerprint.cs`). Both runtimes MUST emit byte-for-byte identical canonical
 * bytes and therefore identical SHA-256 fingerprints. Regenerate the cross-runtime golden vectors with
 * `node scripts/canonical-vectors.mjs` after any change here and reconcile BOTH sides — never edit one
 * runtime to make a test pass.
 *
 * Format:
 *  - Object keys sorted by UTF-16 code unit (Array.prototype.sort default); arrays kept in order.
 *  - Strings escaped exactly as JSON.stringify: `\" \\ \b \t \n \f \r`, other C0 controls as lowercase
 *    `\u00xx`, all non-ASCII (incl. non-BMP) as raw UTF-8. Unpaired surrogates are rejected.
 *  - Numbers normalized by value: only integer-valued numbers within ±(2^53-1) are accepted
 *    (`1.0`→`1`, `-0`→`0`); fractions, non-integer exponents, and unsafe magnitudes are rejected
 *    (JSON.parse cannot reproduce them byte-for-byte, and the .NET side rejects them symmetrically).
 */

/** Maximum canonicalization nesting depth (matches .NET JsonSchemaCanonicalizer.MaxDepth). */
const MAX_DEPTH = 64;

/** JSON Schema Draft 2020-12 dialect URI (default when the caller omits one). */
export const SCHEMA_DIALECT_2020_12 = 'https://json-schema.org/draft/2020-12/schema';

/**
 * Canonicalizes an object-root schema into the deterministic, cross-runtime canonical JSON string.
 * @throws Error when the root is not an object, nesting is too deep, a string carries an unpaired
 *   surrogate, or a numeric value cannot be represented identically across runtimes.
 */
export function canonicalizeSchema(schema: unknown): string {
    if (schema === null || typeof schema !== 'object' || Array.isArray(schema)) {
        throw new Error('TickerQ: request contract schema must have an object root.');
    }
    const out: string[] = [];
    writeValue(out, schema, 0);
    return out.join('');
}

/**
 * Computes the `sha256:`-prefixed lowercase-hex request-contract fingerprint over a fixed-key envelope
 * `{contractVersion, mediaType, required, schema}`, identical to the .NET pre-image.
 */
export function computeContractFingerprint(
    canonicalSchema: string,
    mediaType: string,
    required: boolean,
    contractVersion: number,
): string {
    const preimage =
        '{"contractVersion":' + String(contractVersion) +
        ',"mediaType":' + canonicalString(mediaType) +
        ',"required":' + (required ? 'true' : 'false') +
        ',"schema":' + canonicalSchema +
        '}';
    return 'sha256:' + createHash('sha256').update(preimage, 'utf8').digest('hex');
}

function writeValue(out: string[], value: unknown, depth: number): void {
    if (depth > MAX_DEPTH) {
        throw new Error(`TickerQ: schema nesting exceeds the maximum canonicalization depth of ${MAX_DEPTH}.`);
    }
    if (value === null) { out.push('null'); return; }
    switch (typeof value) {
        case 'boolean': out.push(value ? 'true' : 'false'); return;
        case 'string': out.push(canonicalString(value)); return;
        case 'number': out.push(canonicalNumber(value)); return;
        case 'bigint':
            throw new Error(`TickerQ: schema contains a bigint value '${value}n'; JSON schemas cannot carry bigints.`);
        case 'object':
            if (Array.isArray(value)) { writeArray(out, value, depth); return; }
            writeObject(out, value as Record<string, unknown>, depth);
            return;
        default:
            // undefined / function / symbol have no JSON representation.
            throw new Error(`TickerQ: schema contains an unsupported value of type '${typeof value}'.`);
    }
}

function writeArray(out: string[], value: readonly unknown[], depth: number): void {
    out.push('[');
    for (let i = 0; i < value.length; i++) {
        if (i > 0) out.push(',');
        const item = value[i];
        // JSON.stringify emits null for these array holes; mirror it for parity.
        if (item === undefined || typeof item === 'function' || typeof item === 'symbol') {
            out.push('null');
        } else {
            writeValue(out, item, depth + 1);
        }
    }
    out.push(']');
}

function writeObject(out: string[], value: Record<string, unknown>, depth: number): void {
    // Array.prototype.sort's default comparator orders by UTF-16 code unit — identical to .NET
    // string.CompareOrdinal.
    const keys = Object.keys(value).sort();
    out.push('{');
    let wrote = 0;
    for (const key of keys) {
        const member = value[key];
        // undefined / function / symbol values are omitted by JSON.stringify; mirror it.
        if (member === undefined || typeof member === 'function' || typeof member === 'symbol') continue;
        if (wrote > 0) out.push(',');
        out.push(canonicalString(key));
        out.push(':');
        writeValue(out, member, depth + 1);
        wrote++;
    }
    out.push('}');
}

/**
 * Escapes a string exactly as ECMA-262 JSON.stringify, then rejects unpaired surrogates so the result
 * is losslessly representable as UTF-8 on both runtimes (the .NET decoder refuses lone surrogates).
 */
function canonicalString(value: string): string {
    for (let i = 0; i < value.length; i++) {
        const code = value.charCodeAt(i);
        if (code >= 0xd800 && code <= 0xdbff) {
            const next = i + 1 < value.length ? value.charCodeAt(i + 1) : 0;
            if (next >= 0xdc00 && next <= 0xdfff) { i++; continue; } // valid pair
            throw new Error('TickerQ: schema string contains an unpaired surrogate and cannot be canonicalized.');
        }
        if (code >= 0xdc00 && code <= 0xdfff) {
            throw new Error('TickerQ: schema string contains an unpaired surrogate and cannot be canonicalized.');
        }
    }
    // JSON.stringify already produces the exact escaping we require (lowercase \u00xx, short escapes,
    // raw non-ASCII/non-BMP).
    return JSON.stringify(value);
}

function canonicalNumber(value: number): string {
    if (!Number.isFinite(value)) {
        throw new Error(`TickerQ: schema numeric value '${value}' is not finite.`);
    }
    if (!Number.isInteger(value)) {
        throw new Error(
            `TickerQ: schema numeric value '${value}' is not integer-valued. Fractional/exponent literals ` +
            'cannot be reproduced byte-for-byte across runtimes; use an integer or omit the constraint.',
        );
    }
    if (!Number.isSafeInteger(value)) {
        throw new Error(
            `TickerQ: schema numeric value '${value}' exceeds the JS safe-integer range ±(2^53-1) and ` +
            'cannot be represented identically across runtimes.',
        );
    }
    return String(value); // String(-0) === "0"
}
