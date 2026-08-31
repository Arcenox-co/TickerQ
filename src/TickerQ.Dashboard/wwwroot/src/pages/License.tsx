import {
  AlertCircle,
  ArrowUpRight,
  BadgeCheck,
  CalendarClock,
  FileKey,
  FlaskConical,
  KeyRound,
  PackageCheck,
  ShieldCheck,
} from "lucide-react";
import { DataCard, DataCardHeader, PageHeader } from "@/components/cron/PageHeader";
import { cn } from "@/lib/utils";
import { getRuntimeConfig } from "@/lib/runtime-config";
import { useLicense } from "@/services/hooks";
import type { LicenseResponse, LicenseStatus } from "@/services/api-types";

const SAFE_ACTION_URLS = new Set([
  "https://license.tickerq.net/pricing",
  "https://license.tickerq.net/billing",
]);

const STATUS_STYLES: Record<LicenseStatus, string> = {
  Active: "border-emerald-500/25 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300",
  Expiring: "border-amber-500/25 bg-amber-500/10 text-amber-800 dark:text-amber-300",
  Expired: "border-red-500/25 bg-red-500/10 text-red-700 dark:text-red-300",
  Missing: "border-red-500/25 bg-red-500/10 text-red-700 dark:text-red-300",
  Invalid: "border-red-500/25 bg-red-500/10 text-red-700 dark:text-red-300",
};

function formatDate(value: string | null) {
  if (!value) return "Not applicable";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Unavailable";
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
    timeZone: "UTC",
  }).format(date) + " UTC";
}

function DetailRow({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="grid gap-1 border-b border-border/70 px-4 py-3 last:border-b-0 sm:grid-cols-[12rem_1fr] sm:gap-6">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className={cn("min-w-0 break-words text-sm text-foreground", mono && "font-mono text-xs")}>{value}</dd>
    </div>
  );
}

function LicenseSummary({ license }: { license: LicenseResponse }) {
  const safeActionUrl =
    license.actionUrl && SAFE_ACTION_URLS.has(license.actionUrl)
      ? license.actionUrl
      : null;
  const StatusIcon = license.executionAllowed ? BadgeCheck : AlertCircle;

  return (
    <DataCard>
      <div className="flex flex-col gap-4 p-5 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex min-w-0 gap-3">
          <div
            className={cn(
              "mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border",
              STATUS_STYLES[license.status]
            )}
          >
            <StatusIcon className="h-4.5 w-4.5" aria-hidden="true" />
          </div>
          <div className="min-w-0 space-y-1.5">
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="text-base font-semibold">Certificate {license.status.toLowerCase()}</h2>
              <span className={cn("rounded-full border px-2 py-0.5 text-[11px] font-semibold", STATUS_STYLES[license.status])}>
                {license.executionAllowed ? "Execution enabled" : "Execution blocked"}
              </span>
            </div>
            <p className="max-w-3xl text-sm leading-6 text-muted-foreground">{license.message}</p>
          </div>
        </div>
        {safeActionUrl && license.actionLabel && (
          <a
            href={safeActionUrl}
            target="_blank"
            rel="noreferrer"
            className="inline-flex h-9 shrink-0 items-center justify-center gap-1.5 rounded-md border border-border bg-background px-3 text-xs font-semibold text-foreground transition-colors hover:bg-surface-2"
          >
            {license.actionLabel}
            <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
          </a>
        )}
      </div>
    </DataCard>
  );
}

