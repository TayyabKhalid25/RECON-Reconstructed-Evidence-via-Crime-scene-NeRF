import { prisma } from '@/lib/prisma'
import { apiError, apiOk, serialiseAsset, withErrors } from '@/lib/api'
import { appendAudit, sha256Buffer } from '@/lib/custody'
import { sessionFromRequest } from '@/lib/auth'
import { assetKey, writeAsset } from '@/lib/storage'
import { enqueueReconstruction } from '@/lib/queue'

/**
 * POST /api/uploads — the walkthrough video in, a PENDING job out.
 *
 * Multipart: `video` (file), `caseId`, and optional `sceneName`. Computes
 * SHA-256 and writes a custody row at upload time, because custody starts on
 * day one and is not a November feature.
 */

/** Captures are 60-90 s of 1080p per docs/CAPTURE.md; 2 GB is generous. */
const MAX_UPLOAD_BYTES = 2 * 1024 * 1024 * 1024

export const POST = withErrors(async (req: Request) => {
  const session = await sessionFromRequest(req)
  if (!session) return apiError('UNAUTHORIZED', 'Missing or invalid session token')
  if (session.role === 'VIEWER') {
    return apiError('FORBIDDEN', 'Viewers cannot upload captures')
  }

  const form = await req.formData().catch(() => null)
  if (!form) return apiError('BAD_REQUEST', 'Expected multipart/form-data')

  const video = form.get('video')
  const caseId = form.get('caseId')
  const sceneName = form.get('sceneName')

  if (!(video instanceof File)) return apiError('BAD_REQUEST', 'Missing video file field')
  if (typeof caseId !== 'string' || caseId.length === 0) {
    return apiError('BAD_REQUEST', 'Missing caseId field')
  }
  if (video.size > MAX_UPLOAD_BYTES) {
    return apiError('PAYLOAD_TOO_LARGE', `Upload exceeds ${MAX_UPLOAD_BYTES} bytes`)
  }

  const kase = await prisma.case.findUnique({
    where: { id: caseId },
    select: { id: true, ownerId: true },
  })
  if (!kase) return apiError('NOT_FOUND', 'Case not found')
  if (session.role !== 'ADMIN' && kase.ownerId !== session.sub) {
    return apiError('FORBIDDEN', 'Case belongs to another investigator')
  }

  const buf = Buffer.from(await video.arrayBuffer())
  const sha256 = sha256Buffer(buf)

  // Scene row first, so the storage key is scene-scoped and every file for one
  // capture lands together on disk.
  const scene = await prisma.scene.create({
    data: {
      caseId: kase.id,
      name: typeof sceneName === 'string' && sceneName ? sceneName : (video.name || 'capture'),
    },
    select: { id: true, name: true },
  })

  const key = assetKey(scene.id, video.name || 'capture.mp4')
  await writeAsset(key, buf)

  const { asset, job } = await prisma.$transaction(async (tx) => {
    const createdAsset = await tx.asset.create({
      data: {
        kind: 'SOURCE_VIDEO',
        sceneId: scene.id,
        storageKey: key,
        sha256,
        byteSize: BigInt(buf.byteLength),
        mimeType: video.type || 'video/mp4',
      },
      select: { id: true, sha256: true, byteSize: true, storageKey: true },
    })

    const createdJob = await tx.job.create({
      data: { sceneId: scene.id, status: 'PENDING' },
      select: { id: true, status: true },
    })

    await appendAudit(
      { userId: session.sub, action: 'asset.upload', targetType: 'Asset', targetId: createdAsset.id },
      tx
    )
    await appendAudit(
      { userId: session.sub, action: 'job.create', targetType: 'Job', targetId: createdJob.id },
      tx
    )

    return { asset: createdAsset, job: createdJob }
  })

  // After commit, so a worker cannot claim a job the transaction has not made
  // visible yet.
  await enqueueReconstruction({ jobId: job.id, sceneId: scene.id, sourceKey: key })

  return apiOk(
    { scene, job, asset: serialiseAsset(asset) },
    201
  )
})
