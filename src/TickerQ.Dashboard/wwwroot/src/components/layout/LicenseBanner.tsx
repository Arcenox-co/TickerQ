import { AlertCircle, AlertTriangle, ArrowUpRight, FlaskConical } from "lucide-react";
import { useLicense } from "@/services/hooks";
import { cn } from "@/lib/utils";

const SAFE_ACTION_URLS = new Set([
  "https://license.tickerq.net/pricing",
  "https://license.tickerq.net/billing",
]);

export function LicenseBanner() {
  const { data: license } = useLicense();

  if (!license) return null;

  const visible =
    license.status === "Missing" ||
    license.status === "Invalid" ||
    license.status === "Expired" ||
    license.status === "Expiring" ||
    license.isEvaluation;

  if (!visible) return null;

  const blocking = !license.executionAllowed;
  const Icon = license.isEvaluation && !blocking ? FlaskConical : blocking ? AlertCircle : AlertTriangle;
  const safeActionUrl =
    license.actionUrl && SAFE_ACTION_URLS.has(license.actionUrl)
      ? license.actionUrl
      : null;

  return (
    <div
      role={blocking ? "alert" : "status"}
      aria-live="polite"
      className={cn(
        "flex min-h-10 shrink-0 items-center gap-3 border-b px-4 py-2 text-xs",
        blocking
          ? "border-red-500/25 bg-red-500/10 text-red-700 dark:text-red-300"
          : license.isEvaluation
            ? "border-sky-500/25 bg-sky-500/10 text-sky-800 dark:text-sky-300"
            : "border-amber-500/25 bg-amber-500/10 text-amber-800 dark:text-amber-300"
      )}
    >
      <Icon className="h-4 w-4 shrink-0" aria-hidden="true" />
      <p className="min-w-0 flex-1 leading-5">{license.message}</p>
      {safeActionUrl && license.actionLabel && (
        <a
          href={safeActionUrl}
          target="_blank"
          rel="noreferrer"
          className="inline-flex shrink-0 items-center gap-1 font-semibold underline underline-offset-4 hover:no-underline"
        >
          {license.actionLabel}
          <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
        </a>
      )}
    </div>
  );
}
