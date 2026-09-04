'use client'

import type { JobStatus } from '@/lib/api-client'

/**
 * The five job states from docs/API.md, in one place.
 *
 * One component so the mapping cannot drift between screens: a scene row and a
 * scene page must never disagree about what PROCESSING looks like.
 */
const LOOK: Record<JobStatus, { fg: string; bg: string; border: string; copy: string }> = {
  PENDING: {
    fg: 'text-pending',
    bg: 'bg-pending/10',
    border: 'border-pending/40',
    copy: 'Queued',
  },
  PROCESSING: {
    fg: 'text-processing',
    bg: 'bg-processing/10',
    border: 'border-processing/50',
    copy: 'Reconstructing',
  },
  READY: {
    fg: 'text-ready',
    bg: 'bg-ready/10',
    border: 'border-ready/50',
    copy: 'Ready',
  },
  FAILED: {
    fg: 'text-failed',
    bg: 'bg-failed/10',
    border: 'border-failed/50',
    copy: 'Failed',
  },
  CANCELLED: {
    fg: 'text-cancelled',
    bg: 'bg-cancelled/10',
    border: 'border-cancelled/40',
    copy: 'Cancelled',
  },
}

/** A job is only worth polling while it can still change. */
export function isLive(status: JobStatus | undefined): boolean {
  return status === 'PENDING' || status === 'PROCESSING'
}

export function StatusBadge({
  status,
  progress,
}: {
  status: JobStatus | null | undefined
  progress?: number
}) {
  if (!status) {
    return (
      <span className="notch-sm border border-edge bg-panel-raised px-2 py-0.5 font-mono text-[11px] tracking-wider text-ink-faint uppercase">
        No job
      </span>
    )
  }
  const l = LOOK[status]
  return (
    <span
      className={`notch-sm inline-flex items-center gap-1.5 border px-2 py-0.5 font-mono text-[11px] tracking-wider uppercase ${l.border} ${l.bg} ${l.fg}`}
    >
      {status === 'PROCESSING' && (
        <span className="size-1.5 animate-pulse rounded-full bg-processing" aria-hidden />
      )}
      {l.copy}
      {status === 'PROCESSING' && typeof progress === 'number' && progress > 0 && (
        <span className="tabular-nums">{progress}%</span>
      )}
    </span>
  )
}

/**
 * The FAILED detail, verbatim.
 *
 * Showing the worker's own error text is what makes debugging across three
 * cities possible; paraphrasing it throws away the only useful thing the
 * pipeline said. gpu/run_scene.py is written to fail with readable reasons
 * precisely so this box is worth reading.
 */
export function JobError({ error }: { error: string }) {
  return (
    <div className="notch-sm border border-failed/40 bg-failed/5 p-3">
      <p className="label mb-1.5 text-failed">Worker error, verbatim</p>
      <pre className="overflow-x-auto font-mono text-xs whitespace-pre-wrap text-ink">
        {error}
      </pre>
    </div>
  )
}
