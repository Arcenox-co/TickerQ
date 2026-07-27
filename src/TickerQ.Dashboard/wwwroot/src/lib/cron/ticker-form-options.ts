import type { SelectOption } from "@/components/form-fields/select-field";

// Labels stay short because the field label carries the stale-node context.
export const ON_STALE_OPTIONS: SelectOption[] = [
  { value: "Restart", label: "Restart" },
  { value: "Cancel", label: "Cancel" },
];
