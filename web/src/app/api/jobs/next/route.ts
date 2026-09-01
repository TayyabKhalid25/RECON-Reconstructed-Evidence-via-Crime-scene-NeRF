import { z } from 'zod'
import { prisma } from '@/lib/prisma'
import { apiError, apiOk, withErrors } from '@/lib/api'
import { appendAudit } from '@/lib/custody'
import { isAuthorisedWorker } from '@/lib/auth'

/**
 * GET /api/jobs/next — a worker claims one PENDING job.
 *
 * With BullMQ the real worker takes jobs off Redis, but docs/API.md keeps this
 * endpoint for manual testing and for recovering a stuck queue.
 */

const Query = z.object({
  worker: z.string().min(1).max(64).optional(),
})

export const GET = withErrors(async (req: Request) => {
  if (!isAuthorisedWorker(req)) {
    return apiError('UNAUTHORIZED', 'Missing or invalid worker token')
  }

  const url = new URL(req.url)
  const parsed = Query.safeParse({ worker: url.searchParams.get('worker') ?? undefined })
  const workerName = parsed.success ? parsed.data.worker : undefined

  // Three CUDA machines run this same worker from week 3. The claim has to be
  // atomic or two of them train the same scene: SKIP LOCKED lets each caller
  // take a different row instead of blocking on the same one.
  const claimed = await prisma.$queryRaw<
    { id: string; sceneId: string }[]
  >`
    UPDATE "Job"
       SET status = 'PROCESSING',
           "startedAt" = NOW(),
           "workerName" = ${workerName ?? null}
     WHERE id = (
       SELECT id FROM "Job"
        WHERE status = 'PENDING'
        ORDER BY "createdAt" ASC
        FOR UPDATE SKIP LOCKED
        LIMIT 1
     )
    RETURNING id, "sceneId"
  `

  if (claimed.length === 0) {
    // Not an error: an idle queue is the normal case.
    return apiOk({ job: null })
  }

  const job = claimed[0]

  const sourceAsset = await prisma.asset.findFirst({
    where: { sceneId: job.sceneId, kind: 'SOURCE_VIDEO' },
    orderBy: { createdAt: 'desc' },
    select: { storageKey: true, sha256: true },
  })

  await appendAudit({
    userId: null,
    action: `job.claim${workerName ? `:${workerName}` : ''}`,
    targetType: 'Job',
    targetId: job.id,
  })

  return apiOk({
    job: {
      jobId: job.id,
      sceneId: job.sceneId,
      sourceKey: sourceAsset?.storageKey ?? null,
      sourceSha256: sourceAsset?.sha256 ?? null,
    },
  })
})
