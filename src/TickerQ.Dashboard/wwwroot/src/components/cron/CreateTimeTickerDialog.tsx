import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Plus, RotateCcw } from "lucide-react";
import { toast } from "sonner";

import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Form } from "@/components/ui/form";
import { TextField } from "@/components/form-fields/text-field";
import { DateTimeField } from "@/components/form-fields/datetime-field";
import { TextareaField } from "@/components/form-fields/textarea-field";
import { NumberField } from "@/components/form-fields/number-field";
import {
  SelectField,
  type SelectOption,
} from "@/components/form-fields/select-field";
import {
  describeRetryPolicy,
  parseIntervalsList,
} from "@/lib/cron/status-config";

const schema = z.object({
  function: z.string().trim().min(1, "Function is required"),
  executionTime: z.string().trim().optional(),
  description: z.string().max(2000, "Max 2000 characters").optional(),
  retries: z.number().int().min(0).max(100).optional(),
  retryIntervalsSeconds: z
    .string()
    .optional()
    .refine((v) => v == null || parseIntervalsList(v) !== null, {
      message:
        "Use comma-separated whole numbers between 1 and 3600 (e.g. 5, 30, 60)",
    }),
  requestJson: z.string().optional(),
  onStale: z.enum(["Restart", "Cancel"]),
  timeoutSeconds: z.number().int().min(0).max(86400).optional(),
});

// Labels stay short: the trigger renders the full option content, and these sit
// in a half-width column next to the timeout field ("If the node dies mid-run"
// label carries the context; Restart = re-run on another node, Cancel = never twice).
export const ON_STALE_OPTIONS: SelectOption[] = [
  { value: "Restart", label: "Restart" },
  { value: "Cancel", label: "Cancel" },
];

type FormValues = z.infer<typeof schema>;

export function CreateTimeTickerDialog({
  open,
  onOpenChange,
  functionOptions = [],
  onSubmit,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  functionOptions?: SelectOption[];
  onSubmit?: (values: FormValues) => Promise<void> | void;
}) {
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      function: functionOptions[0]?.value ?? "",
      executionTime: "",
      description: "",
      retries: 0,
      retryIntervalsSeconds: "30",
      requestJson: "",
      onStale: "Restart",
      timeoutSeconds: 0,
    },
  });

  const retriesValue = form.watch("retries") ?? 0;
  const intervalsValue = form.watch("retryIntervalsSeconds") ?? "";

  async function handleSubmit(values: FormValues) {
    try {
      await onSubmit?.(values);
      toast.success("Time ticker scheduled");
      form.reset();
      onOpenChange(false);
    } catch (err) {
      toast.error("Failed to create", {
        description: err instanceof Error ? err.message : "An error occurred",
      });
    }
  }

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        className="w-[520px] sm:max-w-[520px] bg-surface-0 border-l border-border overflow-y-auto scrollbar-thin p-0 gap-0"
      >
        <div className="px-6 py-5 border-b border-border">
          <SheetTitle className="text-base font-semibold">
            Schedule Time Ticker
          </SheetTitle>
          <SheetDescription className="mt-0.5 text-[12px] text-muted-foreground">
            Queue a one-time job on the selected environment.
          </SheetDescription>
        </div>

        <div className="p-6">
          <Form {...form}>
            <form
              className="flex flex-col"
              onSubmit={form.handleSubmit(handleSubmit)}
            >
              <div className="space-y-4">
                {functionOptions.length > 0 ? (
                  <SelectField
                    control={form.control}
                    name="function"
                    label="Function"
                    required
                    options={functionOptions}
                    placeholder="Select a function"
                  />
                ) : (
                  <TextField
                    control={form.control}
                    name="function"
                    label="Function"
                    required
                    placeholder="MyFunctionName"
                  />
                )}

                <DateTimeField
                  control={form.control}
                  name="executionTime"
                  label="Execution time"
                  step={1}
                  description="Empty fires at submit time"
                />

                <TextField
                  control={form.control}
                  name="description"
                  label="Description"
                  placeholder="Optional"
                  maxLength={2000}
                />

                <div className="rounded-lg border border-border/50 bg-surface-0/30 p-3 space-y-2.5">
                  <div className="flex items-center gap-1.5 text-[11px] text-muted-foreground uppercase tracking-wider">
                    <RotateCcw className="h-3 w-3" />
                    Retry policy
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <NumberField
                      control={form.control}
                      name="retries"
                      label="Retries"
                      min={0}
                      max={100}
                    />
                    <TextField
                      control={form.control}
                      name="retryIntervalsSeconds"
                      label="Intervals (sec)"
                      placeholder={retriesValue > 0 ? "5, 30, 60" : "—"}
                      disabled={retriesValue <= 0}
                    />
                  </div>
                  <p className="text-[11px] text-muted-foreground/70 italic">
                    →{" "}
                    {describeRetryPolicy(
                      retriesValue,
                      parseIntervalsList(intervalsValue)
                    )}
                  </p>
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <SelectField
                    control={form.control}
                    name="onStale"
                    label="If the node dies mid-run"
                    options={ON_STALE_OPTIONS}
                  />
                  <NumberField
                    control={form.control}
                    name="timeoutSeconds"
                    label="Timeout (sec, 0 = none)"
                    min={0}
                    max={86400}
                  />
                </div>

                <TextareaField
                  control={form.control}
                  name="requestJson"
                  label="Request payload (optional)"
                  description="JSON, plain text, or any string the function expects."
                  placeholder='{"key":"value"}'
                  rows={4}
                />
              </div>

              <footer className="mt-6 flex items-center justify-end gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-8 text-xs rounded-lg"
                  onClick={() => onOpenChange(false)}
                >
                  Cancel
                </Button>
                <Button
                  type="submit"
                  variant="gradient"
                  size="sm"
                  className="h-8 text-xs rounded-lg"
                  disabled={form.formState.isSubmitting}
                >
                  <Plus className="h-3 w-3 mr-1.5" />
                  {form.formState.isSubmitting ? "Creating…" : "Create"}
                </Button>
              </footer>
            </form>
          </Form>
        </div>
      </SheetContent>
    </Sheet>
  );
}
