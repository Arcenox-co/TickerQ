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
  const navigate = useNavigate();

  return (
    <div className="h-[calc(100vh-44px-24px-24px)] -m-6">
      <ChainFlowchartView
        rootId={id}
        from={from === "executions" ? "executions" : "time-tickers"}
        onBack={() =>
          navigate(from === "executions" ? "/executions" : "/time-tickers")
        }
        onEditChain={(rid) => navigate(`/time-tickers/new-chain?edit=${rid}`)}
      />
    </div>
  );
}
