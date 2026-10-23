import type { SelectOption } from "@/components/form-fields/select-field";

// Labels stay short because the field label carries the stale-node context.
export const ON_STALE_OPTIONS: SelectOption[] = [
  { value: "Restart", label: "Restart" },
  { value: "Cancel", label: "Cancel" },
];

export const MISSED_EXECUTION_OPTIONS: SelectOption[] = [
  { value: "RunOnce", label: "Run when available" },
  { value: "Skip", label: "Skip after grace" },
];

export const CRON_CATCH_UP_OPTIONS: SelectOption[] = [
  { value: "Skip", label: "Skip missed" },
  { value: "RunLatest", label: "Run latest" },
];
