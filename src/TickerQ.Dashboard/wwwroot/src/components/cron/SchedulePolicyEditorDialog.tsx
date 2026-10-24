import { useEffect, useState, type ReactNode } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Ban,
  CalendarDays,
  Check,
  ChevronsUpDown,
  Clock3,
  Layers3,
  LoaderCircle,
  Plus,
  RefreshCw,
  Settings2,
  Trash2,
} from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { Textarea } from "@/components/ui/textarea";
import { FieldInfo } from "@/components/form-fields/field-info";
import { TimezoneCombobox } from "@/components/form-fields/timezone-field";
import { dashboardApi } from "@/services/dashboard-api";
import {
  qk,
  useScheduleDateProviders,
  useSchedulePolicyUsage,
} from "@/services/hooks";
import type {
  SaveSchedulePolicyRequest,
  SchedulePolicy,
} from "@/services/api-types";
import { cn } from "@/lib/utils";
import { expandWeekdays, weekdays } from "@/lib/schedule-weekdays";

function timeValue(value: string) {
  return value?.slice(0, 5) || "09:00";
}

function emptyPolicy(): SaveSchedulePolicyRequest {
  return {
    name: "",
    description: null,
    timeZoneId: "UTC",
    allowedWeekdays: "MondayToFriday",
    allowedTimeWindows: [{ start: "09:00:00", end: "17:00:00" }],
    excludedDates: [],
    excludedAnnualDates: [],
    dateProviderNames: [],
    requiredPolicyNames: [],
    excludedWhenPolicyAllowsNames: [],
  };
}

function toForm(policy: SchedulePolicy): SaveSchedulePolicyRequest {
  return {
    name: policy.name,
    description: policy.description,
    timeZoneId: policy.timeZoneId,
    allowedWeekdays: policy.allowedWeekdays,
    allowedTimeWindows: policy.allowedTimeWindows.map((item) => ({ ...item })),
    excludedDates: [...policy.excludedDates],
    excludedAnnualDates: policy.excludedAnnualDates.map((item) => ({ ...item })),
    dateProviderNames: [...policy.dateProviderNames],
    requiredPolicyNames: [...policy.requiredPolicyNames],
    excludedWhenPolicyAllowsNames: [...policy.excludedWhenPolicyAllowsNames],
  };
}

