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

## Versions

Next.js **16**, React **19**, Node **22 LTS**, Prisma **5**. Recorded in `../docs/STACK.md`.

Next 16 is not the Next.js most examples assume, and two changes bite here:

- `params` in a route handler is a **promise**. `const { id } = await ctx.params`, typed with the
  generated `RouteContext<'/api/scenes/[id]'>` helper. Synchronous access was removed, not
  deprecated.
- `next lint` was removed. Lint with `npm run lint`, which calls `eslint` directly.

The bundled docs in `node_modules/next/dist/docs/` are the reference for this version; prefer them
over anything remembered about Next 14 or 15.

## Running it

```bash
npm run db:up          # Postgres 16 + Redis 7 in Docker, localhost only
npm run db:migrate     # first migration from prisma/schema.prisma
npm run db:seed        # one user per role, plus a case to upload into
npm run dev            # http://localhost:3000

npm run worker:throwaway   # in a second shell: proves the queue works
```

`npm run verify` runs typecheck, lint, and the custody chain checks together.

Seeded dev logins are `admin@recon.local` / `investigator@recon.local` / `viewer@recon.local`,
passwords `<role>-dev`. Dev only.

## What is implemented

Every endpoint in `../docs/API.md`, plus upload and custody verification:

| Route | Auth | Notes |
|---|---|---|
| `POST /api/auth/login` | none | returns a JWT |
| `POST /api/uploads` | session | video in, SHA-256 + custody row + PENDING job out |
| `POST /api/jobs` | session | create for an existing scene, idempotent per scene |
| `GET /api/jobs/next` | worker token | atomic claim, `FOR UPDATE SKIP LOCKED` |
| `PATCH /api/jobs/:id/status` | worker token | transitions validated against the state machine |
| `POST /api/jobs/:id/result` | worker token | attaches `.ply` + `metadata.json`, reads `unitScale` |
| `GET /api/scenes` | session | owner-scoped, `?status=READY` |
| `GET /api/scenes/:id` | session | metadata plus asset list |
| `GET /api/scenes/:id/asset` | session | streams the `.ply`, `X-Asset-SHA256` header |
| `GET /api/custody/verify` | admin | walks the hash chain, names the first bad row |

Storage is disk plus tunnel per FTW-13. `src/lib/storage.ts` is the only module that touches
paths, and `urlFor()` is the seam that would return a presigned URL if we ever moved to object
storage — `GET /api/scenes/:id/asset` already redirects when it returns non-null, so that switch
needs no contract change.

Worker routes authenticate with `WORKER_TOKEN` via `X-Worker-Token`, deliberately separate from
user JWTs so a leaked machine token cannot be replayed against investigator routes.

## Not done yet

- **Dashboard UI.** Only the scaffold's default page exists; the upload form and job status view
  are next (handbook W3, "job endpoints live, dashboard shell").
- **RBAC test per role per route.** The checks are in the handlers, but D7 (27 Sep) wants a test
  per role per route asserting the denial cases.
- **AES-256-GCM at rest.** D12. Custody hashing is in place; encryption is not.
- **Anchor endpoints.** `POST`/`GET /api/scenes/:id/anchor` are in the contract for D12, not built.
- **No migration has been run yet.** `prisma/migrations/` is empty until Docker is installed — see
  below.

## Custody

`src/lib/custody.ts` implements the append-only hash chain from the contract. Two invariants:
rows are only ever appended, and appends are serialised with a Postgres advisory lock so two
concurrent writes cannot fork the chain by both building on the same predecessor.

The row hash covers the ISO-8601 timestamp, so `AuditLog.timestamp` is `@db.Timestamp(3)`.
JS `Date` carries only milliseconds; at Postgres's default microsecond precision a value would not
round-trip identically and every verification would fail on read-back. Do not widen that column.

`npm run check:custody` proves the properties D12 is graded on without needing a database,
including that editing a row is caught at that row and that recomputing its hash to match still
breaks the chain at the next row.
