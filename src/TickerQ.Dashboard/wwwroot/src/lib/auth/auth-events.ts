/**
 * One-way signal from the fetch / SignalR layer to the AuthProvider:
 * "the server refused our credentials, flip to anonymous". Used in cookie
 * mode (where there's no token store to clear) and as the final-401 hook in
 * bearer mode after the refresh-and-retry has been exhausted.
 *
 * Module-level rather than React context because the fetch wrapper isn't a
 * component and can't useContext.
 */
type Listener = () => void;
const listeners = new Set<Listener>();

export function onUnauthenticated(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function fireUnauthenticated(): void {
  for (const l of listeners) l();
}
