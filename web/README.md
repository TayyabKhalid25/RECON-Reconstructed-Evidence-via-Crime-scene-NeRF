# Track B · Web platform

Owner: Wahaj. (As of 30 Aug 2026, Wahaj leads both Track A and Track B; Faizan has moved to
Track C, Unity, and is no longer second pair of hands here.)

Next.js (App Router) + Prisma 5 + PostgreSQL 16 + Redis/BullMQ. Accepts the walkthrough upload,
creates the case, enqueues the reconstruction job, stores the produced asset, and keeps the
hash-chained custody log.

Build order is the Track B first light checklist in handbook Section 05. The endpoints, data
model, and job state machine are fixed in `../docs/API.md`; build against that, and SHA-256 the
upload from day one because custody is not a November feature.

Copy `.env.example` to `.env` locally. The real `.env` never enters git.
