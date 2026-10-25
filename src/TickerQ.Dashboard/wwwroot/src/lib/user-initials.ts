/**
 * Up to two initials for the account avatar. Splits on spaces and on the separators common in
 * user names and e-mail addresses, so "Jane Doe", "jane.doe@acme.io", and "jane_doe" all give "JD".
 * The domain of an e-mail address is ignored.
 */
export function userInitials(name: string | null | undefined): string {
  const local = (name ?? "").trim().split("@")[0];
  const parts = local.split(/[\s._-]+/).filter(Boolean);
  if (parts.length === 0) return "?";
  const letters = parts.length === 1
    ? Array.from(parts[0]).slice(0, 2)
    : [Array.from(parts[0])[0], Array.from(parts[parts.length - 1])[0]];
  return letters.join("").toUpperCase();
}
