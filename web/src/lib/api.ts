import { ZodError } from 'zod'
import { isRateLimited } from './rate-limit'

/**
 * One error shape for every route, so the Unity client and the GPU worker can
 * parse failures without special-casing per endpoint:
 *
 *   { "error": { "code": "NOT_FOUND", "message": "..." , "details"?: ... } }
 */

export type ApiErrorCode =
  | 'BAD_REQUEST'
  | 'UNAUTHORIZED'
  | 'FORBIDDEN'
  | 'NOT_FOUND'
  | 'CONFLICT'
  | 'PAYLOAD_TOO_LARGE'
  | 'TOO_MANY_REQUESTS'
  | 'INTERNAL'

const STATUS: Record<ApiErrorCode, number> = {
  BAD_REQUEST: 400,
  UNAUTHORIZED: 401,
  FORBIDDEN: 403,
  NOT_FOUND: 404,
  CONFLICT: 409,
  PAYLOAD_TOO_LARGE: 413,
  TOO_MANY_REQUESTS: 429,
  INTERNAL: 500,
}

export function apiError(code: ApiErrorCode, message: string, details?: unknown) {
  return Response.json(
    { error: { code, message, ...(details === undefined ? {} : { details }) } },
    { status: STATUS[code] }
  )
}

export function apiOk(data: unknown, status = 200) {
  return Response.json(data, { status })
}

/** Zod issues as BAD_REQUEST details, rather than a 500 from an uncaught throw. */
export function zodError(err: ZodError) {
  return apiError('BAD_REQUEST', 'Request validation failed', err.issues)
}

/**
 * Wraps a handler so an unexpected throw becomes a logged 500 in the standard
 * shape. The contract's rule is that nothing fails silently, and an unhandled
 * rejection in a route handler is exactly that.
 */
export function withErrors<T extends unknown[]>(
  handler: (...args: T) => Promise<Response>
): (...args: T) => Promise<Response> {
  return async (...args: T) => {
    try {
      if (args[0] instanceof Request) {
        const req = args[0]
        const ip = req.headers.get('x-forwarded-for') || req.headers.get('x-real-ip') || 'global'
        if (await isRateLimited(ip)) {
          return apiError('TOO_MANY_REQUESTS', 'Rate limit exceeded')
        }
      }
      return await handler(...args)
    } catch (err) {
      if (err instanceof ZodError) return zodError(err)
      console.error('[api] unhandled error', err)
      return apiError('INTERNAL', 'Unexpected server error')
    }
  }
}

/** BigInt (Asset.byteSize) is not JSON-serialisable; send it as a number. */
export function serialiseAsset<T extends { byteSize: bigint }>(asset: T) {
  return { ...asset, byteSize: Number(asset.byteSize) }
}
