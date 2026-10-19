import type { TimeTickerFlatDto } from "../../services/api-types";

type ChainJob = Pick<TimeTickerFlatDto, "id" | "parentId" | "lockHolder">;

/** Child jobs may share an ancestor's ownership instead of storing a separate holder. */
export function resolveChainLockHolder(jobId: string, jobs: readonly ChainJob[]) {
  const byId = new Map(jobs.map(job => [job.id, job]));
  const visited = new Set<string>();
  let job = byId.get(jobId);
  while (job && !visited.has(job.id)) {
    visited.add(job.id);
    if (job.lockHolder) return { value: job.lockHolder, inherited: job.id !== jobId };
    job = job.parentId ? byId.get(job.parentId) : undefined;
  }
  return { value: null, inherited: false };
}
