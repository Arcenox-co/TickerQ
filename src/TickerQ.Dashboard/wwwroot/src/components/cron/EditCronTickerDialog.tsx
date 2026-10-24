import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery } from "@tanstack/react-query";
import { z } from "zod";
import { Save } from "lucide-react";
import { toast } from "sonner";

import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Form } from "@/components/ui/form";
import { AdvancedSettings, AdvancedSettingsGroup } from "@/components/cron/AdvancedSettings";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { CronExpressionPreview, isValidCronExpression } from "@/components/cron/CronExpressionPreview";
import { CronOverlapField } from "@/components/cron/CronOverlapField";
import { CronSchedulePresets } from "@/components/cron/CronSchedulePresets";
import { DurationField } from "@/components/form-fields/duration-field";
import { COMPACT_FIELD_LABEL_CLASS } from "@/components/form-fields/field-label";
import { FieldInfo } from "@/components/form-fields/field-info";
import { NumberField } from "@/components/form-fields/number-field";
import { SelectField } from "@/components/form-fields/select-field";
import { TextareaField } from "@/components/form-fields/textarea-field";
import { TextField } from "@/components/form-fields/text-field";
import { TimezoneField } from "@/components/form-fields/timezone-field";
import {
  initialPayloadForFunction,
  RequestPayloadEditor,
  validateRequestPayload,
} from "@/components/cron/RequestPayloadEditor";
import {
  CRON_CATCH_UP_OPTIONS,
  MISSED_EXECUTION_OPTIONS,
  ON_STALE_OPTIONS,
} from "@/lib/cron/ticker-form-options";
import { describeRetryPolicy, parseIntervalsList } from "@/lib/cron/status-config";
import { createDraftState, selectFunction } from "@/lib/function-draft";
import { decodeRequestPayload } from "@/lib/request-payload";
import { dashboardApi } from "@/services/dashboard-api";
import { useAllFunctions, useSchedulePolicies } from "@/services/hooks";
import type { CronTickerFlatDto } from "@/services/api-types";

const schema = z.object({
  skipIfAlreadyRunning: z.boolean(),
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
  timeZoneId: z.string().trim().min(1, "Time zone is required"),
  schedulePolicyName: z.string().optional(),
  missedExecutionAction: z.enum(["RunOnce", "Skip"]),
  maximumLatenessSeconds: z.number().int().min(0).max(31_536_000).optional(),
  catchUpMode: z.enum(["Skip", "RunLatest"]),
}).superRefine((values, context) => {
  if (values.missedExecutionAction === "Skip" && values.maximumLatenessSeconds == null)
    context.addIssue({ code: "custom", path: ["maximumLatenessSeconds"], message: "Set the allowed lateness." });
  if (values.catchUpMode === "RunLatest" && values.missedExecutionAction !== "Skip")
    context.addIssue({ code: "custom", path: ["catchUpMode"], message: "Run latest requires a bounded grace period." });
});

export type EditCronFormValues = z.infer<typeof schema>;

