import { createContext, useContext } from "react";

/** The page's clock in epoch milliseconds, set by NowProvider; every relative time on the page reads it. */
export const NowContext = createContext(0);

export function useNow(): number {
  return useContext(NowContext);
}
