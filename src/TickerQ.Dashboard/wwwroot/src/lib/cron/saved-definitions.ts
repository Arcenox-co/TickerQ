import type { SavedTickerDefinition } from "@/services/api-types";

export type DefinitionCreatedOrder = "newest" | "oldest";

export function normalizeSavedDefinitionName(value: string): string {
  return value.trim().replace(/\s+/g, " ").normalize("NFKC").toUpperCase();
}

export function findDuplicateTemplateName(
  definitions: SavedTickerDefinition[],
  name: string,
  excludedId?: string,
): SavedTickerDefinition | undefined {
  const normalized = normalizeSavedDefinitionName(name);
  if (!normalized) return undefined;
  return definitions.find(definition =>
    definition.kind === "Template" &&
    definition.id !== excludedId &&
    normalizeSavedDefinitionName(definition.name) === normalized,
  );
}

export function filterAndSortSavedDefinitions(
  definitions: SavedTickerDefinition[],
  query: string,
  order: DefinitionCreatedOrder,
): SavedTickerDefinition[] {
  const needle = query.trim().toLowerCase();
  return definitions
    .filter(definition => !needle || [
      definition.name,
      definition.description ?? "",
      definition.payload.root.function,
    ].some(value => value.toLowerCase().includes(needle)))
    .sort((left, right) => {
      const difference = new Date(left.createdAtUtc).getTime() - new Date(right.createdAtUtc).getTime();
      return (order === "newest" ? -difference : difference) || left.id.localeCompare(right.id);
    });
}
