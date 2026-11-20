import { useEffect, useState } from "react";

import { timeAgo } from "@/lib/relative-time";
import { NowContext, useNow } from "./use-now";

/** One clock for the page, so every relative time on it moves together. */
export function NowProvider({ every = 1000, children }: { every?: number; children: React.ReactNode }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), every);
    return () => window.clearInterval(id);
  }, [every]);
  return <NowContext.Provider value={now}>{children}</NowContext.Provider>;
}

/** "5 min ago" or "in 12s", with the exact time on hover. */
export function RelativeTime({ value, formatTime, className }: {
  value: string | null | undefined;
  formatTime: (value: string | null) => string;
  className?: string;
}) {
  const now = useNow();
  if (!value) return <span className={className}>—</span>;
  return <time dateTime={value} title={formatTime(value)} className={className}>{timeAgo(value, now)}</time>;
}
