import { useNavigate, useParams, useSearchParams } from "react-router-dom";
import { ChainFlowchartView } from "@/components/cron/ChainFlowchartView";

/**
 * Route page wrapping the chain flowchart. Reached from the Time Tickers list's
 * "Chain" badge (and the Executions list's chain badge with `?from=executions`).
 * Layout breaks out of the Shell's main padding so the flowchart fills the
 * available viewport like the Hub.
 */
export default function ChainFlowchartPage() {
  const { id = "" } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const from = searchParams.get("from");
  const cronId = searchParams.get("cron");
  const occurrenceId = searchParams.get("occurrence");
  const navigate = useNavigate();

  return (
    <div className="h-[calc(100dvh-44px)] -m-4 sm:-m-6">
      <ChainFlowchartView
        rootId={id}
        from={cronId ? "cron-tickers" : from === "executions" ? "executions" : "time-tickers"}
        onBack={() =>
          navigate(cronId
            ? `/cron-tickers/${cronId}${occurrenceId ? `?${new URLSearchParams({ occurrence: occurrenceId })}` : ""}`
            : from === "executions" ? "/executions" : "/time-tickers")
        }
        onEditChain={(rid) => navigate(`/time-tickers/new-chain?edit=${rid}`)}
      />
    </div>
  );
}
