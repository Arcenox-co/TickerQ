import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { RotateCcw, Save } from "lucide-react";
import { toast } from "sonner";

import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Form } from "@/components/ui/form";
import { Checkbox } from "@/components/ui/checkbox";
import { TextField } from "@/components/form-fields/text-field";
import { NumberField } from "@/components/form-fields/number-field";
import { SelectField } from "@/components/form-fields/select-field";
import { ON_STALE_OPTIONS } from "@/components/cron/CreateTimeTickerDialog";
import {
  describeRetryPolicy,
  parseIntervalsList,
} from "@/lib/cron/status-config";
import {
  CronExpressionPreview,
  isValidCronExpression,
} from "@/components/cron/CronExpressionPreview";
import type { CronTickerFlatDto } from "@/services/api-types";

const schema = z.object({
  expression: z
    .string()
    .trim()
    .min(1, "Expression is required")
    .refine(isValidCronExpression, "Invalid cron expression"),
  description: z.string().max(2000, "Max 2000 characters").optional(),
  retries: z.number().int().min(0).max(100).optional(),
  retryIntervalsSeconds: z
    .string()
    .optional()
    .refine((v) => v == null || parseIntervalsList(v) !== null, {
      message:
        "Use comma-separated whole numbers between 1 and 3600 (e.g. 5, 30, 60)",
    }),
  onStale: z.enum(["Restart", "Cancel"]),
  timeoutSeconds: z.number().int().min(0).max(86400).optional(),
});

export type EditCronFormValues = z.infer<typeof schema>;

export function EditCronTickerDialog({
  cron,
  open,
  onOpenChange,
  onSubmit,
}: {
  cron: CronTickerFlatDto | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit?: (values: EditCronFormValues & { isEnabled: boolean }) => Promise<void> | void;
}) {
  const form = useForm<EditCronFormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      expression: "",
      description: "",
      retries: 0,
      retryIntervalsSeconds: "",
      onStale: "Restart",
      timeoutSeconds: 0,
    },
  });
  const [enabled, setEnabled] = useState(true);

  // Re-prime the form whenever a different cron is opened for editing.
  useEffect(() => {
    if (cron) {
      form.reset({
        expression: cron.expression,
        description: cron.description ?? "",
        retries: cron.retries ?? 0,
        retryIntervalsSeconds: cron.retryIntervalsSeconds?.join(", ") ?? "",
        onStale: cron.onStale ?? "Restart",
        timeoutSeconds: cron.timeoutSeconds ?? 0,
      });
      setEnabled(cron.isEnabled);
    }
  }, [cron, form]);

  const retriesValue = form.watch("retries") ?? 0;
  const intervalsValue = form.watch("retryIntervalsSeconds") ?? "";
  const expressionValue = form.watch("expression") ?? "";

  async function handleSubmit(values: EditCronFormValues) {
    try {
      await onSubmit?.({ ...values, isEnabled: enabled });
      toast.success("Cron ticker updated");
      onOpenChange(false);
    } catch (err) {
      toast.error("Failed to update", {
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
            Edit Cron Ticker
          </SheetTitle>
          <SheetDescription className="mt-0.5 text-[12px] text-muted-foreground">
            {cron ? (
              <span className="font-mono text-foreground">{cron.functionName}</span>
            ) : null}
          </SheetDescription>
        </div>

        <div className="p-6">
          <Form {...form}>
            <form className="flex flex-col" onSubmit={form.handleSubmit(handleSubmit)}>
              <div className="space-y-4">
                <div className="space-y-1.5">
                  <span className="text-[11px] font-medium text-foreground">
                    Cron expression <span className="text-status-error">*</span>
                  </span>
                  <TextField
                    control={form.control}
                    name="expression"
                    label=""
                    placeholder="0 * * * *"
                  />
                  <CronExpressionPreview expression={expressionValue} />
                </div>

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

                <label className="flex items-center gap-2 text-[12px] text-muted-foreground cursor-pointer select-none">
                  <Checkbox
                    checked={enabled}
                    onCheckedChange={(c) => setEnabled(c === true)}
                    className="h-3.5 w-3.5"
                  />
                  Enabled
                </label>
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
                  <Save className="h-3 w-3 mr-1.5" />
                  {form.formState.isSubmitting ? "Saving…" : "Save changes"}
                </Button>
              </footer>
            </form>
          </Form>
        </div>
      </SheetContent>
    </Sheet>
  );
}
