import { useEffect, useRef, useState } from "react";
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
import { Checkbox } from "@/components/ui/checkbox";
import { TextField } from "@/components/form-fields/text-field";

import { NumberField } from "@/components/form-fields/number-field";
import {
  SelectField,
  type SelectOption,
} from "@/components/form-fields/select-field";
import {
  describeRetryPolicy,
  parseIntervalsList,
} from "@/lib/cron/status-config";
import {
  CronExpressionPreview,
  isValidCronExpression,
} from "@/components/cron/CronExpressionPreview";
import { ON_STALE_OPTIONS } from "@/lib/cron/ticker-form-options";
import type { FunctionInfoDto } from "@/services/api-types";
import {
  initialPayloadForFunction,
  RequestPayloadEditor,
  validateRequestPayload,
} from "@/components/cron/RequestPayloadEditor";
import { createDraftState, selectFunction } from "@/lib/function-draft";

const schema = z.object({
  function: z.string().trim().min(1, "Function is required"),
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
  requestJson: z.string().optional(),
  onStale: z.enum(["Restart", "Cancel"]),
  timeoutSeconds: z.number().int().min(0).max(86400).optional(),
});

type FormValues = z.infer<typeof schema>;

const PRESETS: { label: string; value: string }[] = [
  { label: "Every minute", value: "* * * * *" },
  { label: "Every 5 minutes", value: "*/5 * * * *" },
  { label: "Every hour", value: "0 * * * *" },
  { label: "Every day at midnight", value: "0 0 * * *" },
  { label: "Every Monday 9am", value: "0 9 * * 1" },
];

export function CreateCronTickerDialog({
  open,
  onOpenChange,
  functionOptions = [],
  functions = [],
  onSubmit,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  functionOptions?: SelectOption[];
  functions?: FunctionInfoDto[];
  onSubmit?: (
    values: FormValues & { isEnabled: boolean }
  ) => Promise<void> | void;
}) {
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      function: functionOptions[0]?.value ?? "",
      expression: "0 * * * *",
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
  const expressionValue = form.watch("expression") ?? "";
  const selectedFunctionName = form.watch("function");
  const requestJson = form.watch("requestJson") ?? "";
  const selectedFunction = functions.find((item) => item.functionName === selectedFunctionName);
  const [enabled, setEnabled] = useState(true);
  const draftBook = useRef(createDraftState());
  const requestEditorValid = useRef(true);

  useEffect(() => {
    const selection = selectFunction(
      draftBook.current,
      selectedFunctionName ?? "",
      form.getValues("requestJson") ?? "",
      (name) => initialPayloadForFunction(functions.find((item) => item.functionName === name)),
    );
    draftBook.current = selection.state;
    if (selection.changed) {
      form.setValue("requestJson", selection.value);
      form.clearErrors("requestJson");
      requestEditorValid.current = true;
    }
  }, [form, functions, selectedFunctionName]);

  function resetDraftSession() {
    draftBook.current = createDraftState();
    requestEditorValid.current = true;
  }

  function handleOpenChange(nextOpen: boolean) {
    if (!nextOpen) {
      resetDraftSession();
      form.reset();
      setEnabled(true);
    }
    onOpenChange(nextOpen);
  }

  async function handleSubmit(values: FormValues) {
    if (!requestEditorValid.current) {
      form.setError("requestJson", { type: "validate", message: "Complete or correct the structured JSON field." });
      return;
    }
    const requestError = validateRequestPayload(selectedFunction, values.requestJson ?? "");
    if (requestError) {
      form.setError("requestJson", { type: "validate", message: requestError });
      return;
    }
    try {
      await onSubmit?.({ ...values, isEnabled: enabled });
      toast.success("Cron ticker created");
      form.reset();
      handleOpenChange(false);
    } catch (err) {
      toast.error("Failed to create", {
        description: err instanceof Error ? err.message : "An error occurred",
      });
    }
  }

  return (
    <Sheet open={open} onOpenChange={handleOpenChange}>
      <SheetContent
        side="right"
        className="w-[520px] sm:max-w-[520px] bg-surface-0 border-l border-border overflow-y-auto scrollbar-thin p-0 gap-0"
      >
        <div className="px-6 py-5 border-b border-border">
          <SheetTitle className="text-base font-semibold">
            Schedule Cron Ticker
          </SheetTitle>
          <SheetDescription className="mt-0.5 text-[12px] text-muted-foreground">
            Recurring job that fires whenever the cron expression matches.
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

                <div className="space-y-1.5">
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-[11px] font-medium text-foreground">
                      Cron expression{" "}
                      <span className="text-status-error">*</span>
                    </span>
                    <select
                      value=""
                      onChange={(e) => {
                        if (e.target.value) {
                          form.setValue("expression", e.target.value, {
                            shouldValidate: true,
                          });
                        }
                      }}
                      className="h-6 rounded-md border border-border bg-card px-2 text-[11px] text-muted-foreground hover:text-foreground"
                    >
                      <option value="">Presets…</option>
                      {PRESETS.map((p) => (
                        <option key={p.value} value={p.value}>
                          {p.label}
                        </option>
                      ))}
                    </select>
                  </div>
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

                <RequestPayloadEditor
                  key={selectedFunctionName}
                  functionInfo={selectedFunction}
                  value={requestJson}
                  onChange={(value) => form.setValue("requestJson", value, { shouldDirty: true })}
                  onValidityChange={(valid) => { requestEditorValid.current = valid; }}
                  error={form.formState.errors.requestJson?.message}
                />

                <label className="flex items-center gap-2 text-[12px] text-muted-foreground cursor-pointer select-none">
                  <Checkbox
                    checked={enabled}
                    onCheckedChange={(c) => setEnabled(c === true)}
                    className="h-3.5 w-3.5"
                  />
                  Enabled on create
                </label>
              </div>

              <footer className="mt-6 flex items-center justify-end gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-8 text-xs rounded-lg"
                  onClick={() => handleOpenChange(false)}
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
