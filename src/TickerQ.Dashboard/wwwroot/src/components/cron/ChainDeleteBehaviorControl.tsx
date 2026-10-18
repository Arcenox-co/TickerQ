import { useState } from "react";
import { ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { isReadOnly } from "@/lib/runtime-config";
import { useSetChainDeleteBehavior } from "@/services/hooks";
import type { ChainDeleteBehavior, TimeTickerFlatDto } from "@/services/api-types";

export function ChainDeleteBehaviorControl({ ticker }: { ticker: TimeTickerFlatDto }) {
  const current = ticker.deleteBehavior ?? "Cascade";
  const [open, setOpen] = useState(false);
  const [behavior, setBehavior] = useState<ChainDeleteBehavior>(current);
  const [error, setError] = useState<string | null>(null);
  const mutation = useSetChainDeleteBehavior();
  const readOnly = isReadOnly();

  async function save() {
    if (readOnly || mutation.isPending || behavior === current) return;
    setError(null);
    try {
      await mutation.mutateAsync({ id: ticker.id, behavior });
      toast.success("Chain deletion behavior updated");
      setOpen(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not update deletion behavior. Please try again.");
    }
  }

  return (
    <>
      <Button variant="outline" size="sm" className="h-8 gap-1.5 text-xs" disabled={readOnly}
        aria-label={`Chain deletion behavior: ${current}`}
        onClick={() => { setBehavior(current); setError(null); setOpen(true); }}>
        <ShieldCheck className="h-3.5 w-3.5" /> Deletion: {current}
      </Button>
      <ConfirmDialog open={open} onOpenChange={setOpen} title="Chain deletion behavior"
        description="Choose when the root and its descendants can be deleted."
        confirmLabel="Save behavior" isPending={mutation.isPending}
        confirmDisabled={readOnly || behavior === current} onConfirm={save}>
        <div className="space-y-3">
          <p className="break-all font-mono text-xs">{ticker.functionName}</p>
          <Select value={behavior} onValueChange={(value) => setBehavior(value as ChainDeleteBehavior)} disabled={mutation.isPending}>
            <SelectTrigger aria-label="Chain deletion behavior" className="w-full"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="Cascade">Cascade — delete the whole chain</SelectItem>
              <SelectItem value="Restrict">Restrict — wait for children to finish</SelectItem>
            </SelectContent>
          </Select>
          <p className="text-xs leading-relaxed text-muted-foreground">
            {behavior === "Restrict"
              ? "Blocks deletion and replacement until every descendant is Done, DueDone, Failed, Cancelled, or Skipped. The restriction then lifts automatically. Retention follows its configured ages and safety checks. Execution and cancellation still work normally."
              : "Deleting the root also deletes its descendants. Configured retention cleanup can remove the whole chain when it is eligible. Running or actively owned chains remain protected."}
          </p>
          {error && <p role="alert" className="text-xs text-status-error">{error}</p>}
        </div>
      </ConfirmDialog>
    </>
  );
}
