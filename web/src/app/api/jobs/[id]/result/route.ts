import { JobStatus } from '@prisma/client'
import { prisma } from '@/lib/prisma'
import { apiError, apiOk, withErrors } from '@/lib/api'
import { appendAudit, sha256Buffer } from '@/lib/custody'
import { isAuthorisedWorker } from '@/lib/auth'
import { assetKey, writeAsset } from '@/lib/storage'
import { canTransition, transitionError } from '@/lib/job-state'

/**
 * POST /api/jobs/:id/result — attach .ply and metadata.json. docs/API.md.
 *
 * Multipart with fields `ply` and `metadata`. Moves the job to READY, and reads
 * unitScale out of metadata.json so Unity can place the twin at real size
 * without parsing the asset first.
 */

export const POST = withErrors(
  async (req: Request, ctx: RouteContext<'/api/jobs/[id]/result'>) => {
    if (!isAuthorisedWorker(req)) {
      return apiError('UNAUTHORIZED', 'Missing or invalid worker token')
    }

    const { id } = await ctx.params

    const job = await prisma.job.findUnique({
      where: { id },
      select: { id: true, status: true, sceneId: true },
    })
    if (!job) return apiError('NOT_FOUND', 'Job not found')

    if (!canTransition(job.status, JobStatus.READY)) {
      return apiError('CONFLICT', transitionError(job.status, JobStatus.READY))
    }

    const form = await req.formData().catch(() => null)
    if (!form) return apiError('BAD_REQUEST', 'Expected multipart/form-data')

    const ply = form.get('ply')
    const metadata = form.get('metadata')
    if (!(ply instanceof File)) return apiError('BAD_REQUEST', 'Missing ply file field')
    if (!(metadata instanceof File)) {
      // Unity cannot place a scene correctly without metadata.json, so a result
      // without it is not a usable result.
      return apiError('BAD_REQUEST', 'Missing metadata file field')
    }

    const plyBuf = Buffer.from(await ply.arrayBuffer())
    const metaBuf = Buffer.from(await metadata.arrayBuffer())

    let unitScale: number | null = null
    try {
      const parsed = JSON.parse(metaBuf.toString('utf8'))
      if (typeof parsed.unitScale === 'number' && Number.isFinite(parsed.unitScale)) {
        unitScale = parsed.unitScale
      }
    } catch {
      return apiError('BAD_REQUEST', 'metadata is not valid JSON')
    }

    const plyKey = assetKey(job.sceneId, 'splat_unity.ply')
    const metaKey = assetKey(job.sceneId, 'metadata.json')

    // Files land on disk before the database points at them: a row referencing
    // a missing file is harder to diagnose than an orphaned file.
    await writeAsset(plyKey, plyBuf)
    await writeAsset(metaKey, metaBuf)

    const result = await prisma.$transaction(async (tx) => {
      const moved = await tx.job.updateMany({
        where: { id, status: job.status },
        data: { status: JobStatus.READY, progress: 100, finishedAt: new Date(), error: null },
      })
      if (moved.count === 0) return null

      for (const [kind, key, buf, mime] of [
        ['SPLAT_PLY', plyKey, plyBuf, 'application/octet-stream'],
        ['METADATA_JSON', metaKey, metaBuf, 'application/json'],
      ] as const) {
        await tx.asset.create({
          data: {
            kind,
            sceneId: job.sceneId,
            storageKey: key,
            sha256: sha256Buffer(buf),
            byteSize: BigInt(buf.byteLength),
            mimeType: mime,
          },
        })
      }

      if (unitScale !== null) {
        await tx.scene.update({ where: { id: job.sceneId }, data: { unitScale } })
      }

      await appendAudit(
        { userId: null, action: 'job.result', targetType: 'Job', targetId: id },
        tx
      )

      return { plyKey, metaKey, unitScale }
    })

    if (!result) return apiError('CONFLICT', 'Job status changed concurrently, retry')

    return apiOk({ job: { id, status: JobStatus.READY }, ...result }, 201)
  }
)
