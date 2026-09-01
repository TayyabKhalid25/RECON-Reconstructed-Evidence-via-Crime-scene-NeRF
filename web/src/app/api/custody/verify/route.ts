import { apiError, apiOk, withErrors } from '@/lib/api'
import { sessionFromRequest } from '@/lib/auth'
import { verifyCustodyChain } from '@/lib/custody'

/**
 * GET /api/custody/verify — walks the audit hash chain and reports pass or fail.
 *
 * D12's acceptance test is exactly this: call it and get a pass, then tamper one
 * row directly in the database and confirm it returns fail. The response names
 * the first failing seq, so "which row was touched" is answerable.
 */

export const GET = withErrors(async (req: Request) => {
  const session = await sessionFromRequest(req)
  if (!session) return apiError('UNAUTHORIZED', 'Missing or invalid session token')
  // Custody state is a whole-system property, so this is not investigator-scoped.
  if (session.role !== 'ADMIN') {
    return apiError('FORBIDDEN', 'Custody verification is admin only')
  }

  const result = await verifyCustodyChain()

  // 200 either way: a failed verification is a successful answer to the
  // question, not a server error. Callers branch on `ok`.
  return apiOk(result)
})
