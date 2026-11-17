import { SlidersHorizontal } from "lucide-react";

import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import type { Nouns } from "@/lib/promise-list";
import type { PromiseSnapshot } from "@/services/promise-types";
import { PromiseRule } from "./PromiseRule";
import { PromiseSettings } from "./PromiseSettings";
import type { PromiseActions } from "./use-promise-actions";

/**
 * Everything about how a promise is set up, apart from the day-to-day work: what the dashboard can change, then the
 * rule as declared in code and the options it is checked with.
 */
export function PromiseSetup({ promise, words, actions, open, onOpenChange }: {
  promise: PromiseSnapshot;
  words: Nouns;
  actions: PromiseActions;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  return <Dialog open={open} onOpenChange={onOpenChange}>
    <DialogContent className="max-h-[calc(100dvh-2rem)] gap-5 overflow-y-auto sm:max-w-3xl">
      <DialogHeader>
        <DialogTitle className="flex items-center gap-2">
          <SlidersHorizontal className="size-4 text-muted-foreground" aria-hidden /> Setup · {promise.name}
        </DialogTitle>
        <DialogDescription>{promise.summary}</DialogDescription>
      </DialogHeader>

      <section aria-labelledby="promise-setup-settings" className="space-y-2">
        <div>
          <h3 id="promise-setup-settings" className="text-sm font-medium">Dashboard settings</h3>
          <p className="text-[11px] text-muted-foreground">
            {promise.canManage
              ? "Changes apply on the next check and override the code until reset."
              : "You can view these settings but not change them."}
          </p>
        </div>
        <PromiseSettings promise={promise} words={words} actions={actions} />
      </section>

      <PromiseRule promise={promise} words={words} />
    </DialogContent>
  </Dialog>;
}
