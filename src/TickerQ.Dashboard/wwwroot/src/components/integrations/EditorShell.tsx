import {
  useEffect,
  useId,
  useState,
  type FormEvent,
  type ReactNode,
} from "react";
import { X, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
} from "@/components/ui/sheet";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";

export function EditorShell({
  title,
  description,
  icon,
  dirty,
  busy,
  error,
  saveLabel,
  onClose,
  onSubmit,
  children,
}: {
  title: string;
  description: string;
  icon: ReactNode;
  dirty: boolean;
  busy: boolean;
  error: string | null;
  saveLabel: string;
  onClose: () => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  children: ReactNode;
}) {
  const formId = useId();
  const [discard, setDiscard] = useState(false);
  function close() {
    if (!busy) {
      if (dirty) setDiscard(true);
      else onClose();
    }
  }
  useEffect(() => {
    if (!dirty) return;
    const beforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", beforeUnload);
    return () => window.removeEventListener("beforeunload", beforeUnload);
  }, [dirty]);
  return (
    <>
      <Sheet
        open
        onOpenChange={(open) => {
          if (!open) close();
        }}
      >
        <SheetContent
          showCloseButton={false}
          className="gap-0 data-[side=right]:w-full data-[side=right]:sm:max-w-xl"
          onEscapeKeyDown={(event) => {
            if (busy || discard) event.preventDefault();
          }}
        >
          <SheetHeader className="flex-row items-start gap-3 border-b border-border p-5">
            {icon}
            <div className="min-w-0 flex-1 space-y-1">
              <SheetTitle className="break-words">{title}</SheetTitle>
              <SheetDescription className="text-xs leading-relaxed">
                {description}
              </SheetDescription>
            </div>
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              aria-label="Close editor"
              disabled={busy}
              onClick={close}
            >
              <X className="size-4" />
            </Button>
          </SheetHeader>
          <form
            id={formId}
            onSubmit={onSubmit}
            className="min-h-0 flex-1 overflow-y-auto p-5"
          >
            <fieldset disabled={busy} className="min-w-0 space-y-6">
              {children}
            </fieldset>
          </form>
          <div className="space-y-3 border-t border-border bg-surface-1 p-4">
            {error && (
              <p
                role="alert"
                className="rounded-md border border-status-error/20 bg-status-error/5 p-3 text-xs leading-relaxed text-status-error"
              >
                {error}
              </p>
            )}
            <div className="flex items-center justify-between gap-3">
              <span className="text-xs text-muted-foreground">
                {dirty ? "Unsaved changes" : "All changes saved"}
              </span>
              <div className="flex gap-2">
                <Button variant="outline" disabled={busy} onClick={close}>
                  Cancel
                </Button>
                <Button form={formId} type="submit" disabled={busy || !dirty}>
                  {busy && <Loader2 className="size-3.5 animate-spin" />}
                  {busy ? "Saving…" : saveLabel}
                </Button>
              </div>
            </div>
          </div>
        </SheetContent>
      </Sheet>
      <ConfirmDialog
        open={discard}
        onOpenChange={setDiscard}
        title="Discard your changes?"
        description="Your saved configuration will stay as it is."
        confirmLabel="Discard changes"
        confirmVariant="destructive"
        onConfirm={onClose}
      />
    </>
  );
}
