import type { ComponentProps, ReactNode } from "react";
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
import { FieldInfo } from "./field-info";

export type TextFieldProps<T extends FieldValues> = {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  required?: boolean;
  description?: string;
  info?: ReactNode;
  labelClassName?: string;
} & Omit<ComponentProps<typeof Input>, "name" | "value" | "defaultValue" | "onChange">;

export function TextField<T extends FieldValues>({
  control,
  name,
  label,
  required,
  description,
  info,
  labelClassName,
  ...inputProps
}: TextFieldProps<T>) {
  return (
    <FormField
      control={control}
      name={name}
      render={({ field }) => (
        <FormItem>
          {label && (
            <FieldLabel
              label={label}
              required={required}
              className={labelClassName}
              htmlFor={inputProps.id}
              labelEnd={info ? <FieldInfo>{info}</FieldInfo> : undefined}
            />
          )}
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
