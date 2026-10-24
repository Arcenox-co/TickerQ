import type { ComponentProps, ReactNode } from "react";
import type { Control, FieldPath, FieldValues } from "react-hook-form";
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui/form";
import { Textarea } from "@/components/ui/textarea";
import { FieldLabel } from "./field-label";
import { FieldInfo } from "./field-info";

export type TextareaFieldProps<T extends FieldValues> = {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  required?: boolean;
  description?: string;
  info?: ReactNode;
  labelClassName?: string;
} & Omit<
  ComponentProps<typeof Textarea>,
  "name" | "value" | "defaultValue" | "onChange"
>;

export function TextareaField<T extends FieldValues>({
  control,
  name,
  label,
  required,
  description,
  info,
  labelClassName,
  ...textareaProps
}: TextareaFieldProps<T>) {
  return (
    <FormField
      control={control}
      name={name}
      render={({ field }) => (
        <FormItem>
          <FieldLabel
            label={label}
            required={required}
            className={labelClassName}
            labelEnd={info ? <FieldInfo>{info}</FieldInfo> : undefined}
          />
          <FormControl>
            <Textarea {...field} value={field.value ?? ""} {...textareaProps} />
          </FormControl>
          {description && <FormDescription>{description}</FormDescription>}
          <FormMessage />
        </FormItem>
      )}
    />
  );
}
