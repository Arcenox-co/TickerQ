import { useEffect, useMemo, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Save, X } from "lucide-react";
import { toast } from "sonner";

import { AdvancedSettings, AdvancedSettingsGroup } from "@/components/cron/AdvancedSettings";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import {
  initialPayloadForFunction,
  RequestPayloadEditor,
  validateRequestPayload,
} from "@/components/cron/RequestPayloadEditor";
import { Button } from "@/components/ui/button";
import { Form } from "@/components/ui/form";
import { DateTimeField } from "@/components/form-fields/datetime-field";
import { DurationField } from "@/components/form-fields/duration-field";
import { COMPACT_FIELD_LABEL_CLASS } from "@/components/form-fields/field-label";
import { NumberField } from "@/components/form-fields/number-field";
import { SelectField } from "@/components/form-fields/select-field";
import { TextareaField } from "@/components/form-fields/textarea-field";
import { TextField } from "@/components/form-fields/text-field";
import { MISSED_EXECUTION_OPTIONS, ON_STALE_OPTIONS } from "@/lib/cron/ticker-form-options";
import { describeRetryPolicy, parseIntervalsList } from "@/lib/cron/status-config";
import { parseUtc } from "@/lib/cron/format";
import { createDraftState, selectFunction } from "@/lib/function-draft";
import { decodeRequestPayload, encodeRequestPayload } from "@/lib/request-payload";
import {
  useAllFunctions,
  useSchedulePolicies,
  useTimeTickerRequest,
  useUpdateTimeTicker,
} from "@/services/hooks";
import type { TimeTickerFlatDto } from "@/services/api-types";

