import { Code2 } from "lucide-react";

/** Marks a cron declared in code with SeedCronTickers. Code changes replace dashboard edits on the next epoch. */
export function SeededCronBadge() {
  return (
    <span
      title="Seeded from code with SeedCronTickers. Changes in code replace dashboard edits when a new reconciliation epoch activates."
      className="inline-flex shrink-0 items-center gap-1 rounded-md border border-sky-500/30 bg-sky-500/10 px-1.5 py-0.5 text-[10px] font-medium text-sky-700 dark:text-sky-300"
    >
      <Code2 aria-hidden="true" className="h-3 w-3" />
      From code
    </span>
  );
}
