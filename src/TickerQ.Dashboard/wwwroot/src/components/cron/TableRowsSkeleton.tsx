import { Skeleton } from "@/components/ui/skeleton";

/**
 * Loading-state placeholder for tabular pages (Executions, Time Tickers,
 * Cron Tickers). Renders `rows` <tr>s, each with `cols` cells holding a thin
 * pulsing Skeleton bar — matches the real tbody so layout doesn't jump when
 * the rows arrive.
 */
export function TableRowsSkeleton({
  rows = 6,
  cols,
}: {
  rows?: number;
  cols: number;
}) {
  return (
    <>
      {Array.from({ length: rows }).map((_, r) => (
        <tr key={r} className="border-b border-border/30 last:border-0">
          {Array.from({ length: cols }).map((__, c) => (
            <td key={c} className="px-4 py-2.5">
              <Skeleton className="h-3 w-[70%]" />
            </td>
          ))}
        </tr>
      ))}
    </>
  );
}
