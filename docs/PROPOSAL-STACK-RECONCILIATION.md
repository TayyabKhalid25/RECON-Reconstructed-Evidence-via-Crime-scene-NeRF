# Proposal / Gantt / diagram reconciliation (FTW-39)

**Purpose:** the tech stack diagram and Gantt contradict `docs/STACK.md`, and self-contradicting
documents are the cheapest question a defence panel can ask. This file is the authoritative
mapping to apply to those artifacts. Proposal is due **04 Sep**; defence **18 Sep**.

The diagram, Gantt and proposal are not in this repo (they are PDFs/DOCX held outside it), so this
is the correction list for whoever holds the files, not an edit. Facts only — the prose in graded
deliverables is written by the team.

Right-hand column is what is actually installed and verified, per `docs/STACK.md`.

## Apply these

| Item | Diagram / Gantt currently says | Change it to | Evidence |
|---|---|---|---|
| Prisma | 8 | **5.22.0** | `web/package.json`, `prisma` + `@prisma/client` |
| PostgreSQL | 18 | **16** | `postgres:16` in `web/docker-compose.yml` |
| Python | 3.14.7 | **3.11.16** | Miniforge env `recon` |
| Unity | 6.5 LTS | **see the Unity note below — do not just write "6 LTS"** | PR #14 |
| Track letters (Gantt) | A=Unity, B=Web, C=GPU | **A=GPU, B=Web, C=Unity** | Flowchart + stack diagram already agree; handbook Section 02 |
| Track owners (Gantt) | Faizan on Web, Wahaj second | **Wahaj sole owner of Web (Track B) *and* Track A; Faizan is Tayyab's second on Unity (Track C)** | Role change 30 Aug 2026, handbook Section 03 |

## Three traps — do NOT "fix" these

**1. `Next.js 16` in the diagram is correct. Leave it.** FTW-39's description lists it as a
mismatch, and the handbook flags it too, but both predate the install: the pinned entry reads
"App Router, current stable" and what is installed is **16.3.4** (React 19.2.8). Editing the
diagram down to an older major to match the 22 Aug proposal text would introduce an error, not
remove one. If anything the *proposal* is the document that should gain the exact number.

**2. `Node 20 LTS` is stale as a target.** FTW-39 says reconcile to Node 20 LTS. The pin was
deliberately changed to **22 LTS** and **v22.23.2** is installed (recorded in `docs/STACK.md` and
the 2026-09-01 session log; Next.js 16 requires Node 20.9+ anyway). Reconcile to **22 LTS**.

**3. The Gantt's owner names are stale, not just its track letters.** The 30 Aug role change moved
Faizan to Unity and gave Web entirely to Wahaj. A Gantt that fixes A/B/C but still shows Faizan on
Web is still wrong, and it is wrong about people, which reads worse in a defence than a version
number.

## Unity, flagged for verification rather than asserted

PR #14 records **Unity 6000.3.22f1** installed against a pin of "6 LTS". Whether the `6000.3`
stream is an LTS release, or whether the LTS is `6000.0.x`, has **not been checked against Unity's
own release notes**, and the difference matters because the proposal claims an LTS editor.

Someone on Track C should confirm before 18 Sep, and then either the pin or the installed version
needs to move so the claim is true. Do not paper over it by writing "Unity 6 LTS" in the diagram
while `6000.3.22f1` is what is on the machine.

Also note PR #14 is **not merged**, so those Unity rows are not yet in `docs/STACK.md` on main.
Confirm the final numbers from the merged branch before submitting.

## Verified, no change needed

These already agree across the proposal, `docs/STACK.md`, and the machine:

| Item | Value |
|---|---|
| OS (GPU track) | Ubuntu 22.04.5 LTS under WSL2 |
| CUDA toolkit | 12.6 (nvcc V12.6.85) |
| PyTorch | 2.7.1+cu126 |
| COLMAP | 3.13.0 (conda-forge, CUDA build) |
| Nerfstudio / splatfacto | 1.1.5 (gsplat 1.4.0) |
| Redis + BullMQ | Redis 7, BullMQ 6.3.4 |
| AR Foundation | 6.x |
| Physics | PhysX, bundled |

## Two claims in the proposal that first-light numbers now support

Not version reconciliation, but they are in the same "make the documents agree with reality" job
and the numbers exist as of 2026-08-26 (`docs/RESULTS.md`):

- The **8 GB VRAM bound** is stated as a limitation. Measured peak is **1197 MiB of 8188 (14.6 %)**
  on scene 1 and 1441 MiB (17.6 %) on scene 2. The honest framing is the handbook's: the *phone*
  is the ceiling, not the training GPU. That is a stronger position than the proposal currently
  takes, and it is measured.
- The **monocular scale limitation** is acknowledged. Scene 1 is now metric via the printed
  marker: **unitScale 0.369573**, with a 2.1 % spread across 27 view pairs. Quote the spread, and
  keep the caveat that the marker closure is circular by construction — an independent check
  against a second measured object is still outstanding (FTW-36).

## Done when

The diagram and Gantt match this table, the three traps are not "fixed" into errors, and the Unity
LTS question has an answer. Cross-check against merged `docs/STACK.md`, not this file, at submission
time — this file is a snapshot taken 2026-09-02.
