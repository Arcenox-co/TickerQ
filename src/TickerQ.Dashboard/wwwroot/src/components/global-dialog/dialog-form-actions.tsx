import type { ComponentProps } from "react";
import { cn } from "@/lib/utils";

export function DialogFormActions({
  className,
  children,
  ...props
}: ComponentProps<"footer">) {
  return (
    <footer className={cn("mt-4 w-full", className)} {...props}>
      <div className="flex flex-row flex-wrap items-center justify-end gap-2 [&_button]:w-auto [&_button]:max-w-none [&_button]:flex-none">
        {children}
      </div>
    </footer>
  );
}
