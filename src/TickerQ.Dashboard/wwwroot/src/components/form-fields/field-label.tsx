import type { ReactNode } from "react";
import { FormLabel } from "@/components/ui/form";
import { cn } from "@/lib/utils";

export const COMPACT_FIELD_LABEL_CLASS =
  "text-[10px] uppercase tracking-wider text-muted-foreground";

export function FieldLabel({
  label,
  required,
  className,
  htmlFor,
  labelEnd,
}: {
  label: string;
  required?: boolean;
  className?: string;
  htmlFor?: string;
  labelEnd?: ReactNode;
}) {
  const labelNode = (
    <FormLabel className={cn(className)} htmlFor={htmlFor}>
      {label}
      {required && (
        <span className="text-destructive ml-0.5" aria-hidden>
          *
        </span>
      )}
    </FormLabel>
  );
  if (!labelEnd) return labelNode;
  return (
    <div className="flex items-center justify-between gap-2">
      {labelNode}
      {labelEnd}
    </div>
  );
}
