/**
 * Throwaway worker: proves the queue actually works end to end.
 *
 * Deliberately does no reconstruction. It claims a job off Redis and walks it
 * PENDING -> PROCESSING -> READY so the pipeline can be verified before the
 * real GPU worker exists. The real worker (Track A, FTW-37) replaces this and
 * this file gets deleted.
 *
 *   npx tsx scripts/throwaway-worker.ts
 */
import { Worker } from 'bullmq'
import { PrismaClient } from '@prisma/client'
import { RECONSTRUCTION_QUEUE, redisConnection, type ReconstructionJobData } from '../src/lib/queue'
import { appendAudit } from '../src/lib/custody'

const prisma = new PrismaClient()

const worker = new Worker<ReconstructionJobData>(
  RECONSTRUCTION_QUEUE,
  async (job) => {
    const { jobId, sceneId } = job.data
    console.log(`[worker] claimed job=${jobId} scene=${sceneId}`)

    await prisma.$transaction(async (tx) => {
      await tx.job.updateMany({
        where: { id: jobId, status: 'PENDING' },
        data: { status: 'PROCESSING', startedAt: new Date(), workerName: 'throwaway' },
      })
      await appendAudit(
        { userId: null, action: 'job.status:PROCESSING', targetType: 'Job', targetId: jobId },
        tx
      )
    })

    // Stand-in for minutes of COLMAP plus splatfacto.
    for (const progress of [25, 50, 75]) {
      await new Promise((r) => setTimeout(r, 400))
      await prisma.job.update({ where: { id: jobId }, data: { progress } })
      console.log(`[worker] job=${jobId} progress=${progress}`)
    }

    await prisma.$transaction(async (tx) => {
      await tx.job.updateMany({
        where: { id: jobId, status: 'PROCESSING' },
        data: { status: 'READY', progress: 100, finishedAt: new Date() },
      })
      await appendAudit(
        { userId: null, action: 'job.status:READY', targetType: 'Job', targetId: jobId },
        tx
      )
    })

    console.log(`[worker] job=${jobId} READY`)
    return { ok: true }
  },
  {
    connection: redisConnection(),
    // One at a time: a real reconstruction owns the GPU, so the throwaway
    // worker models the same constraint.
    concurrency: 1,
  }
)

worker.on('failed', async (job, err) => {
  console.error(`[worker] job=${job?.id} FAILED: ${err.message}`)
  if (!job?.data?.jobId) return
  // Contract: FAILED always carries readable error text.
  await prisma.job.updateMany({
    where: { id: job.data.jobId },
    data: { status: 'FAILED', error: err.message, finishedAt: new Date() },
  })
})

console.log(`[worker] listening on queue "${RECONSTRUCTION_QUEUE}"`)

for (const signal of ['SIGINT', 'SIGTERM'] as const) {
  process.on(signal, async () => {
    console.log(`\n[worker] ${signal}, closing`)
    await worker.close()
    await prisma.$disconnect()
    process.exit(0)
  })
}
