import { JobStatus } from '@prisma/client'

/**
 * The job state machine from docs/API.md, in one place.
 *
 *   PENDING -> PROCESSING -> READY
 *      |            |
 *      +----------> FAILED     (with error text)
 *                   CANCELLED  (user asked to stop)
 *
 * One direction only. READY, FAILED and CANCELLED are terminal: a job that
 * walks backwards out of a terminal state is how "jobs run twice" and "status
 * flaps" bugs start, so the transition table is enforced rather than assumed.
 */

const ALLOWED: Record<JobStatus, readonly JobStatus[]> = {
  [JobStatus.PENDING]: [JobStatus.PROCESSING, JobStatus.FAILED, JobStatus.CANCELLED],
  [JobStatus.PROCESSING]: [JobStatus.READY, JobStatus.FAILED, JobStatus.CANCELLED],
  [JobStatus.READY]: [],
  [JobStatus.FAILED]: [],
  [JobStatus.CANCELLED]: [],
}

export function canTransition(from: JobStatus, to: JobStatus): boolean {
  return ALLOWED[from].includes(to)
}

export function isTerminal(status: JobStatus): boolean {
  return ALLOWED[status].length === 0
}

/** Human-readable reason, for the error text a rejected transition returns. */
export function transitionError(from: JobStatus, to: JobStatus): string {
  if (from === to) return `job is already ${from}`
  if (isTerminal(from)) return `job is in terminal state ${from} and cannot move to ${to}`
  return `illegal transition ${from} -> ${to}`
}
