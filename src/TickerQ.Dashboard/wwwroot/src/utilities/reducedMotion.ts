/**
 * Applies the "reduce motion" preference to the document root.
 * Honors the client's OS/browser "prefers-reduced-motion" setting automatically,
 * and also respects a site-wide override from the dashboard's server configuration.
 */
export function applyReducedMotionPreference(): void {
  const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  const forcedByConfig = window.TickerQConfig?.reducedMotion ?? false

  document.documentElement.classList.toggle('reduced-motion', prefersReducedMotion || forcedByConfig)
}