export default function LicensePage() {
  const { data: license, isLoading, isError } = useLicense();
  const config = getRuntimeConfig();

  return (
    <div className="space-y-5">
      <PageHeader
        title="License"
        description="Offline certificate status and runtime authority for this TickerQ host."
      />

      {isLoading && (
        <DataCard className="p-5" aria-live="polite">
          <div className="space-y-3 animate-pulse">
            <div className="h-5 w-44 rounded bg-surface-2" />
            <div className="h-4 w-full max-w-xl rounded bg-surface-2" />
          </div>
        </DataCard>
      )}

      {isError && (
        <DataCard className="border-red-500/25 bg-red-500/5 p-5">
          <div className="flex gap-3 text-red-700 dark:text-red-300" role="alert">
            <AlertCircle className="mt-0.5 h-5 w-5 shrink-0" aria-hidden="true" />
            <div>
              <h2 className="text-sm font-semibold">Certificate status unavailable</h2>
              <p className="mt-1 text-sm opacity-80">The dashboard could not read the host certificate status. Check the host logs and try again.</p>
            </div>
          </div>
        </DataCard>
      )}

      {license && <LicenseSummary license={license} />}

      {license?.isEvaluation && (
        <DataCard className="border-sky-500/25 bg-sky-500/5">
          <div className="flex gap-3 p-4 text-sky-800 dark:text-sky-300">
            <FlaskConical className="mt-0.5 h-5 w-5 shrink-0" aria-hidden="true" />
            <div>
              <h2 className="text-sm font-semibold">Evaluation certificate</h2>
              <p className="mt-1 text-sm leading-6 opacity-85">This certificate is limited to internal, non-production evaluation. Obtain a commercial certificate before running TickerQ in production.</p>
            </div>
          </div>
        </DataCard>
      )}

      <div className="grid gap-4 xl:grid-cols-2">
        <DataCard>
          <DataCardHeader title="Product">
            <PackageCheck className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
          </DataCardHeader>
          <dl>
            <DetailRow label="Product" value="TickerQ" />
            <DetailRow label="Dashboard version" value={config.version || "Development build"} mono />
            <DetailRow label="Verification" value="Offline Ed25519 certificate" />
            <DetailRow label="Runtime policy" value={license?.executionAllowed ? "Licensed execution enabled" : "Fail-closed; dashboard diagnostics only"} />
          </dl>
        </DataCard>

        <DataCard>
          <DataCardHeader title="Entitlement">
            <ShieldCheck className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
          </DataCardHeader>
          <dl>
            <DetailRow label="Workspace" value={license?.workspaceName || "Not available"} />
            <DetailRow label="Workspace ID" value={license?.workspaceId || "Not available"} mono />
            <DetailRow label="Plan" value={license?.plan || "Not available"} />
            <DetailRow label="Certificate kind" value={license?.kind || "Not available"} />
          </dl>
        </DataCard>
      </div>

      <DataCard>
        <DataCardHeader title="Certificate details">
          <FileKey className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
        </DataCardHeader>
        <dl className="grid lg:grid-cols-2 lg:[&>div:nth-child(odd)]:border-r">
          <DetailRow label="Certificate ID" value={license?.licenseId || "Not available"} mono />
          <DetailRow label="Issued" value={formatDate(license?.issuedAt ?? null)} />
          <DetailRow label="Expires" value={formatDate(license?.expiresAt ?? null)} />
          <DetailRow label="Days remaining" value={license?.daysRemaining == null ? "Not applicable" : String(Math.max(0, license.daysRemaining))} />
          <DetailRow label="Schema version" value={license?.schemaVersion == null ? "Not available" : `v${license.schemaVersion}`} />
          <DetailRow label="Signature algorithm" value={license?.algorithm || "Not available"} />
          <DetailRow label="Signing key ID" value={license?.keyId || "Not available"} mono />
          <DetailRow label="Anchored minor line" value={license?.anchoredMinorLine || "Not applicable"} mono />
        </dl>
        <div className="flex items-center gap-2 border-t border-border px-4 py-3 text-xs text-muted-foreground">
          <KeyRound className="h-3.5 w-3.5" aria-hidden="true" />
          Signature and trust checks run locally; raw certificate material is never exposed by the dashboard.
          <CalendarClock className="ml-auto hidden h-3.5 w-3.5 sm:block" aria-hidden="true" />
        </div>
      </DataCard>
    </div>
  );
}
