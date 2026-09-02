import { Queue } from 'bullmq'
import IORedis from 'ioredis'

/**
 * BullMQ reconstruction queue.
 *
 * docs/API.md: the real GPU worker takes jobs off Redis rather than polling
 * /api/jobs/next, but that endpoint stays for manual testing and for recovering
 * a stuck queue.
 */

export const RECONSTRUCTION_QUEUE = 'reconstruction'

/** Payload the GPU worker receives. Keep in step with docs/API.md. */
export type ReconstructionJobData = {
  jobId: string
  sceneId: string
  /** Storage key of the uploaded video, resolved through src/lib/storage.ts. */
  sourceKey: string
}

const globalForQueue = globalThis as unknown as {
  reconnection: IORedis | undefined
  reconQueue: Queue<ReconstructionJobData> | undefined
}

/**
 * BullMQ requires maxRetriesPerRequest: null, and it must not be overridden:
 * with a finite value a blocking command can throw during a brief Redis blip
 * and kill the worker instead of reconnecting.
 */
export function redisConnection(): IORedis {
  if (globalForQueue.reconnection) return globalForQueue.reconnection

  const url = process.env.REDIS_URL
  if (!url) throw new Error('REDIS_URL is not set')

  const conn = new IORedis(url, { maxRetriesPerRequest: null })
  if (process.env.NODE_ENV !== 'production') globalForQueue.reconnection = conn
  return conn
}

/** Cached like the Prisma client, so dev hot reload does not leak connections. */
export function reconstructionQueue(): Queue<ReconstructionJobData> {
  if (globalForQueue.reconQueue) return globalForQueue.reconQueue

  const queue = new Queue<ReconstructionJobData>(RECONSTRUCTION_QUEUE, {
    connection: redisConnection(),
    defaultJobOptions: {
      // A reconstruction is minutes of GPU time. Retrying a genuine failure
      // automatically wastes the box and hides the error, so failures surface
      // as FAILED with readable text instead.
      attempts: 1,
      removeOnComplete: { count: 100 },
      removeOnFail: false,
    },
  })

  if (process.env.NODE_ENV !== 'production') globalForQueue.reconQueue = queue
  return queue
}

/**
 * Enqueue with the database job id as the BullMQ job id, so an accidental
 * double-enqueue of the same job is dropped by Redis rather than training the
 * same scene twice.
 */
export async function enqueueReconstruction(data: ReconstructionJobData) {
  return reconstructionQueue().add('reconstruct', data, { jobId: data.jobId })
}
