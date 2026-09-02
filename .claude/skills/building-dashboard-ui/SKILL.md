---
name: building-dashboard-ui
description: Use when building the web dashboard: case lists, upload flow, job status display, scene viewer pages, or when the UI hammers the API with polling or shows stale or blank states.
---

# Building the dashboard UI

## Overview

The dashboard is what the panel sees first at every demo. It needs to look deliberate, show job state truthfully, and not melt the free tiers. Next.js App Router, Tailwind, and a component library (shadcn/ui) rather than hand-rolled CSS.

## The rules

- **Server components for lists, client components for anything live.** The case list renders on the server from Prisma; only the job-status widget and upload form need `"use client"`.
- **Poll the web API, never Redis, and poll gently.** SWR or React Query with `refreshInterval` around 5s, only while a job is in PENDING or PROCESSING, stopping on READY/FAILED. Free Redis is metered; free Postgres is not built for hammering either.
- **Every async view has four states designed, not defaulted:** loading (skeleton, not spinner-on-white), empty ("No cases yet" with the create action), error (the API's error string plus a retry), and data. A blank div during a demo reads as broken.
- **Upload shows real progress.** `fetch` cannot report upload progress; use `XMLHttpRequest` (or a lib wrapping it) for the video POST and drive a progress bar from `upload.onprogress`. A 300 MB upload with no feedback looks frozen at exactly the wrong moment.
- **Job status is a single component** mapping the five states to badge colour and copy: PENDING grey, PROCESSING blue with progress percent, READY green, FAILED red **showing the worker's error detail verbatim**, CANCELLED grey. The FAILED detail on screen is what makes remote debugging between three cities possible.

## Structure

```
app/(dashboard)/cases/page.tsx          server: list cases
app/(dashboard)/cases/[id]/page.tsx     server: case detail, scenes, custody log
components/job-status-badge.tsx         client: polls /api/jobs/:id
components/upload-dropzone.tsx          client: XHR upload with progress
lib/api.ts                              typed fetch wrappers, one error shape
```

## The scene viewer

Rendering splats in the browser is its own decision, written up in `docs/VIEWER-AND-EDITING.md`. Do not pick a renderer ad hoc — FTW-59 measures Spark against PlayCanvas against GaussianSplats3D on our real 178 k scene, and the winner gets pinned in `docs/STACK.md`.

Two rules the viewer must not get wrong, because both produce confidently incorrect output:

- **Say which asset is on screen.** The viewer loads a lossy delivery format (SOG or SPZ); the archival `.ply` is what `sha256` covers and what measurements come from. A user who measures on a quantised scene and quotes it in a report is a bug we can design out.
- **Refuse `unitScale == 0.0` loudly**, exactly as the Unity client does. A non-metric scene must not be silently navigable as though it were metric. See `docs/FRAMES.md`.

No anchoring is involved anywhere in the web viewer — there is no physical room to align to.

## Demo-day gotchas

| Symptom | Cause |
|---|---|
| Status stuck on PROCESSING in the UI, DB says READY | Poll stopped early or cache not invalidated on interval |
| Dashboard fine locally, unstyled flash in prod | Tailwind content globs missing a folder |
| Custody log renders slowly | Rendering the full chain; paginate, verify server side |
| Layout collapses on the projector | Only tested at laptop width; check 1024px and phone width, the panel will see both |
