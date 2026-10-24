import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { FlaskConical, Loader2, Plus } from "lucide-react";
import { toast } from "sonner";

import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Form } from "@/components/ui/form";
import { CronOverlapField } from "@/components/cron/CronOverlapField";
import { CronSchedulePresets } from "@/components/cron/CronSchedulePresets";
import { TextField } from "@/components/form-fields/text-field";
import { TextareaField } from "@/components/form-fields/textarea-field";
import { TimezoneField } from "@/components/form-fields/timezone-field";
import { COMPACT_FIELD_LABEL_CLASS } from "@/components/form-fields/field-label";

import { NumberField } from "@/components/form-fields/number-field";
import { DurationField } from "@/components/form-fields/duration-field";
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
import type { FunctionInfoDto, TestRunResponse } from "@/services/api-types";
import { dashboardApi } from "@/services/dashboard-api";
import { encodeRequestPayload } from "@/lib/request-payload";
import {
  initialPayloadForFunction,
  RequestPayloadEditor,
  validateRequestPayload,
} from "@/components/cron/RequestPayloadEditor";
import { createDraftState, selectFunction } from "@/lib/function-draft";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { SingleJobTestDialog } from "@/components/cron/SingleJobTestDialog";
import { AdvancedSettings, AdvancedSettingsGroup } from "@/components/cron/AdvancedSettings";
import { FieldInfo } from "@/components/form-fields/field-info";
import {
  CRON_CATCH_UP_OPTIONS,
  MISSED_EXECUTION_OPTIONS,
  ON_STALE_OPTIONS,
} from "@/lib/cron/ticker-form-options";
import { useSchedulePolicies } from "@/services/hooks";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

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
  timeZoneId: z.string().trim().optional(),
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

