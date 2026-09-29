import type { Control, FieldPath, FieldValues } from "react-hook-form";
import {
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui/form";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { FieldLabel } from "./field-label";

export type SelectOption = {
  value: string;
  label: string;
  secondary?: string;
};

export type SelectFieldProps<T extends FieldValues> = {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  required?: boolean;
  placeholder?: string;
  options: readonly SelectOption[];
  triggerClassName?: string;
  disabled?: boolean;
};

export function SelectField<T extends FieldValues>({
  control,
  name,
  label,
  required,
  placeholder,
  options,
  triggerClassName,
  disabled,
}: SelectFieldProps<T>) {
  return (
    <FormField
      control={control}
      name={name}
      render={({ field }) => (
        <FormItem>
          <FieldLabel label={label} required={required} />
          <Select
            onValueChange={field.onChange}
            value={field.value ? String(field.value) : undefined}
            disabled={disabled ?? field.disabled}
          >
            <FormControl>
              {/* w-full so the form field spans its column like the Hub
                  (shadcn's default SelectTrigger is w-fit). */}
              <SelectTrigger className={cn("w-full", triggerClassName)}>
                <SelectValue placeholder={placeholder} />
              </SelectTrigger>
            </FormControl>
            <SelectContent>
              {options.map((opt) => (
                <SelectItem key={opt.value} value={opt.value}>
                  <span>{opt.label}</span>
                  {opt.secondary && (
                    <span className="ml-1.5 text-muted-foreground/50">
                      - {opt.secondary}
                    </span>
                  )}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <FormMessage />
        </FormItem>
      )}
    />
  );
}
