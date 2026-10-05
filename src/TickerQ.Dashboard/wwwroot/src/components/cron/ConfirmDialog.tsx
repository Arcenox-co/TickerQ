import { type ReactNode } from "react";

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";

/**
 * Small controlled confirm dialog. The caller owns the mutation and passes
 * `isPending` so the confirm button can show a loading state. Used for
 * side-effecting actions (run now, cancel, duplicate, delete occurrences).
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel = "Confirm",
  confirmVariant = "gradient",
  isPending = false,
  onConfirm,
  children,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: ReactNode;
  confirmLabel?: string;
  confirmVariant?: "gradient" | "destructive" | "default";
  isPending?: boolean;
  onConfirm: () => void | Promise<void>;
  children?: ReactNode;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[440px]">
        <DialogHeader>
          <DialogTitle className="text-base">{title}</DialogTitle>
          {description ? (
            <DialogDescription className="text-[12px] leading-relaxed">
              {description}
            </DialogDescription>
          ) : null}
        </DialogHeader>
        {children}
        <DialogFooter className="mt-2 flex items-center justify-end gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-8 text-xs rounded-lg"
            onClick={() => onOpenChange(false)}
            disabled={isPending}
          >
            Cancel
          </Button>
          <Button
            type="button"
            variant={confirmVariant}
            size="sm"
            className="h-8 text-xs rounded-lg"
            onClick={() => void onConfirm()}
            disabled={isPending}
            autoFocus
          >
            {isPending ? "Working…" : confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
