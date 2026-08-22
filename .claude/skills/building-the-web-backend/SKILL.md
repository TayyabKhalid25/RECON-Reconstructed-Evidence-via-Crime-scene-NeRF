---
name: building-the-web-backend
description: Use when writing Next.js App Router route handlers, Prisma queries, JWT auth, RBAC checks, or request validation, or when hitting "too many Prisma clients" in dev or inconsistent API error shapes.
---

# Building the web backend

## Overview

Next.js App Router route handlers implementing `docs/API.md`, Prisma 5 on PostgreSQL 16, hand-rolled JWT (the `jose` library) with three roles. The patterns below prevent the four bugs every first Next.js backend hits.

## Prisma client is a singleton

Dev hot-reload creates a new client per reload and exhausts Postgres connections (free tiers have low caps). One module, imported everywhere:

```ts
// lib/db.ts
import { PrismaClient } from "@prisma/client";
const g = globalThis as unknown as { prisma?: PrismaClient };
export const prisma = g.prisma ?? new PrismaClient();
if (process.env.NODE_ENV !== "production") g.prisma = prisma;
```

## Every route handler has the same skeleton

Validate → authorize → act → uniform response. Zod at the boundary, so bad input is a 400 with a message, never a 500 three layers deep:

```ts
// app/api/jobs/[id]/status/route.ts
const Body = z.object({
  status: z.enum(["PROCESSING", "READY", "FAILED", "CANCELLED"]),
  error: z.string().optional(),
  progress: z.number().min(0).max(100).optional(),
});

export async function PATCH(req: Request, { params }: { params: { id: string } }) {
  const auth = await requireRole(req, "WORKER");          // 401/403 short-circuit
  if (auth instanceof Response) return auth;
  const parsed = Body.safeParse(await req.json());
  if (!parsed.success) return json(400, { error: parsed.error.flatten() });
  // ... guarded transition per building-the-job-pipeline
  return json(200, { ok: true });
}
```

One error shape everywhere: `{ error: string | object }` with the right status code. The Unity client and the worker both parse this; do not improvise per route.

## Auth and RBAC

- JWT signed with `jose`, secret from env, short expiry plus refresh. Payload: `{ sub, role }` and nothing sensitive.
- Roles are an enum in Prisma (`ADMIN`, `INVESTIGATOR`, `WORKER`). Authorization is **server side per route**, never a hidden button. The test that proves it: an INVESTIGATOR fetching another investigator's case gets a 403 from a real request.
- Workers authenticate with the same mechanism (a long-lived token with role WORKER), so `/api/jobs/*` is not open to the internet through the tunnel.

## Gotchas

| Symptom | Cause |
|---|---|
| "Too many connections" from Postgres in dev | Non-singleton Prisma client |
| Route works in dev, 405 in prod | Exported the wrong HTTP method name from the route file |
| Handler returns HTML error page to the worker | Threw instead of returning a Response; wrap in try/catch, return the uniform error shape |
| Free-tier Postgres "outage" after idle days | Tier suspends idle databases; wake it, and expect the first query to be slow |
