import { useId, type ComponentProps } from "react";
import type { Control, FieldPath, FieldValues } from "react-hook-form";
import {
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { FieldLabel } from "./field-label";

export type NumberFieldProps<T extends FieldValues> = {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  description?: string;
  required?: boolean;
  allowNull?: boolean;
} & Omit<
  ComponentProps<typeof Input>,
  "name" | "value" | "defaultValue" | "onChange" | "onBlur" | "ref" | "type"
>;

export function NumberField<T extends FieldValues>({
  control,
  name,
  label,
  description,
  required,
  allowNull = false,
  ...inputProps
}: NumberFieldProps<T>) {
  const { id, disabled, className: inputClassName, ...restInput } = inputProps;
  const autoId = useId();
  const inputId = id ?? `${autoId}-${String(name)}`;

  return (
    <FormField
      control={control}
      name={name}
      render={({ field }) => (
        // No spacing override: FormItem's own `grid gap-2` must stay the single
        // source of label→control spacing so fields sharing a two-column row
        // keep their inputs vertically aligned.
        <FormItem>
          <FieldLabel label={label} required={required} htmlFor={inputId} />
          <FormControl>
            <Input
              id={inputId}
              ref={field.ref}
              name={field.name}
              type="number"
              className={inputClassName}
              disabled={disabled}
              value={
                allowNull
                  ? field.value == null
                    ? ""
                    : (field.value as number)
                  : (field.value ?? "")
              }
              onBlur={field.onBlur}
              onChange={(e) => {
                const v = e.target.value;
                if (allowNull && v === "") {
                  field.onChange(null);
                  return;
                }
                const n = Number.parseInt(v, 10);
                if (allowNull && v !== "" && Number.isNaN(n)) {
                  field.onChange(null);
                  return;
                }
                field.onChange(Number.isNaN(n) ? 0 : n);
              }}
              {...restInput}
            />
          </FormControl>
          {description && (
            <p className="text-xs text-muted-foreground">{description}</p>
          )}
          <FormMessage />
        </FormItem>
      )}
    />
  );
}
