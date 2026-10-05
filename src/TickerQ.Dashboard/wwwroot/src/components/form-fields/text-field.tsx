import type { ComponentProps } from "react";
import type { Control, FieldPath, FieldValues } from "react-hook-form";
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { FieldLabel } from "./field-label";

export type TextFieldProps<T extends FieldValues> = {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  required?: boolean;
  description?: string;
} & Omit<ComponentProps<typeof Input>, "name" | "value" | "defaultValue" | "onChange">;

export function TextField<T extends FieldValues>({
  control,
  name,
  label,
  required,
  description,
  ...inputProps
}: TextFieldProps<T>) {
  return (
    <FormField
      control={control}
      name={name}
      render={({ field }) => (
        <FormItem>
          {label && <FieldLabel label={label} required={required} htmlFor={inputProps.id} />}
          <FormControl>
            <Input {...field} value={field.value ?? ""} {...inputProps} />
          </FormControl>
          {description && <FormDescription>{description}</FormDescription>}
          <FormMessage />
        </FormItem>
      )}
    />
  );
}
