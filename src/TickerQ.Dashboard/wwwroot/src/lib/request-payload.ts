// Centralized request-payload transport.
//
// The dashboard API models a ticker's `request` as a byte[] on the server, so
// JSON transport carries it as base64. The browser edits payloads as raw JSON
// text, so every write path must encode text -> UTF-8 base64 and every read
// path must decode base64 -> UTF-8 text. Keeping both sides here guarantees the
// two never drift and that Unicode survives the round trip.

/**
 * Encode raw JSON text for the wire. Returns null for empty / requestless
 * payloads so the server records "no payload" rather than an empty blob.
 */
export function encodeRequestPayload(json: string | null | undefined): string | null {
  if (!json) return null;
  const bytes = new TextEncoder().encode(json);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

/**
 * Decode a base64 payload returned by the API back into raw JSON text for
 * editing. Falls back to the input verbatim when it is not valid base64, which
 * keeps compatibility with legacy rows that stored the JSON unencoded.
 */
export function decodeRequestPayload(base64: string | null | undefined): string {
  if (!base64) return "";
  try {
    const binary = atob(base64);
    const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0));
    return new TextDecoder().decode(bytes);
  } catch {
    return base64;
  }
}
