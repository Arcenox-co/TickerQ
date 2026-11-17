import { ChevronLeft, ChevronRight } from "lucide-react";

import { Button } from "@/components/ui/button";
import type { Page } from "@/lib/promise-list";

/** "1–10 of 37" with previous and next, matching the About page's function list. */
export function Pager<T>({ page, noun, onPage }: { page: Page<T>; noun: string; onPage: (page: number) => void }) {
  return <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-2.5">
    <p className="text-xs tabular-nums text-muted-foreground" role="status">
      {page.total === 0 ? `No ${noun}` : `${page.from}–${page.to} of ${page.total.toLocaleString()} ${noun}`}
    </p>
    {page.pages > 1 && <nav aria-label={`${noun} pages`} className="flex items-center gap-1.5">
      <Button variant="outline" size="icon" className="size-7" aria-label="Previous page" disabled={page.page === 1}
        onClick={() => onPage(page.page - 1)}><ChevronLeft className="size-3.5" /></Button>
      <span className="min-w-12 text-center text-xs tabular-nums text-muted-foreground">{page.page} / {page.pages}</span>
      <Button variant="outline" size="icon" className="size-7" aria-label="Next page" disabled={page.page === page.pages}
        onClick={() => onPage(page.page + 1)}><ChevronRight className="size-3.5" /></Button>
    </nav>}
  </div>;
}