export function SchedulePolicyEditorDialog({
  open,
  policy,
  policies,
  onClose,
  onSaved,
}: {
  open: boolean;
  policy: SchedulePolicy | null;
  policies: SchedulePolicy[];
  onClose: () => void;
  onSaved?: (policy: SchedulePolicy) => void;
}) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState<SaveSchedulePolicyRequest>(() =>
    policy ? toForm(policy) : emptyPolicy(),
  );
  const usage = useSchedulePolicyUsage(policy?.id ?? null);
  const dateProviders = useScheduleDateProviders();

  useEffect(() => {
    if (open) setForm(policy ? toForm(policy) : emptyPolicy());
  }, [open, policy]);

  const save = useMutation({
    mutationFn: () => policy
      ? dashboardApi.updateSchedulePolicy(policy.id, policy.version, form)
      : dashboardApi.createSchedulePolicy(form),
    onSuccess: async (savedPolicy) => {
      toast.success(policy ? "Schedule policy updated" : "Schedule policy created");
      await queryClient.invalidateQueries({ queryKey: qk.schedulePolicies });
      onSaved?.(savedPolicy);
      onClose();
    },
    onError: (error) => toast.error("Could not save policy", {
      description: error instanceof Error ? error.message : "Unknown error",
    }),
  });

  const selectedDays = expandWeekdays(form.allowedWeekdays);
  const composablePolicies = policies.filter((item) => item.id !== policy?.id);
  const setList = (key: "excludedDates", value: string) =>
    setForm((current) => ({
      ...current,
      [key]: value.split(",").map((item) => item.trim()).filter(Boolean),
    }));

  function submit() {
    if (!form.name.trim()) {
      toast.error("Policy name is required");
      return;
    }
    if (selectedDays.length === 0) {
      toast.error("Select at least one allowed weekday");
      return;
    }
    save.mutate();
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="flex max-h-[92vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-[680px]">
        <DialogHeader className="shrink-0 border-b border-border/70 px-6 pb-4 pt-5">
          <DialogTitle>
            {policy ? `Edit ${policy.name}` : "Create schedule policy"}
          </DialogTitle>
          <DialogDescription className="text-xs">
            {policy ? "Update this reusable calendar." : "Create a reusable calendar for scheduled work."}
          </DialogDescription>
        </DialogHeader>

        <div className="min-h-0 flex-1 space-y-6 overflow-y-auto px-6 py-5 scrollbar-thin">
          <PolicySection icon={<Settings2 />} title="Basics">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
              <Field label="Name" required>
                <Input
                  value={form.name}
                  disabled={!!policy}
                  placeholder="Business hours"
                  onChange={(event) => setForm({ ...form, name: event.target.value })}
                />
              </Field>
              <Field label="Time zone" info="Days, hours, and exclusions are evaluated in this time zone.">
                <TimezoneCombobox
                  value={form.timeZoneId}
                  onChange={(timeZoneId) => setForm({ ...form, timeZoneId })}
                />
              </Field>
            </div>
            <Field label="Description">
              <Textarea
                rows={3}
                value={form.description ?? ""}
                placeholder="When should this policy allow scheduled work?"
                onChange={(event) => setForm({
                  ...form,
                  description: event.target.value || null,
                })}
              />
            </Field>
          </PolicySection>

          <PolicySection icon={<Clock3 />} title="Availability">
            <Field label="Days" info="A scheduled time must fall on one of these days.">
              <div className="grid grid-cols-7 gap-1.5">
                {weekdays.map((day) => {
                  const selected = selectedDays.includes(day);
                  return (
                    <Button
                      key={day}
                      type="button"
                      size="sm"
                      variant={selected ? "default" : "outline"}
                      aria-pressed={selected}
                      className={cn("h-9 px-1 text-[11px]", !selected && "text-muted-foreground")}
                      onClick={() => {
                        const next = selected
                          ? selectedDays.filter((item) => item !== day)
                          : [...selectedDays, day];
                        setForm({
                          ...form,
                          allowedWeekdays: weekdays
                            .filter((item) => next.includes(item))
                            .join(", "),
                        });
                      }}
                    >
                      {day.slice(0, 3)}
                    </Button>
                  );
                })}
              </div>
            </Field>

            <Field
              label="Hours"
              info="Use multiple windows for split schedules. Windows may cross midnight."
              labelEnd={(
                <label className="flex cursor-pointer items-center gap-2 text-[11px] text-muted-foreground">
                  <Checkbox
                    checked={form.allowedTimeWindows.length === 0}
                    onCheckedChange={(checked) => setForm((current) => ({
                      ...current,
                      allowedTimeWindows: checked
                        ? []
                        : current.allowedTimeWindows.length > 0
                          ? current.allowedTimeWindows
                          : [{ start: "09:00:00", end: "17:00:00" }],
                    }))}
                  />
                  All day
                </label>
              )}
            >
              {form.allowedTimeWindows.length > 0 ? (
                <div className="space-y-2">
                  {form.allowedTimeWindows.map((window, index) => (
                    <div key={index} className="grid grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)_auto] items-center gap-2">
                      <Input
                        type="time"
                        aria-label={`Window ${index + 1} start`}
                        value={timeValue(window.start)}
                        onChange={(event) => setForm((current) => ({
                          ...current,
                          allowedTimeWindows: current.allowedTimeWindows.map((item, itemIndex) =>
                            itemIndex === index ? { ...item, start: `${event.target.value}:00` } : item,
                          ),
                        }))}
                      />
                      <span className="text-[11px] text-muted-foreground">to</span>
                      <Input
                        type="time"
                        aria-label={`Window ${index + 1} end`}
                        value={timeValue(window.end)}
                        onChange={(event) => setForm((current) => ({
                          ...current,
                          allowedTimeWindows: current.allowedTimeWindows.map((item, itemIndex) =>
                            itemIndex === index ? { ...item, end: `${event.target.value}:00` } : item,
                          ),
                        }))}
                      />
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon-sm"
                        aria-label={`Remove window ${index + 1}`}
                        title="Remove window"
                        onClick={() => setForm((current) => ({
                          ...current,
                          allowedTimeWindows: current.allowedTimeWindows.filter((_, itemIndex) => itemIndex !== index),
                        }))}
                      >
                        <Trash2 />
                      </Button>
                    </div>
                  ))}
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => setForm((current) => ({
                      ...current,
                      allowedTimeWindows: [
                        ...current.allowedTimeWindows,
                        { start: "09:00:00", end: "17:00:00" },
                      ],
                    }))}
                  >
                    <Plus /> Add window
                  </Button>
                </div>
              ) : (
                <div className="rounded-lg border border-dashed border-border px-3 py-2.5 text-xs text-muted-foreground">
                  All hours on the selected days are allowed.
                </div>
              )}
            </Field>
          </PolicySection>

          <PolicySection icon={<CalendarDays />} title="Exceptions">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <Field label="Exact dates" info="Specific dates in YYYY-MM-DD format, separated by commas.">
                <Input
                  value={form.excludedDates.join(", ")}
                  placeholder="YYYY-MM-DD, YYYY-MM-DD"
                  onChange={(event) => setList("excludedDates", event.target.value)}
                />
              </Field>
              <Field label="Every year" info="Recurring dates in MM-DD format, separated by commas.">
                <Input
                  value={form.excludedAnnualDates.map((item) =>
                    `${String(item.month).padStart(2, "0")}-${String(item.day).padStart(2, "0")}`,
                  ).join(", ")}
                  placeholder="MM-DD, MM-DD"
                  onChange={(event) => setForm({
                    ...form,
                    excludedAnnualDates: event.target.value
                      .split(",")
                      .map((item) => item.trim())
                      .filter(Boolean)
                      .map((item) => {
                        const [month, day] = item.split("-").map(Number);
                        return { month, day };
                      }),
                  })}
                />
              </Field>
            </div>
            <Field
              label="Date providers"
              info="Local-date providers registered by this host. Register the same names on every scheduler node."
            >
              <DateProviderPicker
                value={form.dateProviderNames}
                providers={dateProviders.data?.names ?? []}
                isLoading={dateProviders.isLoading}
                isError={dateProviders.isError}
                onRetry={() => void dateProviders.refetch()}
                onChange={(dateProviderNames) => setForm((current) => ({
                  ...current,
                  dateProviderNames,
                }))}
              />
            </Field>
          </PolicySection>

          <PolicySection icon={<Layers3 />} title="Combine policies">
            <div className="grid gap-2 text-[11px] leading-relaxed text-muted-foreground sm:grid-cols-2">
              <p className="flex gap-2 rounded-lg border border-status-healthy/25 bg-status-healthy/[0.05] px-3 py-2">
                <Check className="mt-0.5 size-3.5 shrink-0 text-status-healthy" />
                <span><strong className="font-semibold text-status-healthy">Require</strong>: a time is allowed only when that policy allows it too.</span>
              </p>
              <p className="flex gap-2 rounded-lg border border-status-error/25 bg-status-error/[0.05] px-3 py-2">
                <Ban className="mt-0.5 size-3.5 shrink-0 text-status-error" />
                <span><strong className="font-semibold text-status-error">Exclude</strong>: a time is blocked whenever that policy allows it.</span>
              </p>
            </div>
            {composablePolicies.length > 0 ? (
              <div className="space-y-2">
                <p className="text-[11px] text-muted-foreground">
                  {form.requiredPolicyNames.length} required · {form.excludedWhenPolicyAllowsNames.length} excluded
                </p>
                {composablePolicies.map((item) => {
                  const mode: CombineMode = form.requiredPolicyNames.includes(item.name)
                    ? "require"
                    : form.excludedWhenPolicyAllowsNames.includes(item.name)
                      ? "exclude"
                      : "none";
                  return (
                    <CombinePolicyRow
                      key={item.id}
                      policy={item}
                      mode={mode}
                      onChange={(next) => setForm((current) => ({
                        ...current,
                        requiredPolicyNames: next === "require"
                          ? [...current.requiredPolicyNames.filter((name) => name !== item.name), item.name]
                          : current.requiredPolicyNames.filter((name) => name !== item.name),
                        excludedWhenPolicyAllowsNames: next === "exclude"
                          ? [...current.excludedWhenPolicyAllowsNames.filter((name) => name !== item.name), item.name]
                          : current.excludedWhenPolicyAllowsNames.filter((name) => name !== item.name),
                      }))}
                    />
                  );
                })}
              </div>
            ) : (
              <div className="rounded-lg border border-dashed border-border px-3 py-2.5 text-xs text-muted-foreground">
                No other policies are available.
              </div>
            )}
          </PolicySection>
        </div>

        <DialogFooter className="m-0 shrink-0 rounded-none rounded-b-xl border-t border-border/70 bg-muted/30 px-6 py-4">
          {policy && usage.data ? (
            <p className="mr-auto self-center text-[11px] text-muted-foreground">
              Used by {usage.data.timeTickerCount} time ticker
              {usage.data.timeTickerCount === 1 ? "" : "s"} and{" "}
              {usage.data.cronTickerCount} cron ticker
              {usage.data.cronTickerCount === 1 ? "" : "s"}.
            </p>
          ) : null}
          <Button variant="outline" onClick={onClose}>Cancel</Button>
          <Button onClick={submit} disabled={save.isPending}>
            {save.isPending ? "Saving…" : policy ? "Save changes" : "Create policy"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

type CombineMode = "none" | "require" | "exclude";

const combineOptions: { mode: CombineMode; label: string }[] = [
  { mode: "none", label: "Not used" },
  { mode: "require", label: "Require" },
  { mode: "exclude", label: "Exclude" },
];

function CombinePolicyRow({
  policy,
  mode,
  onChange,
}: {
  policy: SchedulePolicy;
  mode: CombineMode;
  onChange: (mode: CombineMode) => void;
}) {
  return (
    <div
      className={cn(
        "flex flex-col gap-2.5 rounded-lg border border-l-4 px-3 py-2.5 transition-colors sm:flex-row sm:items-center sm:justify-between",
        mode === "require" && "border-status-healthy/40 border-l-status-healthy bg-status-healthy/[0.06]",
        mode === "exclude" && "border-status-error/40 border-l-status-error bg-status-error/[0.06]",
        mode === "none" && "border-border/70 border-l-border",
      )}
    >
      <div className="min-w-0">
        <p className="truncate text-xs font-medium" title={policy.name}>{policy.name}</p>
        <p
          className={cn(
            "mt-0.5 truncate text-[11px]",
            mode === "require" ? "text-status-healthy" : mode === "exclude" ? "text-status-error" : "text-muted-foreground",
          )}
        >
          {mode === "require"
            ? "Must also allow the time"
            : mode === "exclude"
              ? "Blocks times it allows"
              : policy.description || policy.timeZoneId}
        </p>
      </div>
      <div
        role="radiogroup"
        aria-label={`How ${policy.name} combines with this policy`}
        className="inline-flex shrink-0 rounded-lg border border-border bg-muted/40 p-0.5"
      >
        {combineOptions.map((option) => {
          const active = option.mode === mode;
          return (
            <button
              key={option.mode}
              type="button"
              role="radio"
              aria-checked={active}
              onClick={() => onChange(option.mode)}
              className={cn(
                "inline-flex h-7 items-center gap-1 rounded-md px-2.5 text-[11px] font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                !active && "text-muted-foreground hover:bg-background hover:text-foreground",
                active && option.mode === "none" && "bg-background text-foreground shadow-sm",
                active && option.mode === "require" && "bg-status-healthy text-background shadow-sm",
                active && option.mode === "exclude" && "bg-status-error text-background shadow-sm",
              )}
            >
              {option.mode === "require" ? <Check className="size-3" /> : null}
              {option.mode === "exclude" ? <Ban className="size-3" /> : null}
              {option.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}

function DateProviderPicker({
  value,
  providers,
  isLoading,
  isError,
  onRetry,
  onChange,
}: {
  value: string[];
  providers: string[];
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  onChange: (value: string[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const registered = uniqueNames(providers);
  const unavailable = uniqueNames(value).filter((selected) =>
    !registered.some((provider) => sameName(provider, selected)),
  );

  function isSelected(name: string) {
    return value.some((selected) => sameName(selected, name));
  }

  function toggle(name: string) {
    onChange(isSelected(name)
      ? value.filter((selected) => !sameName(selected, name))
      : [...value, name]);
  }

  const summary = isLoading
    ? "Loading providers…"
    : value.length === 0
      ? registered.length === 0
        ? "No providers registered"
        : "Select date providers"
      : value.length === 1
        ? value[0]
        : `${value.length} providers selected`;

  return (
    <div className="space-y-1.5">
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <Button
            type="button"
            variant="outline"
            role="combobox"
            aria-expanded={open}
            className="h-9 w-full justify-between bg-surface-0 px-3 text-xs font-normal"
          >
            <span className={value.length === 0 ? "truncate text-muted-foreground" : "truncate"}>
              {summary}
            </span>
            {isLoading ? (
              <LoaderCircle className="size-3.5 shrink-0 animate-spin text-muted-foreground" />
            ) : (
              <ChevronsUpDown className="size-3.5 shrink-0 text-muted-foreground" />
            )}
          </Button>
        </PopoverTrigger>
        <PopoverContent
          align="start"
          side="bottom"
          sideOffset={4}
          collisionPadding={12}
          className="w-(--radix-popover-trigger-width) min-w-72 p-0"
          onEscapeKeyDown={(event) => event.stopPropagation()}
        >
          <Command loop>
            <CommandInput placeholder="Search registered providers…" className="text-xs" />
            <CommandList>
              {isLoading ? (
                <div className="flex items-center gap-2 px-3 py-4 text-xs text-muted-foreground">
                  <LoaderCircle className="size-3.5 animate-spin" />
                  Loading providers…
                </div>
              ) : isError ? (
                <div className="space-y-2 px-3 py-3 text-xs text-muted-foreground">
                  <p>Registered providers could not be loaded.</p>
                  <Button type="button" size="xs" variant="outline" onClick={onRetry}>
                    <RefreshCw className="size-3" />
                    Retry
                  </Button>
                </div>
              ) : (
                <>
                  <CommandEmpty>No matching date provider.</CommandEmpty>
                  {registered.length > 0 ? (
                    <CommandGroup heading="Registered on this host">
                      {registered.map((name) => (
                        <CommandItem
                          key={name.toLocaleLowerCase()}
                          value={name}
                          data-checked={isSelected(name)}
                          onSelect={() => toggle(name)}
                          className="cursor-pointer text-xs"
                        >
                          <span className="min-w-0 truncate">{name}</span>
                        </CommandItem>
                      ))}
                    </CommandGroup>
                  ) : null}
                  {unavailable.length > 0 ? (
                    <CommandGroup heading="Unavailable on this host">
                      {unavailable.map((name) => (
                        <CommandItem
                          key={name.toLocaleLowerCase()}
                          value={name}
                          data-checked
                          onSelect={() => toggle(name)}
                          className="cursor-pointer text-xs text-muted-foreground"
                        >
                          <span className="min-w-0 truncate">{name}</span>
                        </CommandItem>
                      ))}
                    </CommandGroup>
                  ) : null}
                </>
              )}
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
      {unavailable.length > 0 ? (
        <p className="text-[10px] text-amber-600 dark:text-amber-400">
          {unavailable.join(", ")} {unavailable.length === 1 ? "is" : "are"} saved but unavailable on this host.
        </p>
      ) : null}
    </div>
  );
}

function sameName(left: string, right: string) {
  return left.localeCompare(right, undefined, { sensitivity: "accent" }) === 0;
}

function uniqueNames(values: string[]) {
  const names = new Map<string, string>();
  for (const value of values) {
    const trimmed = value.trim();
    if (trimmed) names.set(trimmed.toLocaleLowerCase(), trimmed);
  }
  return [...names.values()].sort((left, right) =>
    left.localeCompare(right, undefined, { sensitivity: "base" }),
  );
}

function Field({
  label,
  info,
  labelEnd,
  required,
  children,
}: {
  label: string;
  info?: ReactNode;
  labelEnd?: ReactNode;
  required?: boolean;
  children: ReactNode;
}) {
  return (
    <div className="min-w-0 space-y-1.5">
      <div className="flex min-h-5 items-center justify-between gap-2">
        <Label className="text-[10px] uppercase tracking-wider text-muted-foreground">
          {label}{required ? <span className="ml-0.5 text-destructive">*</span> : null}
        </Label>
        <div className="flex items-center gap-1.5">
          {labelEnd}
          {info ? <FieldInfo>{info}</FieldInfo> : null}
        </div>
      </div>
      {children}
    </div>
  );
}

function PolicySection({
  icon,
  title,
  children,
}: {
  icon: ReactNode;
  title: string;
  children: ReactNode;
}) {
  return (
    <section className="space-y-3.5">
      <div className="flex items-center gap-2 text-[10px] font-semibold uppercase tracking-wider text-foreground/75 [&_svg]:size-3.5 [&_svg]:text-primary">
        {icon}
        <h3>{title}</h3>
      </div>
      <div className="space-y-4">{children}</div>
    </section>
  );
}
