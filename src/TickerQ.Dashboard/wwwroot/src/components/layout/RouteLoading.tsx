import { Loader2 } from "lucide-react";

export function RouteLoading() {
  return <div role="status" aria-live="polite" className="flex items-center justify-center gap-2 py-20 text-sm text-muted-foreground">
    <Loader2 aria-hidden="true" className="size-4 animate-spin" />
    Loading page…
  </div>;
}
