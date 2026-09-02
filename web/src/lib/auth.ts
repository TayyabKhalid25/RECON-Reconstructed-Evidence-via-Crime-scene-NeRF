import { SignJWT, jwtVerify } from 'jose'
import type { Role } from '@prisma/client'

/**
 * JWT sessions. Full login and RBAC enforcement is D7 (27 Sep); this is the
 * minimum first light needs so routes are not built unauthenticated and
 * retrofitted later.
 */

export type SessionClaims = {
  sub: string
  email: string
  role: Role
}

const ALG = 'HS256'
const TOKEN_TTL = '12h'

function secret(): Uint8Array {
  const s = process.env.JWT_SECRET
  if (!s || s === 'change-me') {
    // Fail loudly rather than signing with a placeholder, which would mint
    // tokens anyone reading .env.example could forge.
    throw new Error('JWT_SECRET is not set to a real value')
  }
  return new TextEncoder().encode(s)
}

export async function signSession(claims: SessionClaims): Promise<string> {
  return new SignJWT({ email: claims.email, role: claims.role })
    .setProtectedHeader({ alg: ALG })
    .setSubject(claims.sub)
    .setIssuedAt()
    .setExpirationTime(TOKEN_TTL)
    .sign(secret())
}

/** Returns null on any invalid or expired token; callers turn that into a 401. */
export async function verifySession(token: string): Promise<SessionClaims | null> {
  try {
    const { payload } = await jwtVerify(token, secret(), { algorithms: [ALG] })
    if (!payload.sub) return null
    return {
      sub: payload.sub,
      email: String(payload.email ?? ''),
      role: payload.role as Role,
    }
  } catch {
    return null
  }
}

/** Reads the bearer token from an Authorization header. */
export async function sessionFromRequest(req: Request): Promise<SessionClaims | null> {
  const header = req.headers.get('authorization')
  if (!header?.startsWith('Bearer ')) return null
  return verifySession(header.slice('Bearer '.length).trim())
}

/**
 * Shared-secret auth for GPU workers, which are machines and have no user
 * session. Separate from user JWTs so a leaked worker token cannot be replayed
 * against investigator-facing routes.
 */
export function isAuthorisedWorker(req: Request): boolean {
  const expected = process.env.WORKER_TOKEN
  if (!expected) return false
  const provided = req.headers.get('x-worker-token')
  if (!provided || provided.length !== expected.length) return false
  // Constant-time-ish compare; lengths are already equal here.
  let diff = 0
  for (let i = 0; i < expected.length; i += 1) {
    diff |= expected.charCodeAt(i) ^ provided.charCodeAt(i)
  }
  return diff === 0
}
