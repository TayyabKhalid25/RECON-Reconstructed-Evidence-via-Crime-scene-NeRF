import { z } from 'zod'
import { prisma } from '@/lib/prisma'
import { apiError, apiOk, withErrors, zodError } from '@/lib/api'
import { sessionFromRequest } from '@/lib/auth'
import { appendAudit } from '@/lib/custody'

/**
 * GET  /api/cases — the caller's cases, newest first.
 * POST /api/cases — create one.
 *
 * Added because nothing could list or create a Case, so the upload flow was
 * unusable from a browser: POST /api/uploads needs a caseId and there was no
 * way to discover or make one. docs/API.md is updated in the same change.
 *
 * RBAC is the same shape as the scene routes: an investigator sees only their
 * own cases, an ADMIN sees all. The handbook is explicit that "what stops an
 * investigator downloading another investigator's case" needs a code answer,
 * and this is that answer for the list endpoint.
 */

const NewCase = z.object({
  title: z.string().trim().min(1).max(200),
  description: z.string().trim().max(4000).optional(),
})

export const GET = withErrors(async (req: Request) => {
  const session = await sessionFromRequest(req)
  if (!session) return apiError('UNAUTHORIZED', 'Missing or invalid session token')

  const cases = await prisma.case.findMany({
    // Scoped in the query rather than filtered after fetching: the rows never
    // leave the database, so there is nothing to accidentally serialise.
    where: session.role === 'ADMIN' ? {} : { ownerId: session.sub },
    orderBy: { createdAt: 'desc' },
    select: {
      id: true,
      title: true,
      description: true,
      createdAt: true,
      updatedAt: true,
      ownerId: true,
      // Enough for a list row to be useful without a second request per case.
      _count: { select: { scenes: true } },
    },
  })

  return apiOk({
    cases: cases.map(({ _count, ...c }) => ({ ...c, sceneCount: _count.scenes })),
  })
})

export const POST = withErrors(async (req: Request) => {
  const session = await sessionFromRequest(req)
  if (!session) return apiError('UNAUTHORIZED', 'Missing or invalid session token')

  // A VIEWER may read a case but must not open one. Opening a case is the act
  // that starts a custody record.
  if (session.role === 'VIEWER') {
    return apiError('FORBIDDEN', 'Viewers cannot create cases')
  }

  const parsed = NewCase.safeParse(await req.json().catch(() => null))
  if (!parsed.success) return zodError(parsed.error)

  const created = await prisma.case.create({
    data: {
      title: parsed.data.title,
      description: parsed.data.description,
      ownerId: session.sub,
    },
    select: {
      id: true,
      title: true,
      description: true,
      createdAt: true,
      updatedAt: true,
      ownerId: true,
    },
  })

  // Opening a case is the first link in its chain of custody.
  await appendAudit({
    userId: session.sub,
    action: 'case.create',
    targetType: 'Case',
    targetId: created.id,
  })

  return apiOk({ case: { ...created, sceneCount: 0 } }, 201)
})
