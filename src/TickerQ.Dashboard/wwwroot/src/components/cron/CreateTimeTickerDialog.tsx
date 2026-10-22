import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { FilePenLine, FlaskConical, Plus, RotateCcw } from "lucide-react";
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

import { NumberField } from "@/components/form-fields/number-field";
import {
  SelectField,
  type SelectOption,
} from "@/components/form-fields/select-field";
import { ON_STALE_OPTIONS } from "@/lib/cron/ticker-form-options";
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
import { useCreateDefinition } from "@/services/hooks";
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
    },
  });

  const retriesValue = form.watch("retries") ?? 0;
  const intervalsValue = form.watch("retryIntervalsSeconds") ?? "";
  const selectedFunctionName = form.watch("function");
  const requestJson = form.watch("requestJson") ?? "";
  const selectedFunction = functions.find((item) => item.functionName === selectedFunctionName);
  const draftBook = useRef(createDraftState());
  const requestEditorValid = useRef(true);
  const createDefinition = useCreateDefinition();
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
    if (form.formState.isDirty) {
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

                <RequestPayloadEditor
                  key={selectedFunctionName}
                  functionInfo={selectedFunction}
                  value={requestJson}
                  onChange={(value) => form.setValue("requestJson", value, { shouldDirty: true })}
                  onValidityChange={(valid) => { requestEditorValid.current = valid; }}
                  error={form.formState.errors.requestJson?.message}
                />
              </div>

              <footer className="mt-6 space-y-3 border-t border-border/60 pt-4">
                <div className="flex flex-wrap items-center justify-end gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="h-8 text-xs rounded-lg"
                    disabled={testing || form.formState.isSubmitting}
                    onClick={() => void prepareTest()}
                  >
                    <FlaskConical className="mr-1.5 h-3 w-3" />
                    Test once
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="h-8 text-xs rounded-lg"
                    onClick={() => setSaveDraftOpen(true)}
                  >
                    <FilePenLine className="mr-1.5 h-3 w-3" />
                    Save draft
                  </Button>
                </div>
                <div className="flex flex-wrap items-center justify-end gap-2 border-t border-border/60 pt-3">
                  <Button
                    type="button"
                    variant="ghost"
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
