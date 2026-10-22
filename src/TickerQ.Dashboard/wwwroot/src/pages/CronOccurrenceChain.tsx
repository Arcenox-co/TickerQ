import { useEffect, useMemo } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router-dom";
import { ArrowLeft, Loader2 } from "lucide-react";
import { ChainFlowchartView } from "@/components/cron/ChainFlowchartView";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { cronOccurrenceChainPreview } from "@/lib/cron/cron-chain";
import { useCronOccurrence, useCronTicker } from "@/services/hooks";
import { joinCronGroup, leaveCronGroup } from "@/services/ticker-hub";

export default function CronOccurrenceChainPage() {
  const { id = "", occurrenceId = "" } = useParams<{ id: string; occurrenceId: string }>();
  return <CronOccurrenceChain key={`${id}/${occurrenceId}`} cronId={id} occurrenceId={occurrenceId} />;
}

function CronOccurrenceChain({ cronId, occurrenceId }: { cronId: string; occurrenceId: string }) {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const occurrenceQuery = useCronOccurrence(occurrenceId);
  const occurrence = occurrenceQuery.data;
  // Once launched, only the persisted chain describes this run, even if its template changes.
  const cronQuery = useCronTicker(occurrence?.chainRootId ? null : cronId);
  const cron = cronQuery.data;
  const previewTickers = useMemo(() => cron && occurrence
    ? cronOccurrenceChainPreview(cron, occurrence) : [], [cron, occurrence]);
  const onBack = () => navigate(`/cron-tickers/${cronId}${searchParams.get("details") === "1"
    ? `?${new URLSearchParams({ occurrence: occurrenceId })}` : ""}`);

  useEffect(() => {
    void joinCronGroup(cronId);
    return () => { void leaveCronGroup(cronId); };
  }, [cronId]);

  const validOccurrence = occurrence?.cronTickerId === cronId;
  const loading = occurrenceQuery.isLoading || (!occurrence?.chainRootId && cronQuery.isLoading);
  const error = occurrenceQuery.isError || (!occurrence?.chainRootId && cronQuery.isError);
  const hasChain = validOccurrence && (!!occurrence?.chainRootId || previewTickers.length > 0);

  return (
    <div className="h-[calc(100dvh-44px)] -m-4 sm:-m-6">
      {!loading && !error && hasChain ? (
        <ChainFlowchartView
          rootId={occurrence!.chainRootId ?? occurrenceId}
          from="cron-tickers"
          onBack={onBack}
          previewTickers={occurrence!.chainRootId ? undefined : previewTickers}
        />
      ) : (
        <div className="space-y-4 p-6">
          <button type="button" onClick={onBack} className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground">
            <ArrowLeft className="h-3 w-3" /> Back to occurrences
          </button>
          {loading ? <div className="flex items-center gap-2 text-xs text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" /> Loading chain…</div>
            : error ? <QueryErrorNotice message="Failed to load this occurrence’s chain." onRetry={() => {
              void occurrenceQuery.refetch();
              if (!occurrence?.chainRootId) void cronQuery.refetch();
            }} />
            : <p className="text-sm text-muted-foreground">{!validOccurrence
              ? "This occurrence is unavailable for this cron."
              : occurrence && ["Skipped", "Cancelled", "Failed"].includes(occurrence.status)
                ? `This occurrence was ${occurrence.status.toLowerCase()} before creating a chain.`
                : "No chain is available for this occurrence."}</p>}
        </div>
      )}
    </div>
  );
}
