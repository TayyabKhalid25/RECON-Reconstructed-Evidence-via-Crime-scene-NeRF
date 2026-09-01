import { z } from 'zod'
import { prisma } from '@/lib/prisma'
import { apiError, apiOk, withErrors, zodError } from '@/lib/api'
import { appendAudit } from '@/lib/custody'
import { enqueueReconstruction } from '@/lib/queue'
import { sessionFromRequest } from '@/lib/auth'

/** POST /api/jobs — create, returns { jobId }. docs/API.md. */

const CreateJob = z.object({
  sceneId: z.string().min(1),
})

export const POST = withErrors(async (req: Request) => {
  const session = await sessionFromRequest(req)
  if (!session) return apiError('UNAUTHORIZED', 'Missing or invalid session token')

  const parsed = CreateJob.safeParse(await req.json().catch(() => null))
  if (!parsed.success) return zodError(parsed.error)

  const scene = await prisma.scene.findUnique({
    where: { id: parsed.data.sceneId },
    include: { case: { select: { ownerId: true } }, job: { select: { id: true } } },
  })
  if (!scene) return apiError('NOT_FOUND', 'Scene not found')

  // An investigator must not act on another investigator's case (D7).
  if (session.role !== 'ADMIN' && scene.case.ownerId !== session.sub) {
    return apiError('FORBIDDEN', 'Scene belongs to another investigator')
  }

  // Contract: a scene owns one reconstruction job. Returning the existing id
  // keeps a retried request idempotent instead of training the scene twice.
  if (scene.job) {
    return apiOk({ jobId: scene.job.id, existing: true })
  }

  const sourceAsset = await prisma.asset.findFirst({
    where: { sceneId: scene.id, kind: 'SOURCE_VIDEO' },
    orderBy: { createdAt: 'desc' },
    select: { storageKey: true },
  })
  if (!sourceAsset) {
    return apiError('BAD_REQUEST', 'Scene has no uploaded video to reconstruct')
  }

  // Audit row and job row commit together: a job with no custody entry, or a
  // custody entry for a job that does not exist, are both worse than failing.
  const job = await prisma.$transaction(async (tx) => {
    const created = await tx.job.create({
      data: { sceneId: scene.id, status: 'PENDING' },
      select: { id: true },
    })
    await appendAudit(
      { userId: session.sub, action: 'job.create', targetType: 'Job', targetId: created.id },
      tx
    )
    return created
  })

  // Enqueued after commit: a worker could otherwise claim the job before the
  // transaction is visible and fail to find it.
  await enqueueReconstruction({
    jobId: job.id,
    sceneId: scene.id,
    sourceKey: sourceAsset.storageKey,
  })

  return apiOk({ jobId: job.id }, 201)
})
