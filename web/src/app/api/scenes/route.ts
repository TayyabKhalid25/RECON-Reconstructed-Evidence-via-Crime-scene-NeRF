import { JobStatus } from '@prisma/client'
import { prisma } from '@/lib/prisma'
import { apiError, apiOk, withErrors } from '@/lib/api'
import { sessionFromRequest } from '@/lib/auth'

/** GET /api/scenes?status=READY — list, newest first. docs/API.md. */

export const GET = withErrors(async (req: Request) => {
  const session = await sessionFromRequest(req)
  if (!session) return apiError('UNAUTHORIZED', 'Missing or invalid session token')

  const statusParam = new URL(req.url).searchParams.get('status')
  if (statusParam && !(statusParam in JobStatus)) {
    return apiError('BAD_REQUEST', `Unknown status ${statusParam}`)
  }

  const scenes = await prisma.scene.findMany({
    where: {
      // Investigators see only their own cases; admins see everything. This is
      // the server-side filter, not a hidden button (D7).
      ...(session.role === 'ADMIN' ? {} : { case: { ownerId: session.sub } }),
      ...(statusParam ? { job: { status: statusParam as JobStatus } } : {}),
    },
    orderBy: { createdAt: 'desc' },
    select: {
      id: true,
      name: true,
      createdAt: true,
      unitScale: true,
      caseId: true,
      job: { select: { id: true, status: true, progress: true } },
    },
  })

  return apiOk({ scenes })
})
