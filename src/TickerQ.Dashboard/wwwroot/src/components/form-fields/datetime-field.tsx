import type { ComponentProps } from "react";
import type { Control, FieldPath, FieldValues } from "react-hook-form";
import { X } from "lucide-react";
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { FieldLabel } from "./field-label";

/** The current local time formatted as a datetime-local value (with seconds). */
export function nowAsDatetimeLocal(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return (
    `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}` +
    `T${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
  );
}

export type DateTimeFieldProps<T extends FieldValues> = {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  required?: boolean;
  description?: string;
} & Omit<
  ComponentProps<typeof Input>,
  "name" | "value" | "defaultValue" | "onChange" | "type"
>;

/**
 * datetime-local form field for nullable schedule times. Focusing an empty
 * field auto-fills the current local time (a sane starting point to adjust),
 * and the ✕ button clears it back to empty — which callers treat as null.
 */
export function DateTimeField<T extends FieldValues>({
  control,
  name,
  label,
  required,
  description,
  ...inputProps
}: DateTimeFieldProps<T>) {
  return (
    <FormField
      control={control}
      name={name}
      render={({ field }) => (
        <FormItem>
          {label && <FieldLabel label={label} required={required} htmlFor={inputProps.id} />}
          <FormControl>
            <div className="relative">
              <Input
                {...field}
                value={field.value ?? ""}
                type="datetime-local"
                onFocus={(e) => {
                  if (!field.value) field.onChange(nowAsDatetimeLocal());
                  inputProps.onFocus?.(e);
                }}
                {...inputProps}
                className={`pr-14 ${inputProps.className ?? ""}`}
              />
              {field.value && (
                <button
                  type="button"
                  aria-label="Clear"
                  title="Clear (fire at submit time)"
                  // preventDefault on mousedown so the click doesn't move focus
                  // into the input — that would immediately auto-fill again.
                  // right-8 keeps it clear of the native calendar picker icon.
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => field.onChange("")}
                  className="absolute right-8 top-1/2 -translate-y-1/2 rounded p-0.5 text-muted-foreground/60 hover:text-foreground hover:bg-surface-1"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              )}
            </div>
          </FormControl>
          {description && <FormDescription>{description}</FormDescription>}
          <FormMessage />
        </FormItem>
      )}
    />
  );
}
