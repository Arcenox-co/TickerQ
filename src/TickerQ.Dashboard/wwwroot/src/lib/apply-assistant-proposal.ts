import type { AssistantProposal, ProposalChainNode } from "../services/assistant-api.ts";
import type { dashboardApi } from "../services/dashboard-api.ts";
import { encodeRequestPayload } from "./request-payload.ts";
import { proposalChainToTimeTickerNode, proposalChainToCronTemplate } from "./assistant-proposal-payload.ts";

export type AssistantActionApi = Pick<typeof dashboardApi,
  "addTimeTicker" | "addCronTicker" | "addTimeTickerChain" | "updateCronTicker" |
  "getTimeTicker" | "getCronOccurrence" | "getCronTicker" | "runTimeTicker" | "rerunCronOccurrence" |
  "cancelTicker" | "deleteTimeTicker" | "deleteCronTicker" | "deleteCronOccurrence" |
  "getIntegrations" | "saveIntegrations">;

const sameTime = (a?: string | null, b?: string | null) =>
  !a && !b || !!a && !!b && Date.parse(a) === Date.parse(b);

/** Called only by an explicit Confirm click, never by the chat stream. */
export async function applyAssistantProposal(p: AssistantProposal, api: AssistantActionApi): Promise<string> {
  switch (p.kind) {
    case "createTimeTicker":
      await api.addTimeTicker({ function: p.function, executionTime: p.executionTime || null,
        description: p.description || null, retries: p.retries, request: encodeRequestPayload(p.requestJson) });
      return "Time ticker created";
    case "createCronTicker":
      await api.addCronTicker({ function: p.function, expression: p.expression, description: p.description || null,
        retries: p.retries, request: encodeRequestPayload(p.requestJson), isEnabled: p.isEnabled ?? true });
      return "Cron ticker created";
    case "createChain":
    case "createCronChain": {
      const root = JSON.parse(p.chainJson) as ProposalChainNode;
      if (!root?.function) throw new Error("The proposed chain is invalid. Request a new proposal.");
      if (p.kind === "createChain") {
        await api.addTimeTickerChain({ executionTime: p.executionTime, root: proposalChainToTimeTickerNode(root, true) });
      } else {
        await api.addCronTicker({ function: root.function, expression: p.expression, retries: root.retries ?? 0,
          description: root.description || null, request: encodeRequestPayload(root.requestJson),
          isEnabled: p.isEnabled ?? true, skipIfAlreadyRunning: p.skipIfAlreadyRunning ?? false,
          chain: proposalChainToCronTemplate(root) });
      }
      return p.kind === "createChain" ? "Chain created" : "Recurring chain created";
    }
    case "updateCronTicker": {
      if (!p.targetId) throw new Error("The proposal has no target.");
      const current = await api.getCronTicker(p.targetId);
      if (current.expression !== p.currentExpression || current.functionName !== p.function)
        throw new Error("The cron definition changed. Request a new proposal before updating it.");
      // PATCH only requested fields; never reset retry intervals, payload, chain, overlap or timeout settings.
      await api.updateCronTicker(p.targetId, { expression: p.expression === p.currentExpression ? undefined : p.expression,
        description: p.description || undefined, retries: p.retries ?? undefined, isEnabled: p.isEnabled ?? undefined });
      return "Cron ticker updated";
    }
    case "retryExecution":
    case "cancelExecution":
    case "deleteExecution": {
      if (!p.targetId) throw new Error("The proposal has no target.");
      if (p.targetType === "CronTicker") {
        if (p.kind !== "deleteExecution") throw new Error("Unsupported cron definition action.");
        const current = await api.getCronTicker(p.targetId);
        if (current.functionName !== p.function || current.expression !== p.expression)
          throw new Error("The cron definition changed. Request a new proposal.");
        await api.deleteCronTicker(p.targetId);
        return "Cron ticker deleted";
      }
      if (p.targetType !== "TimeTicker" && p.targetType !== "CronOccurrence") throw new Error("Invalid execution type.");
      const current = p.targetType === "TimeTicker" ? await api.getTimeTicker(p.targetId) : await api.getCronOccurrence(p.targetId);
      if (current.functionName !== p.function || current.status !== p.expectedStatus ||
          !sameTime(current.executedAt, p.expectedExecutedAt) || !sameTime(current.lockedAt, p.expectedLockedAt))
        throw new Error("This execution changed since the proposal. Check its current status and request a new proposal.");
      if (p.kind === "retryExecution") {
        if (!["Failed", "Cancelled", "Skipped"].includes(current.status)) throw new Error("Only failed, cancelled or skipped executions can be retried.");
        if (p.targetType === "TimeTicker") await api.runTimeTicker(p.targetId);
        else {
          const cronId = "cronTickerId" in current ? current.cronTickerId : null;
          if (!cronId || cronId !== p.cronTickerId) throw new Error("The source cron definition changed.");
          await api.rerunCronOccurrence(p.targetId);
        }
        return "Run requested";
      }
      if (p.kind === "cancelExecution") {
        if (!["Idle", "Queued", "InProgress"].includes(current.status)) throw new Error("This execution can no longer be cancelled.");
        const result = await api.cancelTicker({ id: p.targetId, type: p.targetType, running: current.status === "InProgress" });
        if (result.affected < 1) throw new Error("No execution was cancelled. Refresh its status.");
        return result.outcome === "Requested" ? "Cancellation requested — waiting for the handler" : "Pending execution cancelled";
      }
      if (["InProgress", "Queued"].includes(current.status)) throw new Error("Wait for the active execution to stop before deleting it.");
      if (p.targetType === "TimeTicker") await api.deleteTimeTicker(p.targetId);
      else await api.deleteCronOccurrence(p.targetId);
      return "Execution deleted";
    }
    case "createAlertRule":
    case "createMonitor": {
      const snapshot = await api.getIntegrations();
      if (!snapshot.canManage) throw new Error("You do not have permission to manage integrations.");
      if (!p.configurationVersion || snapshot.configuration.version !== p.configurationVersion)
        throw new Error("Integration settings changed. Request a fresh proposal before saving.");
      const config = snapshot.configuration;
      if (p.kind === "createAlertRule") {
        if (!p.rule) throw new Error("Missing alert rule.");
        await api.saveIntegrations({ ...config, rules: [...config.rules, p.rule] });
      } else {
        if (!p.monitor || !snapshot.monitoringAvailable) throw new Error("Monitoring is unavailable or the monitor is missing.");
        await api.saveIntegrations({ ...config, monitors: [...(config.monitors ?? []), p.monitor] });
      }
      return p.kind === "createAlertRule" ? "Alert rule created" : "Monitor created";
    }
    default: throw new Error("Unsupported proposal. Request a new one.");
  }
}
