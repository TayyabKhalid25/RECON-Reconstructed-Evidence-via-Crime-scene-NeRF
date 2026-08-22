---
name: building-the-job-pipeline
description: Use when implementing upload, job enqueue, the BullMQ queue, worker claiming, status transitions, or when jobs get stuck in PROCESSING, run twice, or vanish silently.
---

# Building the job pipeline

## Overview

The spine of the system: upload → PENDING job on Redis → GPU worker claims it → PROCESSING with progress → READY with assets, or FAILED with readable error text. Two properties are non-negotiable: **a job never vanishes silently**, and **re-running a job is safe** (workers die mid-job; the retry must not corrupt anything).

## The upload trap, decide hosting before writing code

Walkthrough videos are 100 MB+. Serverless platforms cap request bodies far below that (Vercel is ~4.5 MB `verify`), so `POST /api/upload` with the file in the body dies on the platforms you would deploy to first. Two workable shapes:

1. **Self-host Next.js as a long-running Node server** (a team machine behind a tunnel, or any free-tier VM): stream `request.body` straight to disk, never buffer the whole file in memory.
2. **Direct-to-storage upload**: the API returns a presigned URL, the phone PUTs the video to storage, then calls back to create the job. Required if the web app lives on serverless.

Pick one now; it decides your storage answer (handbook Decision 4) too.

## Queue rules

```ts
// connection: BullMQ requires maxRetriesPerRequest: null on ioredis
const connection = new IORedis(process.env.REDIS_URL!, { maxRetriesPerRequest: null });

const queue = new Queue("reconstruct", { connection,
  defaultJobOptions: {
    attempts: 3,
    backoff: { type: "exponential", delay: 60_000 },
    removeOnComplete: 100, removeOnFail: false,   // keep failures for post-mortems
  }});
```

- **Concurrency 1 per GPU worker.** One training run per GPU, always.
- **BullMQ uses blocking commands, not polling**, which matters on metered free-tier Redis. Never add your own tight polling loop next to it.
- **Progress:** worker calls `job.updateProgress()` sparingly (per stage, not per iteration); each progress tick is Redis traffic.
- **Idempotency:** the job handler starts by wiping its own partial output directory. A retried job produces the same result, not a duplicate asset row: upsert by `jobId`, never blind insert.

## Status transitions live in Postgres, guarded

Redis is the queue; Postgres is the truth. Enforce the state machine with conditional updates so a stale worker cannot resurrect a finished job:

```ts
const r = await prisma.job.updateMany({
  where: { id, status: "PENDING" },          // expected current state
  data: { status: "PROCESSING", workerName },
});
if (r.count === 0) throw new Error("job not claimable, state changed under us");
```

Every transition writes an AuditLog row in the same Prisma transaction.

## Stuck-job symptom table

| Symptom | Cause | Fix |
|---|---|---|
| PROCESSING forever | Worker died mid-job | BullMQ stalled-job detection re-queues it; make sure `attempts > 1` and the handler is idempotent |
| Job ran twice, two assets | Handler not idempotent | Upsert by jobId, wipe partial output on start |
| Jobs vanish | `removeOnFail` left at default, or error swallowed | Keep failed jobs, and every catch block writes FAILED plus the actual error string to Postgres |
| Worker can't connect from WSL | Plain `redis://` against a TLS-only host | Use `rediss://` and confirm the WSL clock is not skewed |
