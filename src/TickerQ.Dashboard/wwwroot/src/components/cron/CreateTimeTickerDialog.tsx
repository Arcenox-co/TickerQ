import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { FilePenLine, FlaskConical, Plus } from "lucide-react";
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
import { TextareaField } from "@/components/form-fields/textarea-field";
import { DateTimeField } from "@/components/form-fields/datetime-field";
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
import type { FunctionInfoDto, TestRunResponse } from "@/services/api-types";
import { dashboardApi } from "@/services/dashboard-api";
import {
  initialPayloadForFunction,
  RequestPayloadEditor,
  validateRequestPayload,
} from "@/components/cron/RequestPayloadEditor";
import { createDraftState, selectFunction } from "@/lib/function-draft";
import { encodeRequestPayload } from "@/lib/request-payload";
import { SaveDefinitionDialog, type SaveDefinitionValues } from "@/components/cron/SaveDefinitionDialog";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { SingleJobTestDialog } from "@/components/cron/SingleJobTestDialog";
import { AdvancedSettings, AdvancedSettingsGroup } from "@/components/cron/AdvancedSettings";
import {
  MISSED_EXECUTION_OPTIONS,
  ON_STALE_OPTIONS,
} from "@/lib/cron/ticker-form-options";
import { useCreateDefinition } from "@/services/hooks";
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
  schedulePolicyName: z.string().optional(),
  blockedOccurrenceBehavior: z.enum(["Reject", "Skip", "MoveForward"]),
  missedExecutionAction: z.enum(["RunOnce", "Skip"]),
  maximumLatenessSeconds: z.number().int().min(0).max(31_536_000).optional(),
}).superRefine((values, context) => {
  if (values.missedExecutionAction === "Skip" && values.maximumLatenessSeconds == null)
    context.addIssue({ code: "custom", path: ["maximumLatenessSeconds"], message: "Set the allowed lateness." });
});

type FormValues = z.infer<typeof schema>;