function formValuesFor(cron: CronTickerFlatDto, requestJson = ""): EditCronFormValues {
  return {
    function: cron.functionName,
    expression: cron.expression,
    description: cron.description ?? "",
    retries: cron.retries ?? 0,
    retryIntervalsSeconds: cron.retryIntervalsSeconds?.join(", ") ?? "",
    requestJson,
    onStale: cron.onStale ?? "Restart",
    timeoutSeconds: cron.timeoutSeconds ?? 0,
    skipIfAlreadyRunning: cron.skipIfAlreadyRunning ?? false,
    timeZoneId: cron.timeZoneId ?? "UTC",
    schedulePolicyName: cron.schedulePolicyName ?? "",
    missedExecutionAction: cron.missedExecutionAction ?? "RunOnce",
    maximumLatenessSeconds: cron.maximumLatenessSeconds ?? 300,
    catchUpMode: cron.catchUpMode ?? "Skip",
  };
}

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
      function: "",
      expression: "",
      description: "",
      retries: 0,
      retryIntervalsSeconds: "",
      requestJson: "",
      onStale: "Restart",
      timeoutSeconds: 0,
      skipIfAlreadyRunning: false,
      timeZoneId: "UTC",
      schedulePolicyName: "",
      missedExecutionAction: "RunOnce",
      maximumLatenessSeconds: 300,
      catchUpMode: "Skip",
    },
  });
  const schedulePolicies = useSchedulePolicies();
  const functionsQuery = useAllFunctions();
  const requestQuery = useQuery({
    queryKey: ["cron-tickers", cron?.id ?? "none", "request"],
    queryFn: ({ signal }) => dashboardApi.getCronTickerRequest(cron!.id, signal),
    enabled: open && !!cron,
  });
  const [enabled, setEnabled] = useState(true);
  const [leavePromptOpen, setLeavePromptOpen] = useState(false);
  const draftBook = useRef(createDraftState());
  const requestEditorValid = useRef(true);

  useEffect(() => {
    if (cron) {
      form.reset(formValuesFor(cron));
      setEnabled(cron.isEnabled);
      setLeavePromptOpen(false);
      draftBook.current = {
        active: cron.functionName,
        drafts: { [cron.functionName]: "" },
      };
      requestEditorValid.current = true;
    }
  }, [cron, form]);

  useEffect(() => {
    if (!cron || !requestQuery.isSuccess) return;
    if (form.getValues("function") !== cron.functionName || form.formState.dirtyFields.requestJson)
      return;

    const requestJson = decodeRequestPayload(requestQuery.data.payload);
    form.resetField("requestJson", { defaultValue: requestJson });
    draftBook.current = {
      active: cron.functionName,
      drafts: { [cron.functionName]: requestJson },
    };
    requestEditorValid.current = true;
  }, [cron, form, requestQuery.data, requestQuery.isSuccess]);

  const retriesValue = form.watch("retries") ?? 0;
  const intervalsValue = form.watch("retryIntervalsSeconds") ?? "";
  const expressionValue = form.watch("expression") ?? "";
  const selectedFunctionName = form.watch("function");
  const requestJson = form.watch("requestJson") ?? "";
  const missedExecutionAction = form.watch("missedExecutionAction");
  const isDirty = form.formState.isDirty || enabled !== (cron?.isEnabled ?? true);
  const functions = functionsQuery.data ?? [];
  const selectedFunction = functions.find(item => item.functionName === selectedFunctionName);
  const functionOptions = Array.from(new Set([
    ...(cron?.functionName ? [cron.functionName] : []),
    ...functions.map(item => item.functionName),
  ])).map(value => ({ value, label: value }));
  const editMetadataReady = requestQuery.isSuccess && functionsQuery.isSuccess;

  useEffect(() => {
    if (!selectedFunctionName) return;
    const selection = selectFunction(
      draftBook.current,
      selectedFunctionName,
      form.getValues("requestJson") ?? "",
      name => initialPayloadForFunction(functions.find(item => item.functionName === name)),
    );
    draftBook.current = selection.state;
    if (selection.changed) {
      form.setValue("requestJson", selection.value, { shouldDirty: true, shouldValidate: true });
      form.clearErrors("requestJson");
      requestEditorValid.current = true;
    }
  }, [form, functions, selectedFunctionName]);

  useEffect(() => {
    if (missedExecutionAction !== "Skip" && form.getValues("catchUpMode") !== "Skip")
      form.setValue("catchUpMode", "Skip", { shouldDirty: true, shouldValidate: true });
  }, [form, missedExecutionAction]);

  function closeAndReset() {
    if (cron) {
      form.reset(formValuesFor(cron, decodeRequestPayload(requestQuery.data?.payload)));
      setEnabled(cron.isEnabled);
    }
    draftBook.current = createDraftState();
    requestEditorValid.current = true;
    setLeavePromptOpen(false);
    onOpenChange(false);
  }

  function handleOpenChange(nextOpen: boolean) {
    if (nextOpen) {
      onOpenChange(true);
      return;
    }
    if (isDirty) {
      setLeavePromptOpen(true);
      return;
    }
    closeAndReset();
  }

  async function handleSubmit(values: EditCronFormValues) {
    if (!editMetadataReady) {
      toast.error("The function metadata and existing request payload must finish loading before saving.");
      return;
    }
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
      toast.success("Cron ticker updated");
      form.reset(values);
      closeAndReset();
    } catch (err) {
      toast.error("Failed to update", {
        description: err instanceof Error ? err.message : "An error occurred",
      });
    }
  }

  return (
    <>
      <Sheet open={open} onOpenChange={handleOpenChange}>
        <SheetContent
          side="right"
          className="flex flex-col gap-0 overflow-hidden border-l border-border bg-surface-0 p-0 data-[side=right]:w-full data-[side=right]:sm:max-w-[560px]"
        >
          <div className="shrink-0 border-b border-border bg-surface-0/95 px-5 py-5 backdrop-blur sm:px-6">
            <SheetTitle className="text-base font-semibold">
              Edit Cron Ticker
            </SheetTitle>
            <SheetDescription className="mt-0.5 text-[12px] text-muted-foreground">
              Update the recurring schedule and execution behavior.
            </SheetDescription>
          </div>

          <div className="min-h-0 flex-1 overflow-y-auto scrollbar-thin">
            <Form {...form}>
              <form
                className="flex min-h-full flex-col"
                onSubmit={form.handleSubmit(handleSubmit)}
              >
                <div className="flex-1 space-y-5 p-4 sm:p-6">
                  <div className="grid grid-cols-1 items-start gap-4 sm:grid-cols-2">
                    <SelectField
                      control={form.control}
                      name="function"
                      label="Function"
                      labelClassName={COMPACT_FIELD_LABEL_CLASS}
                      info="The registered TickerFunction invoked for future occurrences."
                      required
                      options={functionOptions}
                      placeholder="Select a function"
                    />

                    <div className="space-y-1.5">
                      <div className="flex items-center justify-between gap-2">
                        <span className={`flex shrink-0 items-center gap-1 whitespace-nowrap ${COMPACT_FIELD_LABEL_CLASS}`}>
                          Cron expression <span className="text-status-error">*</span>
                          <FieldInfo>Defines the recurring fire times interpreted in the selected time zone.</FieldInfo>
                        </span>
                        <CronSchedulePresets onSelect={(expression) =>
                          form.setValue("expression", expression, { shouldValidate: true, shouldDirty: true })
                        } />
                      </div>
                      <div className="pt-0.5">
                        <TextField
                          control={form.control}
                          name="expression"
                          label=""
                          placeholder="0 * * * *"
                        />
                      </div>
                      <CronExpressionPreview expression={expressionValue} />
                    </div>

                    <div className="sm:col-span-2">
                      <TextareaField
                        control={form.control}
                        name="description"
                        label="Description"
                        labelClassName={COMPACT_FIELD_LABEL_CLASS}
                        info="Optional context shown to operators with this recurring job."
                        placeholder="Add details that help operators understand this recurring job…"
                        maxLength={2000}
                        rows={3}
                        className="min-h-20 resize-y"
                      />
                    </div>
                  </div>

                  <TimezoneField
                    control={form.control}
                    name="timeZoneId"
                    label="Time zone"
                    labelClassName={COMPACT_FIELD_LABEL_CLASS}
                    info="The time zone used to interpret the cron expression."
                    required
                  />

                  <div>
                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                      <NumberField
                        control={form.control}
                        name="retries"
                        label="Retries"
                        labelClassName={COMPACT_FIELD_LABEL_CLASS}
                        info="Additional attempts after an occurrence reports a failure."
                        min={0}
                        max={100}
                      />
                      <TextField
                        control={form.control}
                        name="retryIntervalsSeconds"
                        label="Retry delays"
                        labelClassName={COMPACT_FIELD_LABEL_CLASS}
                        info="Comma-separated delays applied in order between retry attempts."
                        placeholder={retriesValue > 0 ? "5, 30, 60" : "—"}
                        disabled={retriesValue <= 0}
                      />
                    </div>
                    <p className="mt-2 text-[11px] italic text-muted-foreground/70">
                      → {describeRetryPolicy(retriesValue, parseIntervalsList(intervalsValue))}
                    </p>
                  </div>

                  {requestQuery.isPending || functionsQuery.isPending ? (
                    <div className="rounded-lg border border-border/50 bg-surface-0/30 p-3 text-[12px] text-muted-foreground">
                      Loading the existing request payload…
                    </div>
                  ) : requestQuery.isError || functionsQuery.isError ? (
                    <div className="flex items-center justify-between gap-3 rounded-lg border border-destructive/30 bg-destructive/5 p-3 text-[12px] text-destructive">
                      <span>Could not load the existing request payload.</span>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="h-7 text-xs"
                        onClick={() => {
                          void requestQuery.refetch();
                          void functionsQuery.refetch();
                        }}
                      >
                        Retry
                      </Button>
                    </div>
                  ) : (
                    <RequestPayloadEditor
                      key={selectedFunctionName}
                      functionInfo={selectedFunction}
                      value={requestJson}
                      onChange={(value) => form.setValue("requestJson", value, { shouldDirty: true })}
                      onValidityChange={(valid) => { requestEditorValid.current = valid; }}
                      error={form.formState.errors.requestJson?.message}
                    />
                  )}

                  <AdvancedSettings description="Schedule timing, execution controls, and recovery behavior.">
                    <AdvancedSettingsGroup title="Scheduling">
                      <label className="flex cursor-pointer select-none items-center gap-2 text-[12px] text-foreground">
                        <Checkbox
                          checked={enabled}
                          onCheckedChange={(checked) => setEnabled(checked === true)}
                          className="h-3.5 w-3.5"
                        />
                        Enabled
                        <span className="text-[11px] text-muted-foreground">
                          Produce occurrences from this schedule.
                        </span>
                      </label>
                      <SelectField
                        control={form.control}
                        name="schedulePolicyName"
                        label="Schedule policy"
                        labelClassName={COMPACT_FIELD_LABEL_CLASS}
                        info="A reusable calendar rule. Cron occurrences outside the allowed calendar are skipped."
                        options={[{ value: "", label: "No policy" }, ...(schedulePolicies.data ?? []).map(policy => ({ value: policy.name, label: policy.name }))]}
                      />
                      <div className="grid grid-cols-1 gap-4 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_5rem]">
                        <SelectField
                          control={form.control}
                          name="missedExecutionAction"
                          label="Late start"
                          labelClassName={COMPACT_FIELD_LABEL_CLASS}
                          info="Run late work or skip it after the grace period."
                          options={MISSED_EXECUTION_OPTIONS}
                        />
                        <SelectField
                          control={form.control}
                          name="catchUpMode"
                          label="Catch-up"
                          labelClassName={COMPACT_FIELD_LABEL_CLASS}
                          info="Run the latest missed occurrence after downtime."
                          options={CRON_CATCH_UP_OPTIONS}
                          disabled={missedExecutionAction !== "Skip"}
                        />
                        <DurationField
                          control={form.control}
                          name="maximumLatenessSeconds"
                          label="Grace"
                          labelClassName={COMPACT_FIELD_LABEL_CLASS}
                          info="Maximum allowed start delay."
                          minSeconds={0}
                          maxSeconds={31_536_000}
                          disabled={missedExecutionAction !== "Skip"}
                        />
                      </div>
                    </AdvancedSettingsGroup>

                    <AdvancedSettingsGroup title="Execution controls">
                      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                        <CronOverlapField
                          checked={form.watch("skipIfAlreadyRunning")}
                          onChange={value => form.setValue("skipIfAlreadyRunning", value, { shouldDirty: true })}
                          isChain={!!cron?.chain}
                          compact
                        />
                        <DurationField
                          control={form.control}
                          name="timeoutSeconds"
                          label="Timeout"
                          labelClassName={COMPACT_FIELD_LABEL_CLASS}
                          info="Cancels one function attempt after this duration. Set zero to disable the timeout."
                          minSeconds={0}
                          maxSeconds={86_400}
                        />
                      </div>
                    </AdvancedSettingsGroup>

                    <AdvancedSettingsGroup title="Recovery">
                      <SelectField
                        control={form.control}
                        name="onStale"
                        label="Owner loss"
                        labelClassName={COMPACT_FIELD_LABEL_CLASS}
                        info="Restart the attempt on another node, or record it as cancelled after lease loss."
                        options={ON_STALE_OPTIONS}
                      />
                    </AdvancedSettingsGroup>
                  </AdvancedSettings>
                </div>

                <footer className="sticky bottom-0 z-10 flex shrink-0 items-center justify-end gap-2 border-t border-border bg-surface-0/95 px-4 py-4 backdrop-blur sm:px-6">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="h-8 rounded-lg text-xs"
                    onClick={() => handleOpenChange(false)}
                  >
                    Cancel
                  </Button>
                  <Button
                    type="submit"
                    variant="gradient"
                    size="sm"
                    className="h-8 rounded-lg text-xs"
                    disabled={form.formState.isSubmitting || !isDirty || !editMetadataReady}
                  >
                    <Save className="mr-1.5 h-3 w-3" />
                    {form.formState.isSubmitting ? "Saving…" : "Save changes"}
                  </Button>
                </footer>
              </form>
            </Form>
          </div>
        </SheetContent>
      </Sheet>

      <ConfirmDialog
        open={leavePromptOpen}
        onOpenChange={setLeavePromptOpen}
        title="Discard unsaved changes?"
        description="Your changes to this cron ticker will be lost."
        confirmLabel="Discard changes"
        confirmVariant="destructive"
        onConfirm={closeAndReset}
      />
    </>
  );
}
