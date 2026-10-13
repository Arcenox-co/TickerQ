// Backend errors arrive as JSON — `{"error":"..."}`, or ProblemDetails-style
// `{"detail":"..."}` / `{"title":"..."}`. Rendering the raw body surfaced the
// braces and quotes to the user as `400 Bad Request — {"error":"..."}`. Pull
// the human-readable field out in priority order, keeping the plain text when
// the body isn't JSON (or carries no usable field).

const MESSAGE_FIELDS = ["error", "detail", "title"] as const;

function extractMessage(body: string): string {
  const text = body.trim();
  if (!text) return "";
  try {
    const parsed: unknown = JSON.parse(text);
    if (parsed && typeof parsed === "object") {
      for (const field of MESSAGE_FIELDS) {
        const value = (parsed as Record<string, unknown>)[field];
        if (typeof value === "string" && value.trim()) return value.trim();
      }
    }
  } catch {
    /* not JSON — fall through to the raw text */
  }
  return text;
}

/** Formats an HTTP error response into `STATUS statusText — message`. */
export function formatApiErrorMessage(
  status: number,
  statusText: string,
  body: string,
): string {
  const message = extractMessage(body);
  return `${status} ${statusText}${message ? ` — ${message}` : ""}`;
}
