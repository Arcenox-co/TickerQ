const EXECUTION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

/** Build the reviewable prompt used by an exception's “Investigate with AI” action. */
export function investigationPrompt(value: string | null | undefined): string {
  const executionId = value?.trim() ?? "";
  if (!EXECUTION_ID.test(executionId)) return "";
  return `Investigate execution ${executionId}. Use the run investigator first, separate confirmed evidence from likely causes, and recommend the safest next action.`;
}

/** Groups an already ordered investigation list without changing its order. */
export function groupInvestigationsByFunction<T extends { functionName: string }>(
  items: readonly T[]
): T[][] {
  const groups = new Map<string, T[]>();
  for (const item of items) {
    const current = groups.get(item.functionName);
    if (current) current.push(item);
    else groups.set(item.functionName, [item]);
  }
  return [...groups.values()];
}

/** Count investigations in a group that have not been opened by an operator yet. */
export function unreadInvestigationCount<T extends { reviewState: string }>(
  items: readonly T[]
): number {
  return items.reduce((count, item) => count + (item.reviewState === "New" ? 1 : 0), 0);
}

/** Select the execution that contains useful failure evidence for an investigation.
 * Chain roots can finish successfully while a descendant fails, so callers must
 * inspect the persisted chain rather than using only the aggregate/root status. */
export function investigationExecutionId<T extends { id: string; status: string; functionName?: string }>(
  executions: readonly T[],
): string | null {
  const executable = executions.filter((item) => item.functionName !== "$tickerq.parallel-group");
  return executable.find((item) => item.status === "Failed")?.id
    ?? executable.find((item) => item.status === "Cancelled")?.id
    ?? null;
}