type FormValues = z.infer<typeof schema>;

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
      skipIfAlreadyRunning: false,
      timeZoneId: "UTC",
      schedulePolicyName: "",
      missedExecutionAction: "RunOnce",
      maximumLatenessSeconds: 300,
      catchUpMode: "Skip",
    },
  });
  const { isDirty, isSubmitting } = form.formState;

  const retriesValue = form.watch("retries") ?? 0;
  const intervalsValue = form.watch("retryIntervalsSeconds") ?? "";
  const expressionValue = form.watch("expression") ?? "";
  const selectedFunctionName = form.watch("function");
  const requestJson = form.watch("requestJson") ?? "";
  const missedExecutionAction = form.watch("missedExecutionAction");
  const selectedFunction = functions.find((item) => item.functionName === selectedFunctionName);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<TestRunResponse | null>(null);
  const [testError, setTestError] = useState<string | null>(null);
  const [testConfirmOpen, setTestConfirmOpen] = useState(false);
  const [testOpen, setTestOpen] = useState(false);
  const [pendingTestValues, setPendingTestValues] = useState<FormValues | null>(null);
  const draftBook = useRef(createDraftState());
  const requestEditorValid = useRef(true);
  const schedulePolicies = useSchedulePolicies();
  const [leavePromptOpen, setLeavePromptOpen] = useState(false);
  const [activationPromptOpen, setActivationPromptOpen] = useState(false);
  const [pendingCreateValues, setPendingCreateValues] = useState<FormValues | null>(null);
  const [creatingMode, setCreatingMode] = useState<"now" | "later" | null>(null);

  useEffect(() => {
    if (missedExecutionAction !== "Skip" && form.getValues("catchUpMode") !== "Skip")
      form.setValue("catchUpMode", "Skip", { shouldDirty: true, shouldValidate: true });
  }, [form, missedExecutionAction]);

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

  function closeAndReset() {
    resetDraftSession();
    form.reset();
    setTestResult(null);
    setTestError(null);
    setTestConfirmOpen(false);
    setTestOpen(false);
    setTesting(false);
    setPendingTestValues(null);
    setLeavePromptOpen(false);
    setActivationPromptOpen(false);
    setPendingCreateValues(null);
    setCreatingMode(null);
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

  function handleSubmit(values: FormValues) {
    if (!requestEditorValid.current) {
      form.setError("requestJson", { type: "validate", message: "Complete or correct the structured JSON field." });
      return;
    }
    const requestError = validateRequestPayload(selectedFunction, values.requestJson ?? "");
    if (requestError) {
      form.setError("requestJson", { type: "validate", message: requestError });
      return;
    }
    setPendingCreateValues(values);
    setActivationPromptOpen(true);
  }

  async function createTicker(isEnabled: boolean) {
    if (!pendingCreateValues || creatingMode) return;
    setCreatingMode(isEnabled ? "now" : "later");
    try {
      await onSubmit?.({ ...pendingCreateValues, isEnabled });
      toast.success("Cron ticker created");
      form.reset();
      closeAndReset();
    } catch (err) {
      toast.error("Failed to create", {
        description: err instanceof Error ? err.message : "An error occurred",
      });
    } finally {
      setCreatingMode(null);
    }
  }

  async function prepareTest() {
    const valid = await form.trigger(["function", "requestJson", "timeoutSeconds"]);
    const values = form.getValues();
    if (!valid || !requestEditorValid.current) {
      if (!requestEditorValid.current)
        form.setError("requestJson", { type: "validate", message: "Complete or correct the structured JSON field." });
      return;
    }
    const requestError = validateRequestPayload(selectedFunction, values.requestJson ?? "");
    if (requestError) {
      form.setError("requestJson", { type: "validate", message: requestError });
      return;
    }
    setPendingTestValues(values);
    setTestConfirmOpen(true);
  }

  async function executeTest() {
    if (!pendingTestValues || testing) return;
    const values = pendingTestValues;
    setTestConfirmOpen(false);
    setTestOpen(true);
    setTesting(true);
    setTestResult(null);
    setTestError(null);
    try {
      const result = await dashboardApi.testRunFunction({
        function: values.function,
        request: encodeRequestPayload(values.requestJson ?? ""),
        timeoutSeconds: values.timeoutSeconds || undefined,
      });
      setTestResult(result);
    } catch (error) {
      setTestError(error instanceof Error ? error.message : "Unknown error");
    } finally {
      setTesting(false);
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
            Schedule Cron Ticker
          </SheetTitle>
          <SheetDescription className="mt-0.5 text-[12px] text-muted-foreground">
            Recurring job that fires whenever the cron expression matches.
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
                  {functionOptions.length > 0 ? (
                    <SelectField
                      control={form.control}
                      name="function"
                      label="Function"
                      labelClassName={COMPACT_FIELD_LABEL_CLASS}
                      info="The registered TickerFunction invoked for every occurrence."
                      required
                      options={functionOptions}
                      placeholder="Select a function"
                    />
                  ) : (
                    <TextField
                      control={form.control}
                      name="function"
                      label="Function"
                      labelClassName={COMPACT_FIELD_LABEL_CLASS}
                      info="The registered TickerFunction invoked for every occurrence."
                      required
                      placeholder="MyFunctionName"
                    />
                  )}

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

                <RequestPayloadEditor
                  key={selectedFunctionName}
                  functionInfo={selectedFunction}
                  value={requestJson}
                  onChange={(value) => form.setValue("requestJson", value, { shouldDirty: true })}
                  onValidityChange={(valid) => { requestEditorValid.current = valid; }}
                  error={form.formState.errors.requestJson?.message}
                />

                <AdvancedSettings description="Schedule timing, execution controls, and recovery behavior.">
                  <AdvancedSettingsGroup title="Scheduling">
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

              <footer className="sticky bottom-0 z-10 flex shrink-0 flex-col gap-3 border-t border-border bg-surface-0/95 px-4 py-4 backdrop-blur sm:flex-row sm:items-center sm:justify-between sm:px-6">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-8 w-fit rounded-lg text-xs"
                  disabled={testing || isSubmitting}
                  onClick={() => void prepareTest()}
                >
                  <FlaskConical className="mr-1.5 h-3 w-3" />
                  Test once
                </Button>
                <div className="flex flex-wrap items-center justify-end gap-2">
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
                    disabled={isSubmitting}
                  >
                    <Plus className="mr-1.5 h-3 w-3" />
                    {isSubmitting ? "Creating…" : "Create"}
                  </Button>
                </div>
              </footer>
            </form>
          </Form>
        </div>
      </SheetContent>
    </Sheet>

    <Dialog
      open={activationPromptOpen}
      onOpenChange={(nextOpen) => {
        if (!creatingMode) setActivationPromptOpen(nextOpen);
      }}
    >
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>When should this cron ticker activate?</DialogTitle>
          <DialogDescription>
            Activate it now to begin producing occurrences, or create it inactive and activate it later from the cron ticker details.
          </DialogDescription>
        </DialogHeader>
        {pendingCreateValues && (
          <div className="rounded-lg border border-border/60 bg-surface-1/50 px-3 py-2.5 text-xs">
            <div className="font-medium text-foreground">{pendingCreateValues.function}</div>
            <div className="mt-1 font-mono text-[11px] text-muted-foreground">
              {pendingCreateValues.expression} · {pendingCreateValues.timeZoneId || "UTC"}
            </div>
          </div>
        )}
        <DialogFooter className="gap-2 sm:gap-2">
          <Button
            type="button"
            variant="ghost"
            disabled={creatingMode !== null}
            onClick={() => setActivationPromptOpen(false)}
          >
            Cancel
          </Button>
          <Button
            type="button"
            variant="outline"
            disabled={creatingMode !== null}
            onClick={() => void createTicker(false)}
          >
            {creatingMode === "later" && <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />}
            Activate later
          </Button>
          <Button
            type="button"
            variant="gradient"
            disabled={creatingMode !== null}
            onClick={() => void createTicker(true)}
          >
            {creatingMode === "now" && <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />}
            Activate now
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <ConfirmDialog
      open={testConfirmOpen}
      onOpenChange={setTestConfirmOpen}
      title="Execute this function now?"
      description={
        <>
          This immediately invokes the real function{" "}
          <span className="font-mono font-semibold text-foreground">
            {pendingTestValues?.function}
          </span>
          . It may change application data or call external services. No cron ticker or occurrence is saved.
        </>
      }
      confirmLabel="Execute test"
      isPending={testing}
      onConfirm={executeTest}
    />

    <SingleJobTestDialog
      open={testOpen}
      pending={testing}
      result={testResult}
      error={testError}
      functionName={pendingTestValues?.function ?? selectedFunctionName ?? ""}
      onClose={() => setTestOpen(false)}
    />

    <Dialog open={leavePromptOpen} onOpenChange={setLeavePromptOpen}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Keep this cron configuration?</DialogTitle>
          <DialogDescription>
            Keep editing, or discard this unsaved cron configuration and close the panel. No cron ticker has been created yet.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="ghost" onClick={() => setLeavePromptOpen(false)}>Keep editing</Button>
          <Button variant="destructive" onClick={closeAndReset}>Discard</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    </>
  );
}
