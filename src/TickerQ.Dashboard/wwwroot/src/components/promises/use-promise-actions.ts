import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";

import { dashboardApi } from "@/services/dashboard-api";
import { qk } from "@/services/hooks";
import type { PromiseSettingsChange, PromiseSnapshot } from "@/services/promise-types";
import { countOf, nouns } from "@/lib/promise-list";

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Unknown error";
}

/** Check, fix, dismiss and settings for one promise. Each answer carries the fresh promise, which replaces the cached one. */
export function usePromiseActions(promise: PromiseSnapshot | null) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const id = promise?.id ?? "";
  const words = nouns(promise?.rowType);

  const store = async (next: PromiseSnapshot) => {
    queryClient.setQueryData<PromiseSnapshot[]>(qk.promises, list => list?.map(p => p.id === next.id ? next : p));
    await queryClient.invalidateQueries({ queryKey: qk.promises });
  };
  const fail = (title: string) => (error: unknown) => toast.error(title, { description: errorMessage(error) });
  const openJob = (jobId: string) => ({ label: "Open job", onClick: () => navigate(`/time-tickers?ticker=${jobId}`) });

  const check = useMutation({
    mutationFn: () => dashboardApi.checkPromise(id),
    onSuccess: async snapshot => {
      if (snapshot.error) toast.error("Check failed", { description: snapshot.error });
      else if (snapshot.counts.needsReview === 0) toast.success("Checked: nothing needs review");
      else toast.success(`Checked: ${countOf(snapshot.counts.needsReview, words)} need${snapshot.counts.needsReview === 1 ? "s" : ""} review`);
      await store(snapshot);
    },
    onError: fail("Could not check"),
  });

  const settings = useMutation({
    mutationFn: (change: PromiseSettingsChange) => dashboardApi.updatePromiseSettings(id, change),
    onSuccess: async snapshot => {
      toast.success("Settings saved");
      await store(snapshot);
    },
    onError: fail("Could not save the setting"),
  });

  const fix = useMutation({
    mutationFn: (findingId: string) => dashboardApi.fixPromiseFinding(id, findingId),
    onSuccess: async result => {
      const done = { RunNow: "Job started", CancelExtra: "Extra job cancelled", NewJob: "Job scheduled" }[result.fix];
      toast.success(done, { action: openJob(result.jobId) });
      await store(result.promise);
    },
    onError: fail("Could not fix this row"),
  });

  const fixAll = useMutation({
    mutationFn: () => dashboardApi.fixAllPromiseFindings(id),
    onSuccess: async result => {
      const parts = [result.failed > 0 && `${result.failed} could not be fixed`, result.remaining > 0 && `${result.remaining} left — run it again`]
        .filter(Boolean).join(" · ");
      (result.failed > 0 ? toast.warning : toast.success)(`Fixed ${countOf(result.fixed, words)}`, parts ? { description: parts } : undefined);
      await store(result.promise);
    },
    onError: fail("Could not run the fixes"),
  });

  const dismiss = useMutation({
    mutationFn: ({ findingId, reason }: { findingId: string; reason?: string }) =>
      dashboardApi.dismissPromiseFinding(id, findingId, reason),
    onSuccess: async snapshot => {
      toast.success("Dismissed");
      await store(snapshot);
    },
    onError: fail("Could not dismiss"),
  });

  return { check, settings, fix, fixAll, dismiss };
}

export type PromiseActions = ReturnType<typeof usePromiseActions>;
