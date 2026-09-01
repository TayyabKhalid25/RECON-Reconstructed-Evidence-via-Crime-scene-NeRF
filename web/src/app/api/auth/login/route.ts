import { z } from 'zod'
import bcrypt from 'bcryptjs'
import { prisma } from '@/lib/prisma'
import { apiError, apiOk, withErrors, zodError } from '@/lib/api'
import { signSession } from '@/lib/auth'
import { appendAudit } from '@/lib/custody'

/**
 * POST /api/auth/login — returns a token. docs/API.md.
 *
 * Full session handling and per-role route tests are D7 (27 Sep); this is what
 * first light needs so the other endpoints are authenticated from the start.
 */

const Login = z.object({
  email: z.string().email(),
  password: z.string().min(1),
})

export const POST = withErrors(async (req: Request) => {
  const parsed = Login.safeParse(await req.json().catch(() => null))
  if (!parsed.success) return zodError(parsed.error)

  const user = await prisma.user.findUnique({
    where: { email: parsed.data.email },
    select: { id: true, email: true, role: true, passwordHash: true },
  })

  // Same response whether the email is unknown or the password is wrong, so the
  // endpoint cannot be used to enumerate accounts.
  const invalid = () => apiError('UNAUTHORIZED', 'Invalid email or password')
  if (!user) {
    // Still spend the hash time, so a missing user is not detectably faster.
    await bcrypt.compare(parsed.data.password, '$2a$10$invalidinvalidinvalidinvalidinvalidinvalidinvalidinvaliduu')
    return invalid()
  }
  if (!(await bcrypt.compare(parsed.data.password, user.passwordHash))) {
    await appendAudit({
      userId: user.id,
      action: 'auth.login.failed',
      targetType: 'User',
      targetId: user.id,
    })
    return invalid()
  }

  const token = await signSession({ sub: user.id, email: user.email, role: user.role })

  await appendAudit({
    userId: user.id,
    action: 'auth.login',
    targetType: 'User',
    targetId: user.id,
  })

  return apiOk({ token, user: { id: user.id, email: user.email, role: user.role } })
})
