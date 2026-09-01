import { z } from 'zod'
import { JobStatus } from '@prisma/client'
import { prisma } from '@/lib/prisma'
import { apiError, apiOk, withErrors, zodError } from '@/lib/api'
import { appendAudit } from '@/lib/custody'
import { isAuthorisedWorker } from '@/lib/auth'
import { canTransition, transitionError } from '@/lib/job-state'

/** PATCH /api/jobs/:id/status — { status, error?, progress? }. docs/API.md. */

const StatusUpdate = z
  .object({
    status: z.nativeEnum(JobStatus),
    error: z.string().max(4000).optional(),
    progress: z.number().int().min(0).max(100).optional(),
  })
  .refine((v) => v.status !== JobStatus.FAILED || (v.error && v.error.trim().length > 0), {
    // The contract is explicit: anything unexpected ends at FAILED *with
    // readable error text*, because a job that silently vanishes is the worst
    // thing to debug.
    message: 'FAILED requires readable error text',
    path: ['error'],
  })

export const PATCH = withErrors(
  async (req: Request, ctx: RouteContext<'/api/jobs/[id]/status'>) => {
    if (!isAuthorisedWorker(req)) {
      return apiError('UNAUTHORIZED', 'Missing or invalid worker token')
    }

    // Next 16: params is a promise, synchronous access was removed.
    const { id } = await ctx.params

    const parsed = StatusUpdate.safeParse(await req.json().catch(() => null))
    if (!parsed.success) return zodError(parsed.error)
    const { status, error, progress } = parsed.data

    const existing = await prisma.job.findUnique({
      where: { id },
      select: { status: true },
    })
    if (!existing) return apiError('NOT_FOUND', 'Job not found')

    if (!canTransition(existing.status, status)) {
      return apiError('CONFLICT', transitionError(existing.status, status))
    }

    const terminal =
      status === JobStatus.READY || status === JobStatus.FAILED || status === JobStatus.CANCELLED

    const updated = await prisma.$transaction(async (tx) => {
      // Re-check inside the transaction so two concurrent updates cannot both
      // pass the check above and race the same job forward.
      const fresh = await tx.job.updateMany({
        where: { id, status: existing.status },
        data: {
          status,
          error: status === JobStatus.FAILED ? (error ?? null) : null,
          ...(progress === undefined ? {} : { progress }),
          ...(status === JobStatus.READY ? { progress: 100 } : {}),
          ...(terminal ? { finishedAt: new Date() } : {}),
        },
      })
      if (fresh.count === 0) return null

      await appendAudit(
        {
          userId: null,
          action: `job.status:${status}`,
          targetType: 'Job',
          targetId: id,
        },
        tx
      )

      return tx.job.findUnique({
        where: { id },
        select: { id: true, status: true, progress: true, error: true, workerName: true },
      })
    })

    if (!updated) {
      return apiError('CONFLICT', 'Job status changed concurrently, retry')
    }

    return apiOk({ job: updated })
  }
)
