import { redisConnection } from './queue'

const LIMIT = 200 // 200 requests per minute
const WINDOW_SECONDS = 60

/**
 * Fixed-window rate limiter using Redis.
 * Returns true if the request has exceeded the rate limit.
 */
export async function isRateLimited(ip: string): Promise<boolean> {
  try {
    const redis = redisConnection()
    const currentWindow = Math.floor(Date.now() / 1000 / WINDOW_SECONDS)
    const key = `ratelimit:${ip}:${currentWindow}`
    
    const current = await redis.incr(key)
    if (current === 1) {
      await redis.expire(key, WINDOW_SECONDS)
    }
    
    return current > LIMIT
  } catch (err) {
    // If Redis is down, fail open so we don't break the whole app
    console.warn('[rate-limit] Redis error, failing open:', err)
    return false
  }
}
