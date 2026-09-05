import { prisma } from '@/lib/prisma'
import { apiError, apiOk, withErrors, zodError } from '@/lib/api'
import { sessionFromRequest } from '@/lib/auth'
import { appendAudit } from '@/lib/custody'
import { AnchorInput, serialiseAnchor, toAnchorColumns } from '@/lib/anchor'

/**
 * POST /api/scenes/:id/anchor — store cloud anchor id and transform.
 * GET  /api/scenes/:id/anchor — second device resolves the same anchor.
 *
 * docs/API.md: "We store the anchor identifier plus the transform from anchor
 * space to scene space, so alignment survives across sessions and devices."
 *
 * A POST adds a row rather than replacing one, and GET returns the newest. That
 * keeps "where did the twin sit last week" answerable after a re-host, which
 * matters for a forensic record and costs nothing to preserve.
 */

/** Ownership check shared by both handlers. */
async function findViewableScene(id: string, session: { sub: string; role: string }) {
  const scene = await prisma.scene.findUnique({
    where: { id },
    select: { id: true, case: { select: { ownerId: true } } },
  })
  if (!scene) return null
  // 404 rather than 403 for another investigator's scene: a 403 confirms the id
  // exists, which is itself a leak across cases (D7).
  if (session.role !== 'ADMIN' && scene.case.ownerId !== session.sub) return null
  return scene
}

export const POST = withErrors(
  async (req: Request, ctx: { params: Promise<{ id: string }> }) => {
    const session = await sessionFromRequest(req)
    if (!session) return apiError('UNAUTHORIZED', 'Missing or invalid session token')

    // A VIEWER may resolve an anchor but must not move where the evidence sits.
    if (session.role === 'VIEWER') {
      return apiError('FORBIDDEN', 'Viewers cannot host anchors')
    }

    const { id } = await ctx.params

    const parsed = AnchorInput.safeParse(await req.json().catch(() => null))
    if (!parsed.success) return zodError(parsed.error)

    const scene = await findViewableScene(id, session)
    if (!scene) return apiError('NOT_FOUND', 'Scene not found')

    const anchor = await prisma.anchor.create({
      data: {
        ...toAnchorColumns(parsed.data),
        sceneId: scene.id,
        createdById: session.sub,
      },
    })

    // Anchoring is a custody event: it asserts where in the real world this
    // evidence sits, which is exactly the kind of claim the chain exists to
    // make attributable.
    await appendAudit({
      userId: session.sub,
      action: 'anchor.create',
      targetType: 'Anchor',
      targetId: anchor.id,
    })

    return apiOk({ anchor: serialiseAnchor(anchor) }, 201)
  }
)

export const GET = withErrors(
  async (req: Request, ctx: { params: Promise<{ id: string }> }) => {
    const session = await sessionFromRequest(req)
    if (!session) return apiError('UNAUTHORIZED', 'Missing or invalid session token')

    const { id } = await ctx.params

    const scene = await findViewableScene(id, session)
    if (!scene) return apiError('NOT_FOUND', 'Scene not found')

    const anchor = await prisma.anchor.findFirst({
      where: { sceneId: scene.id },
      orderBy: { createdAt: 'desc' },
    })
    // Distinct from "scene not found" on purpose: 200 with null anchor tells the
    // second device that the scene exists but nobody has hosted an anchor yet,
    // so it can fall back to marker/manual alignment cleanly without an error.
    if (!anchor) return apiOk({ anchor: null })

    return apiOk({ anchor: serialiseAnchor(anchor) })
  }
)
