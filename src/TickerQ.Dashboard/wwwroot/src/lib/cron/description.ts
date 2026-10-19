import { CronExpressionParser } from "cron-parser";
import cronstrue from "cronstrue";

export function describeCronExpression(expression: string): string | null {
  const trimmed = expression?.trim();
  if (!trimmed) return null;
  const normalized = trimmed.split(/\s+/).length === 5 ? `0 ${trimmed}` : trimmed;
  try {
    CronExpressionParser.parse(normalized);
    return cronstrue.toString(normalized, { use24HourTimeFormat: true });
  } catch {
    return null;
  }
}
