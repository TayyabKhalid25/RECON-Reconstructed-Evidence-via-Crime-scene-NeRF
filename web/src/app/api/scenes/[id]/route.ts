import { prisma } from '@/lib/prisma'
import { apiError, apiOk, serialiseAsset, withErrors } from '@/lib/api'
import { sessionFromRequest } from '@/lib/auth'

/** GET /api/scenes/:id — metadata plus asset URLs. docs/API.md. */

export const GET = withErrors(async (req: Request, ctx: RouteContext<'/api/scenes/[id]'>) => {
  const session = await sessionFromRequest(req)
  if (!session) return apiError('UNAUTHORIZED', 'Missing or invalid session token')

  const { id } = await ctx.params

  const scene = await prisma.scene.findUnique({
    where: { id },
    select: {
      id: true,
      name: true,
      createdAt: true,
      unitScale: true,
      caseId: true,
      case: { select: { ownerId: true } },
      job: { select: { id: true, status: true, progress: true, error: true } },
      assets: {
        select: {
          id: true,
          kind: true,
          sha256: true,
          byteSize: true,
          mimeType: true,
          createdAt: true,
        },
        orderBy: { createdAt: 'asc' },
      },
    },
  })

  // 404 rather than 403 for another investigator's scene: a 403 confirms the id
  // exists, which is itself a leak across cases.
  if (!scene) return apiError('NOT_FOUND', 'Scene not found')
  if (session.role !== 'ADMIN' && scene.case.ownerId !== session.sub) {
    return apiError('NOT_FOUND', 'Scene not found')
  }

  // `case` carries the ownerId used for the check above and is not part of the
  // response.
  const { assets, ...rest } = scene
  delete (rest as { case?: unknown }).case

  return apiOk({
    scene: {
      ...rest,
      assets: assets.map(serialiseAsset),
      // Storage keys are deliberately not exposed; the asset route resolves
      // them so disk-versus-object-storage stays invisible to Unity.
      assetUrl: `/api/scenes/${scene.id}/asset`,
    },
  })
})
