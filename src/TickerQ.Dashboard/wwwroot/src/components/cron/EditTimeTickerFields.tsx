import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { RotateCcw, Save } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Form } from "@/components/ui/form";
import { TextField } from "@/components/form-fields/text-field";
import { DateTimeField } from "@/components/form-fields/datetime-field";
import { NumberField } from "@/components/form-fields/number-field";
import { SelectField } from "@/components/form-fields/select-field";
import { ON_STALE_OPTIONS } from "@/lib/cron/ticker-form-options";
import { describeRetryPolicy, parseIntervalsList } from "@/lib/cron/status-config";
import { parseUtc } from "@/lib/cron/format";
import { useUpdateTimeTicker } from "@/services/hooks";
import type { TimeTickerFlatDto } from "@/services/api-types";

const schema = z.object({
  executionTime: z.string().trim().optional(),
  description: z.string().max(2000, "Max 2000 characters").optional(),
  retries: z.number().int().min(0).max(100).optional(),
  retryIntervalsSeconds: z
    .string()
    .optional()
    .refine((v) => v == null || parseIntervalsList(v) !== null, {
      message: "Use comma-separated whole numbers between 1 and 3600 (e.g. 5, 30, 60)",
    }),
  onStale: z.enum(["Restart", "Cancel"]),
  timeoutSeconds: z.number().int().min(0).max(86400).optional(),
});

type FormValues = z.infer<typeof schema>;

function toDatetimeLocal(iso: string | null): string {
  if (!iso) return "";
  const d = parseUtc(iso);
  if (Number.isNaN(d.getTime())) return "";
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(
    d.getHours()
  )}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

function parseIntervals(raw: string | undefined): number[] | null {
  const parsed = parseIntervalsList(raw ?? "");
  return parsed && parsed.length > 0 ? parsed : null;
}

/**
 * Editable time-ticker form body (no Sheet/Dialog chrome). Self-contained:
 * calls useUpdateTimeTicker + toasts. Used by the time-ticker detail panel's
 * Edit tab.
 */
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
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      executionTime: toDatetimeLocal(ticker.scheduledFor),
      description: ticker.description ?? "",
      retries: ticker.retries ?? 0,
      retryIntervalsSeconds: ticker.retryIntervalsSeconds?.join(", ") ?? "",
      onStale: ticker.onStale ?? "Restart",
      timeoutSeconds: ticker.timeoutSeconds ?? 0,
    },
  });

  useEffect(() => {
    form.reset({
      executionTime: toDatetimeLocal(ticker.scheduledFor),
      description: ticker.description ?? "",
      retries: ticker.retries ?? 0,
      retryIntervalsSeconds: ticker.retryIntervalsSeconds?.join(", ") ?? "",
      onStale: ticker.onStale ?? "Restart",
      timeoutSeconds: ticker.timeoutSeconds ?? 0,
    });
  }, [ticker, form]);

  const retriesValue = form.watch("retries") ?? 0;
  const intervalsValue = form.watch("retryIntervalsSeconds") ?? "";

  async function handleSubmit(values: FormValues) {
    try {
      await update.mutateAsync({
        id: ticker.id,
        body: {
          executionTime: values.executionTime || null,
          description: values.description || null,
          retries: values.retries ?? null,
          retryIntervalsSeconds: parseIntervals(values.retryIntervalsSeconds),
          onStale: values.onStale,
          timeoutSeconds: values.timeoutSeconds ?? 0,
        },
      });
      toast.success("Time ticker updated");
      onSaved();
    } catch (err) {
      toast.error("Failed to update", {
        description: err instanceof Error ? err.message : "An error occurred",
      });
    }
  }

  return (
    <Form {...form}>
      <form className="flex flex-col" onSubmit={form.handleSubmit(handleSubmit)}>
        <div className="space-y-4">
          <DateTimeField
            control={form.control}
            name="executionTime"
            label="Execution time"
            step={1}
            description="When this ticker should fire — ✕ keeps the current schedule."
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
              <NumberField control={form.control} name="retries" label="Retries" min={0} max={100} />
              <TextField
                control={form.control}
                name="retryIntervalsSeconds"
                label="Intervals (sec)"
                placeholder={retriesValue > 0 ? "5, 30, 60" : "—"}
                disabled={retriesValue <= 0}
              />
            </div>
            <p className="text-[11px] text-muted-foreground/70 italic">
              → {describeRetryPolicy(retriesValue, parseIntervalsList(intervalsValue))}
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
        </div>

        <footer className="mt-6 flex items-center justify-end gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-8 text-xs rounded-lg"
            onClick={onCancel}
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
  );
}