export function CreateTimeTickerDialog({
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
      schedulePolicyName: "",
      blockedOccurrenceBehavior: "Reject",
      missedExecutionAction: "RunOnce",
      maximumLatenessSeconds: 300,
    },
  });
  const { isDirty, isSubmitting } = form.formState;

  const retriesValue = form.watch("retries") ?? 0;
  const intervalsValue = form.watch("retryIntervalsSeconds") ?? "";
  const selectedFunctionName = form.watch("function");
  const requestJson = form.watch("requestJson") ?? "";
  const missedExecutionAction = form.watch("missedExecutionAction");
  const selectedFunction = functions.find((item) => item.functionName === selectedFunctionName);
  const draftBook = useRef(createDraftState());
  const requestEditorValid = useRef(true);
  const createDefinition = useCreateDefinition();
  const schedulePolicies = useSchedulePolicies();
  const [saveDraftOpen, setSaveDraftOpen] = useState(false);
  const [leavePromptOpen, setLeavePromptOpen] = useState(false);
  const [leaveAfterSave, setLeaveAfterSave] = useState(false);
  const [testConfirmOpen, setTestConfirmOpen] = useState(false);
  const [testOpen, setTestOpen] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<TestRunResponse | null>(null);
  const [testError, setTestError] = useState<string | null>(null);
  const [pendingTestValues, setPendingTestValues] = useState<FormValues | null>(null);

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
    setLeavePromptOpen(false);
    setLeaveAfterSave(false);
    setSaveDraftOpen(false);
    setTestConfirmOpen(false);
    setTestOpen(false);
    setTesting(false);
    setTestResult(null);
    setTestError(null);
    setPendingTestValues(null);
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

  async function validatedDefinitionValues() {
    if (!await form.trigger()) return null;
    if (!requestEditorValid.current) {
      form.setError("requestJson", { type: "validate", message: "Complete or correct the structured JSON field." });
      return null;
    }
    const values = form.getValues();
    const selected = functions.find(item => item.functionName === values.function);
    const requestError = validateRequestPayload(selected, values.requestJson ?? "");
    if (requestError) {
      form.setError("requestJson", { type: "validate", message: requestError });
      return null;
    }
    return values;
  }

  async function saveDraft(metadata: SaveDefinitionValues) {
    const values = await validatedDefinitionValues();
    if (!values) return;
    try {
      await createDefinition.mutateAsync({
        kind: "Draft",
        target: "TimeTicker",
        name: metadata.name,
        description: metadata.description || null,
        includeRequestData: metadata.includeRequestData,
        payload: {
          executionTime: metadata.executionTime
            ? new Date(metadata.executionTime).toISOString()
            : null,
          expression: null,
          skipIfAlreadyRunning: null,
          schedulePolicyName: values.schedulePolicyName || null,
          blockedOccurrenceBehavior: values.schedulePolicyName ? values.blockedOccurrenceBehavior : null,
          missedExecutionAction: values.missedExecutionAction,
          maximumLatenessSeconds: values.missedExecutionAction === "Skip"
            ? values.maximumLatenessSeconds ?? 0
            : null,
          root: {
            function: values.function,
            description: values.description || null,
            retries: values.retries ?? 0,
            retryIntervalsSeconds: parseIntervalsList(values.retryIntervalsSeconds ?? "") ?? [],
            request: encodeRequestPayload(values.requestJson),
            onStale: values.onStale,
            timeoutSeconds: values.timeoutSeconds ?? 0,
            children: [],
          },
        },
      });
      if (metadata.executionTime !== (values.executionTime ?? "")) {
        form.setValue("executionTime", metadata.executionTime, { shouldDirty: true });
      }
      toast.success("Draft saved");
      setSaveDraftOpen(false);
      if (leaveAfterSave) closeAndReset();
    } catch (error) {
      toast.error("Could not save the draft", {
        description: error instanceof Error ? error.message : "Unknown error",
      });
    }
  }

  async function prepareTest() {
    const valid = await form.trigger(["function", "requestJson", "timeoutSeconds"]);
    const values = form.getValues();
    if (!valid || !requestEditorValid.current) {
      if (!requestEditorValid.current) {
        form.setError("requestJson", {
          type: "validate",
          message: "Complete or correct the structured JSON field.",
        });
      }
      return;
    }
    const selected = functions.find(item => item.functionName === values.function);
    const requestError = validateRequestPayload(selected, values.requestJson ?? "");
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
      await onSubmit?.(values);
      toast.success("Time ticker scheduled");
      form.reset();
      closeAndReset();
    } catch (err) {
      toast.error("Failed to create", {
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
            Schedule Time Ticker
          </SheetTitle>
          <SheetDescription className="mt-0.5 text-[12px] text-muted-foreground">
            Queue a one-time job on the selected environment.
          </SheetDescription>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto scrollbar-thin">
          <Form {...form}>
            <form
              className="flex min-h-full flex-col"
              onSubmit={form.handleSubmit(handleSubmit)}
            >
              <div className="flex-1 space-y-5 p-4 sm:p-6">
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  {functionOptions.length > 0 ? (
                    <SelectField
                      control={form.control}
                      name="function"
                      label="Function"
                      labelClassName={COMPACT_FIELD_LABEL_CLASS}
                      info="The registered TickerFunction invoked when this job runs."
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
                      info="The registered TickerFunction invoked when this job runs."
                      required
                      placeholder="MyFunctionName"
                    />
                  )}

                  <DateTimeField
                    control={form.control}
                    name="executionTime"
                    label="Execution time"
                    labelClassName={COMPACT_FIELD_LABEL_CLASS}
                    info="The local date and time requested for this one-time job."
                    step={1}
                  />
                </div>

                <TextareaField
                  control={form.control}
                  name="description"
                  label="Description"
                  labelClassName={COMPACT_FIELD_LABEL_CLASS}
                  info="Optional context shown to operators in the dashboard."
                  placeholder="Add details that help operators understand this job…"
                  maxLength={2000}
                  rows={3}
                  className="min-h-20 resize-y"
                />

                <div>
                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <NumberField
                      control={form.control}
                      name="retries"
                      label="Retries"
                      labelClassName={COMPACT_FIELD_LABEL_CLASS}
                      info="Additional attempts after the function reports a failure."
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

                <AdvancedSettings description="Schedule timing, execution limits, and recovery behavior.">
                  <AdvancedSettingsGroup title="Scheduling">
                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                      <SelectField
                        control={form.control}
                        name="schedulePolicyName"
                        label="Schedule policy"
                        labelClassName={COMPACT_FIELD_LABEL_CLASS}
                        info="A reusable calendar rule that allows or blocks the requested execution time."
                        options={[{ value: "", label: "No policy" }, ...(schedulePolicies.data ?? []).map(policy => ({ value: policy.name, label: policy.name }))]}
                      />
                      <SelectField
                        control={form.control}
                        name="blockedOccurrenceBehavior"
                        label="If blocked"
                        labelClassName={COMPACT_FIELD_LABEL_CLASS}
                        info="Reject the job, preserve it as skipped, or move it to the policy's next allowed time."
                        disabled={!form.watch("schedulePolicyName")}
                        options={[
                          { value: "Reject", label: "Reject creation" },
                          { value: "MoveForward", label: "Move forward" },
                          { value: "Skip", label: "Save as skipped" },
                        ]}
                      />
                      <SelectField
                        control={form.control}
                        name="missedExecutionAction"
                        label="If late"
                        labelClassName={COMPACT_FIELD_LABEL_CLASS}
                        info="Choose whether late work remains eligible or is skipped after a bounded delay."
                        options={MISSED_EXECUTION_OPTIONS}
                      />
                      <DurationField
                        control={form.control}
                        name="maximumLatenessSeconds"
                        label="Grace period"
                        labelClassName={COMPACT_FIELD_LABEL_CLASS}
                        info="The longest allowed delay between the scheduled time and the start of execution."
                        minSeconds={0}
                        maxSeconds={31_536_000}
                        disabled={missedExecutionAction !== "Skip"}
                      />
                    </div>
                  </AdvancedSettingsGroup>

                  <AdvancedSettingsGroup title="Execution limits">
                    <DurationField
                      control={form.control}
                      name="timeoutSeconds"
                      label="Timeout"
                      labelClassName={COMPACT_FIELD_LABEL_CLASS}
                      info="Cancels one function attempt after this duration. Set zero to disable the timeout."
                      minSeconds={0}
                      maxSeconds={86_400}
                    />
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
                <div className="flex flex-wrap items-center gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="h-8 rounded-lg text-xs"
                    disabled={testing || isSubmitting}
                    onClick={() => void prepareTest()}
                  >
                    <FlaskConical className="mr-1.5 h-3 w-3" />
                    Test once
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="h-8 rounded-lg text-xs"
                    onClick={() => setSaveDraftOpen(true)}
                  >
                    <FilePenLine className="mr-1.5 h-3 w-3" />
                    Save draft
                  </Button>
                </div>
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

    <SaveDefinitionDialog
      open={saveDraftOpen}
      kind="Draft"
      initialExecutionTime={form.getValues("executionTime") ?? ""}
      pending={createDefinition.isPending}
      onOpenChange={nextOpen => {
        if (!nextOpen) {
          setSaveDraftOpen(false);
          setLeaveAfterSave(false);
        }
      }}
      onSave={saveDraft}
    />

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
          . It may change application data or call external services. No time ticker is saved.
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
          <DialogTitle>Keep this job configuration?</DialogTitle>
          <DialogDescription>
            Save it for later, or discard it and close the panel. No ticker has been scheduled yet.
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-2 py-1">
          <Button
            variant="outline"
            className="h-auto justify-start px-3 py-2.5 text-left"
            onClick={() => {
              setLeavePromptOpen(false);
              setLeaveAfterSave(true);
              setSaveDraftOpen(true);
            }}
          >
            <FilePenLine className="mr-2 h-4 w-4 text-primary" />
            <span><span className="block text-xs font-medium">Save as draft</span><span className="block text-[11px] font-normal text-muted-foreground">Edit and activate it later.</span></span>
          </Button>
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={() => setLeavePromptOpen(false)}>Keep editing</Button>
          <Button variant="destructive" onClick={closeAndReset}>Discard</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    </>
  );
}
