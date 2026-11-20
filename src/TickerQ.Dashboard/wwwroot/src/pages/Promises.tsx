import { useState } from "react";
import { Handshake } from "lucide-react";

import { PageHeader, DataCard } from "@/components/cron/PageHeader";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { PromiseDetailSheet } from "@/components/promises/PromiseDetailSheet";
import { PromiseTable } from "@/components/promises/PromiseTable";
import { KpiSkeleton, PromiseKpis } from "@/components/promises/PromiseCharts";
import { PromiseTrendsCard } from "@/components/promises/PromiseTrends";
import { NowProvider } from "@/components/promises/RelativeTime";
import { usePromises } from "@/services/hooks";
import { useTimezone } from "@/lib/timezone";

function useFormatTime() {
  const { timezone } = useTimezone();
  return (value: string | null) => value
    ? new Intl.DateTimeFormat(undefined, { timeZone: timezone, dateStyle: "medium", timeStyle: "short" }).format(new Date(value))
    : "Not yet";
}

const EXAMPLE = `services.MapTicker<ProcessOrderJob, ProcessOrderRequest>()
    .Promise((ShopDb db) => db.Orders.Where(o => o.Status == OrderStatus.Paid), p => p
        .Within(TimeSpan.FromMinutes(5), after: o => o.PaidAt)
        .Job(o => new() { OrderId = o.Number }, matchBy: r => r.OrderId)
        .DoneWhen(o => o.Status == OrderStatus.Processed));`;

export default function PromisesPage() {
  const query = usePromises();
  const formatTime = useFormatTime();
  const promises = query.data ?? [];
  // Kept by id so the open panel follows each refresh of the list.
  const [openId, setOpenId] = useState<string | null>(null);
  const openPromise = promises.find(p => p.id === openId) ?? null;
  const loading = query.isPending;

  return <NowProvider><div className="mx-auto max-w-7xl space-y-4">
    <PageHeader title="Promises" count={query.data ? promises.length : undefined}
      description="Rows your jobs must handle, checked against what actually ran. Fix or dismiss the ones that were missed." />

    {query.isError && <QueryErrorNotice message="Promises could not be loaded." onRetry={() => void query.refetch()} />}

    {loading && <>
      <KpiSkeleton />
      <PromiseTable promises={[]} loading formatTime={formatTime} onOpen={() => undefined} />
    </>}

    {query.isSuccess && promises.length === 0 && <EmptyState />}

    {promises.length > 0 && <>
      <PromiseKpis promises={promises} />
      <PromiseTrendsCard promises={promises} />
      <PromiseTable promises={promises} formatTime={formatTime} onOpen={p => setOpenId(p.id)} />
    </>}

    <PromiseDetailSheet promise={openPromise} formatTime={formatTime} onClose={() => setOpenId(null)} />
  </div></NowProvider>;
}

function EmptyState() {
  return <DataCard className="p-10 text-center">
    <Handshake className="mx-auto size-7 text-muted-foreground" aria-hidden />
    <p className="mt-3 text-sm font-medium">No promises yet</p>
    <p className="mx-auto mt-1 max-w-xl text-xs text-muted-foreground">
      Developers add them in code: <code className="font-mono">services.MapTicker&lt;ProcessOrderJob, ProcessOrderRequest&gt;().Promise(...)</code>.
      Each one says which rows a job must handle, and by when.
    </p>
    <pre className="mx-auto mt-4 max-w-2xl overflow-x-auto rounded-md bg-muted px-4 py-3 text-left font-mono text-[11px] leading-relaxed">{EXAMPLE}</pre>
  </DataCard>;
}
