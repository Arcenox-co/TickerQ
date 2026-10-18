import { type ReactNode, useRef } from "react";

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
  dismissLabel = "Cancel",
  confirmVariant = "gradient",
  isPending = false,
  confirmDisabled = false,
  onConfirm,
  children,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: ReactNode;
  confirmLabel?: string;
  dismissLabel?: string;
  confirmVariant?: "gradient" | "destructive" | "default";
  isPending?: boolean;
  confirmDisabled?: boolean;
  onConfirm: () => void | Promise<void>;
  children?: ReactNode;
}) {
  const confirming = useRef(false);

  function handleOpenChange(next: boolean) {
    if (!next && (isPending || confirming.current)) return;
    onOpenChange(next);
  }

  async function handleConfirm() {
    if (isPending || confirmDisabled || confirming.current) return;
    confirming.current = true;
    try {
      await onConfirm();
    } finally {
      confirming.current = false;
    }
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent
        className="sm:max-w-[440px]"
        onEscapeKeyDown={(event) => {
          // Escape belongs to this dialog, not the detail panel behind it.
          event.stopPropagation();
          if (isPending || confirming.current) event.preventDefault();
        }}
      >
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
            onClick={() => handleOpenChange(false)}
            disabled={isPending}
            autoFocus={confirmVariant === "destructive"}
          >
            {dismissLabel}
          </Button>
          <Button
            type="button"
            variant={confirmVariant}
            size="sm"
            className="h-8 text-xs rounded-lg"
            onClick={() => void handleConfirm()}
            disabled={isPending || confirmDisabled}
            autoFocus={confirmVariant !== "destructive"}
          >
            {isPending ? "Working…" : confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
