import { useEffect, useId, useState, type ReactNode } from "react";
import type {
  Control,
  ControllerRenderProps,
  FieldPath,
  FieldValues,
} from "react-hook-form";

import { FieldLabel } from "@/components/form-fields/field-label";
import { FieldInfo } from "@/components/form-fields/field-info";
import {
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";

const DURATION_MULTIPLIERS = {
  s: 1,
  m: 60,
  h: 3_600,
  d: 86_400,
} as const;

export function parseDuration(value: string): number | null {
  const trimmed = value.trim().toLowerCase();
  if (trimmed === "0") return 0;

  const match = /^(\d+)\s*([smhd])$/.exec(trimmed);
  if (!match) return null;

  const amount = Number.parseInt(match[1], 10);
  const unit = match[2] as keyof typeof DURATION_MULTIPLIERS;
  const seconds = amount * DURATION_MULTIPLIERS[unit];
  return Number.isSafeInteger(seconds) ? seconds : null;
}

export function formatDuration(seconds: number): string {
  if (seconds === 0) return "0s";
  if (seconds % 86_400 === 0) return `${seconds / 86_400}d`;
  if (seconds % 3_600 === 0) return `${seconds / 3_600}h`;
  if (seconds % 60 === 0) return `${seconds / 60}m`;
  return `${seconds}s`;
}

export type DurationFieldProps<T extends FieldValues> = {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  description?: string;
  required?: boolean;
  disabled?: boolean;
  minSeconds?: number;
  maxSeconds?: number;
  info?: ReactNode;
  labelClassName?: string;
};

export function DurationField<T extends FieldValues>({
  control,
  name,
  label,
  description,
  required,
  disabled,
  minSeconds = 0,
  maxSeconds,
  info,
  labelClassName,
}: DurationFieldProps<T>) {
  return (
    <FormField
      control={control}
      name={name}
      render={({ field }) => (
        <DurationFieldControl
          field={field}
          label={label}
          description={description}
          required={required}
          disabled={disabled}
          minSeconds={minSeconds}
          maxSeconds={maxSeconds}
          info={info}
          labelClassName={labelClassName}
        />
      )}
    />
  );
}

function DurationFieldControl<T extends FieldValues>({
  field,
  label,
  description,
  required,
  disabled,
  minSeconds,
  maxSeconds,
  info,
  labelClassName,
}: {
  field: ControllerRenderProps<T, FieldPath<T>>;
  label: string;
  description?: string;
  required?: boolean;
  disabled?: boolean;
  minSeconds: number;
  maxSeconds?: number;
  info?: ReactNode;
  labelClassName?: string;
}) {
  const inputId = `${useId()}-${String(field.name)}`;
  const seconds = Number(field.value ?? 0);
  const [draft, setDraft] = useState(() => formatDuration(seconds));
  const [localError, setLocalError] = useState<string | null>(null);

  useEffect(() => {
    if (Number.isFinite(seconds)) setDraft(formatDuration(seconds));
  }, [seconds]);

  function validate(nextDraft: string, showError: boolean) {
    const parsed = parseDuration(nextDraft);
    let error: string | null = null;

    if (parsed == null) {
      error = "Use a whole number followed by s, m, h, or d (for example, 5m).";
    } else if (parsed < minSeconds) {
      error = `Must be at least ${formatDuration(minSeconds)}.`;
    } else if (maxSeconds != null && parsed > maxSeconds) {
      error = `Must be no more than ${formatDuration(maxSeconds)}.`;
    }

    setLocalError(showError ? error : null);
    field.onChange(error ? Number.NaN : parsed);
    return error == null;
  }

  return (
    <FormItem>
      <FieldLabel
        label={label}
        required={required}
        className={labelClassName}
        htmlFor={inputId}
        labelEnd={
          info ? (
            <FieldInfo>
              <span>{info}</span>
              <span className="mt-1 block text-muted-foreground">
                Enter one value using s, m, h, or d, such as 30s, 5m, 2h, or 1d.
              </span>
            </FieldInfo>
          ) : undefined
        }
      />
      <FormControl>
        <Input
          id={inputId}
          ref={field.ref}
          name={field.name}
          type="text"
          inputMode="text"
          autoComplete="off"
          placeholder="e.g. 5m"
          className="w-full max-w-40"
          disabled={disabled ?? field.disabled}
          value={draft}
          aria-invalid={localError ? true : undefined}
          onBlur={() => {
            field.onBlur();
            if (validate(draft, true)) {
              const parsed = parseDuration(draft);
              if (parsed != null) setDraft(formatDuration(parsed));
            }
          }}
          onChange={(event) => {
            const nextDraft = event.target.value;
            setDraft(nextDraft);
            validate(nextDraft, false);
          }}
        />
      </FormControl>
      {description && (
        <p className="text-[11px] leading-relaxed text-muted-foreground">
          {description}
        </p>
      )}
      {localError && (
        <p className="text-[11px] leading-relaxed text-destructive">
          {localError}
        </p>
      )}
      {!localError && <FormMessage />}
    </FormItem>
  );
}
