import { prisma } from '@/lib/prisma'
import { apiError, withErrors } from '@/lib/api'
import { sessionFromRequest } from '@/lib/auth'
import { appendAudit } from '@/lib/custody'
import { assetExists, assetStream, urlFor } from '@/lib/storage'

/**
 * GET /api/scenes/:id/asset — the .ply, or a redirect to it. docs/API.md.
 *
 * That "or a redirect" clause is what makes FTW-13 reversible: on disk storage
 * this streams bytes, and an object-store driver would return a presigned URL
 * from urlFor() and get a 302 here, with no contract change.
 */

export const GET = withErrors(
  async (req: Request, ctx: RouteContext<'/api/scenes/[id]/asset'>) => {
    const session = await sessionFromRequest(req)
    if (!session) return apiError('UNAUTHORIZED', 'Missing or invalid session token')

    const { id } = await ctx.params

    const scene = await prisma.scene.findUnique({
      where: { id },
      select: { id: true, case: { select: { ownerId: true } } },
    })
    if (!scene) return apiError('NOT_FOUND', 'Scene not found')
    if (session.role !== 'ADMIN' && scene.case.ownerId !== session.sub) {
      return apiError('NOT_FOUND', 'Scene not found')
    }

    const asset = await prisma.asset.findFirst({
      where: { sceneId: id, kind: 'SPLAT_PLY' },
      orderBy: { createdAt: 'desc' },
      select: { id: true, storageKey: true, byteSize: true, sha256: true },
    })
    if (!asset) return apiError('NOT_FOUND', 'Scene has no reconstructed asset yet')

    const redirect = urlFor(asset.storageKey)
    if (redirect) return Response.redirect(redirect, 302)

    if (!(await assetExists(asset.storageKey))) {
      // Row points at a file that is gone: say so plainly rather than serving
      // an empty body that Unity would fail to parse for no clear reason.
      return apiError('NOT_FOUND', 'Asset row exists but the file is missing on disk')
    }

    // Every asset fetch is a custody event: who pulled the evidence, and when.
    await appendAudit({
      userId: session.sub,
      action: 'asset.download',
      targetType: 'Asset',
      targetId: asset.id,
    })

    return new Response(await assetStream(asset.storageKey), {
      headers: {
        'Content-Type': 'application/octet-stream',
        'Content-Length': String(asset.byteSize),
        'Content-Disposition': `attachment; filename="${id}.ply"`,
        // Lets Unity verify the bytes it received match what custody recorded.
        'X-Asset-SHA256': asset.sha256,
      },
    })
  }
)