const schema = z.object({
  function: z.string().trim().min(1, "Function is required"),
  executionTime: z.string().trim().optional(),
  description: z.string().max(2000, "Max 2000 characters").optional(),
  retries: z.number().int().min(0).max(100).optional(),
  retryIntervalsSeconds: z
    .string()
    .optional()
    .refine((value) => value == null || parseIntervalsList(value) !== null, {
      message: "Use comma-separated whole numbers between 1 and 3600 (e.g. 5, 30, 60)",
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

function toDatetimeLocal(iso: string | null): string {
  if (!iso) return "";
  const date = parseUtc(iso);
  if (Number.isNaN(date.getTime())) return "";
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(
    date.getHours()
  )}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

function parseIntervals(raw: string | undefined): number[] | null {
  const parsed = parseIntervalsList(raw ?? "");
  return parsed && parsed.length > 0 ? parsed : null;
}

function formValuesFor(ticker: TimeTickerFlatDto, requestJson = ""): FormValues {
  return {
    function: ticker.functionName,
    executionTime: toDatetimeLocal(ticker.scheduledFor),
    description: ticker.description ?? "",
    retries: ticker.retries ?? 0,
    retryIntervalsSeconds: ticker.retryIntervalsSeconds?.join(", ") ?? "",
    requestJson,
    onStale: ticker.onStale ?? "Restart",
    timeoutSeconds: ticker.timeoutSeconds ?? 0,
    schedulePolicyName: ticker.schedulePolicyName ?? "",
    blockedOccurrenceBehavior: ticker.blockedOccurrenceBehavior ?? "Reject",
    missedExecutionAction: ticker.missedExecutionAction ?? "RunOnce",
    maximumLatenessSeconds: ticker.maximumLatenessSeconds ?? 300,
  };
}

/** Editable form body used by the time-ticker detail panel's Edit tab. */
export function EditTimeTickerFields({
  ticker,
  onCancel,
  onSaved,
}: {
  ticker: TimeTickerFlatDto;
  onCancel: () => void;
  onSaved: () => void;
}) {
  const update = useUpdateTimeTicker();
  const schedulePolicies = useSchedulePolicies();
  const functionsQuery = useAllFunctions();
  const requestQuery = useTimeTickerRequest(ticker.id);
  const [leavePromptOpen, setLeavePromptOpen] = useState(false);
  const draftBook = useRef(createDraftState());
  const requestEditorValid = useRef(true);
  const initializedRevision = useRef("");
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: formValuesFor(ticker),
  });
  const tickerFormRevision = [
    ticker.id,
    ticker.functionName,
    ticker.scheduledFor,
    ticker.description ?? "",
    ticker.retries ?? 0,
    ticker.retryIntervalsSeconds?.join(",") ?? "",
    ticker.onStale ?? "Restart",
    ticker.timeoutSeconds ?? 0,
    ticker.schedulePolicyName ?? "",
    ticker.blockedOccurrenceBehavior ?? "Reject",
    ticker.missedExecutionAction ?? "RunOnce",
    ticker.maximumLatenessSeconds ?? 300,
  ].join("|");

  useEffect(() => {
    if (initializedRevision.current === tickerFormRevision) return;
    initializedRevision.current = tickerFormRevision;
    form.reset(formValuesFor(ticker));
    setLeavePromptOpen(false);
    draftBook.current = {
      active: ticker.functionName,
      drafts: { [ticker.functionName]: "" },
    };
    requestEditorValid.current = true;
  }, [ticker, tickerFormRevision, form]);

  useEffect(() => {
    if (!requestQuery.isSuccess) return;
    if (form.getValues("function") !== ticker.functionName || form.formState.dirtyFields.requestJson)
      return;

    const requestJson = decodeRequestPayload(requestQuery.data.payload);
    form.resetField("requestJson", { defaultValue: requestJson });
    draftBook.current = {
      active: ticker.functionName,
      drafts: { [ticker.functionName]: requestJson },
    };
    requestEditorValid.current = true;
  }, [form, requestQuery.data, requestQuery.isSuccess, ticker.functionName]);

  const retriesValue = form.watch("retries") ?? 0;
  const intervalsValue = form.watch("retryIntervalsSeconds") ?? "";
  const selectedFunctionName = form.watch("function");
  const requestJson = form.watch("requestJson") ?? "";
  const missedExecutionAction = form.watch("missedExecutionAction");
  const functions = useMemo(() => functionsQuery.data ?? [], [functionsQuery.data]);
  const selectedFunction = functions.find(item => item.functionName === selectedFunctionName);
  const functionOptions = Array.from(new Set([
    ticker.functionName,
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

  function resetAndCancel() {
    form.reset(formValuesFor(ticker, decodeRequestPayload(requestQuery.data?.payload)));
    draftBook.current = createDraftState();
    requestEditorValid.current = true;
    setLeavePromptOpen(false);
    onCancel();
  }

  function requestCancel() {
    if (form.formState.isDirty) {
      setLeavePromptOpen(true);
      return;
    }
    resetAndCancel();
  }

  async function handleSubmit(values: FormValues) {
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
      await update.mutateAsync({
        id: ticker.id,
        body: {
          function: values.function,
          executionTime: values.executionTime || null,
          description: values.description || null,
          retries: values.retries ?? null,
          retryIntervalsSeconds: parseIntervals(values.retryIntervalsSeconds),
          request: values.requestJson?.trim()
            ? encodeRequestPayload(values.requestJson)
            : "",
          onStale: values.onStale,
          timeoutSeconds: values.timeoutSeconds ?? 0,
          schedulePolicyName: values.schedulePolicyName || null,
          replaceSchedulePolicy: true,
          blockedOccurrenceBehavior: values.schedulePolicyName ? values.blockedOccurrenceBehavior : null,
          missedExecutionAction: values.missedExecutionAction,
          maximumLatenessSeconds: values.missedExecutionAction === "Skip"
            ? values.maximumLatenessSeconds ?? 0
            : null,
        },
      });
      toast.success("Time ticker updated");
      onSaved();
    } catch (error) {
      toast.error("Failed to update", {
        description: error instanceof Error ? error.message : "An error occurred",
      });
    }
  }

  return (
    <div className="flex min-h-full flex-col">
      <header className="flex shrink-0 items-start justify-between gap-3 border-b border-border px-5 py-4">
        <div className="min-w-0">
          <h2 className="text-sm font-semibold">Edit time ticker</h2>
          <p className="mt-0.5 truncate font-mono text-[11px] text-muted-foreground">{ticker.functionName}</p>
        </div>
        <button
          type="button"
          aria-label="Close editor"
          onClick={requestCancel}
          className="rounded-md p-1 text-muted-foreground/60 transition-colors hover:bg-surface-1 hover:text-foreground"
        >
          <X className="h-4 w-4" />
        </button>
      </header>

      <Form {...form}>
        <form className="flex flex-1 flex-col" onSubmit={form.handleSubmit(handleSubmit)}>
          <div className="flex-1 space-y-5 px-5 py-4">
            <div className="grid grid-cols-1 items-start gap-4 sm:grid-cols-2">
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
              <DateTimeField
                control={form.control}
                name="executionTime"
                label="Execution time"
                labelClassName={COMPACT_FIELD_LABEL_CLASS}
                info="The local date and time requested for this one-time job."
                step={1}
              />
              <div className="sm:col-span-2">
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
              </div>
            </div>

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
                onChange={value => form.setValue("requestJson", value, { shouldDirty: true })}
                onValidityChange={valid => { requestEditorValid.current = valid; }}
                error={form.formState.errors.requestJson?.message}
              />
            )}

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
                    info="Reject the update, preserve the job as skipped, or move it to the policy's next allowed time."
                    disabled={!form.watch("schedulePolicyName")}
                    options={[
                      { value: "Reject", label: "Reject update" },
                      { value: "MoveForward", label: "Move forward" },
                      { value: "Skip", label: "Mark skipped" },
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

          <footer className="sticky bottom-0 z-10 flex shrink-0 items-center justify-end gap-2 border-t border-border bg-surface-0/95 px-5 py-4 backdrop-blur">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-8 rounded-lg text-xs"
              onClick={requestCancel}
            >
              Cancel
            </Button>
            <Button
              type="submit"
              variant="gradient"
              size="sm"
              className="h-8 rounded-lg text-xs"
              disabled={form.formState.isSubmitting || !form.formState.isDirty || !editMetadataReady}
            >
              <Save className="mr-1.5 h-3 w-3" />
              {form.formState.isSubmitting ? "Saving…" : "Save changes"}
            </Button>
          </footer>
        </form>
      </Form>

      <ConfirmDialog
        open={leavePromptOpen}
        onOpenChange={setLeavePromptOpen}
        title="Discard unsaved changes?"
        description="Your changes to this time ticker will be lost."
        confirmLabel="Discard changes"
        confirmVariant="destructive"
        onConfirm={resetAndCancel}
      />
    </div>
  );
}
