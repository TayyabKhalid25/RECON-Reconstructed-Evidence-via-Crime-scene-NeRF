# Every step, from today to final evaluation

*Forensic NeRF Reconstructor · Build Handbook*

One document for the whole project: what to build, who builds it, in what order, on which machine, and what has to be true by each deadline. Written to be the single source of truth. Where it disagrees with the proposal, flowchart, or Gantt, fix those to match this, or change this deliberately.

- **Team:** Tayyab · Wahaj · Faizan
- **Today:** Fri 21 Aug 2026
- **Final eval:** Fri 11 Dec 2026
- **Weeks left:** 16
- **Budget:** 0 USD
- **GPUs:** 4060 · 4060 · 4050

## Contents

- **00** Read this first
- **01** What you are building
- **02** People, machines, devices
- **03** Blocking decisions
- **04** The pinned stack
- **05** Week 0 setup sprint
- **06** The contract
- **07** Track A: GPU
- **08** Track B: Web
- **09** Track C: Unity AR
- **10** Integration
- **11** Scale and coordinates
- **12** Evaluation plan
- **13** Deliverables and test gates
- **14** Calendar to Dec 11
- **15** Risk register
- **16** Graded deliverables
- **17** Working agreements
- **18** Beyond the deadlines

## Section 00. Read this first

The project proposal is due **Fri 04 Sep** and the proposal defence is **18 Sep**. As of the 22 Aug draft the proposal names the advisor (Mr. Saifullah Tanvir), commits to Nerfstudio splatfacto, adopts printed markers plus ARCore Cloud Anchors, pins one version set, states the 8 GB and 6 GB VRAM bounds, and acknowledges the monocular scale limitation. The technical content is sound. What remains is layout, completeness, and verification, not re-design. Nothing in this document matters more than the five items below, and most of them are not code.

> [!IMPORTANT]
> **Do these five things this week**
>
> 1. **Fix the proposal's remaining layout and completeness defects.** A stray page break strands one orphaned line on page 2 and leaves page 4 entirely blank, the most visible defect in the document. References [9], [10] and [11] sit in the reference list but are cited nowhere in the body ([9] belongs on the NeRF mention in Section 4, [10] on photogrammetry degrading on forensic materials, [11] on reflective and transparent surfaces in the introduction). There is still no timeline, no risk table, and no dataset strategy section, and all three already exist elsewhere (the Gantt, Section 15 here, and the superseded original proposal), so this is copy and adapt, not new writing.
> 2. **Get the proposal onto the official template.** Submission is on `D1 Project Proposal Template.dotx`. The current document appears to be its own formatting, so budget half a day to move it across, and do that before you polish the content.
> 3. **Verify ARCore Cloud Anchors against live sources** (Section 03). The proposal now names it directly, so it is the load bearing anchoring dependency. Confirm availability, free quota, and AR Foundation support. Also confirm Azure Spatial Anchors really is retired, so the "why not ASA" defence answer is grounded in a checked fact.
> 4. **Confirm your AR devices.** Which phones do you actually have, and are they on Google's ARCore supported list or Apple's ARKit list? Track C cannot start without one working device.
> 5. **Get COLMAP producing camera poses on Wahaj's Legion.** First real technical milestone, and it upgrades the proposal too: Section 4 is titled "Work Done so Far" but contains only decisions, and one paragraph of real numbers (frames registered, training minutes, peak VRAM, PSNR) beats everything else in it.

### Conventions used in this document

| Mark | Meaning |
|---|---|
| [T] [W] [F] [ALL] | Owner. Tayyab, Wahaj, Faizan, or whole team. |
| `verify` | Version numbers, service availability, and API names that move fast. I could not check these against live sources while writing, so treat them as a starting point and confirm against official docs before you depend on them. Where a claim is load bearing I say so explicitly. |
| `gate` | A hard date. Something must be demonstrably working, not "nearly working". |

Checkboxes in this document save to your own browser, so each of you can track your own progress independently. They do not sync between people.

## Section 01. What you are building

In one sentence: a phone records a walkthrough of a room, a CUDA machine turns that video into a 3D Gaussian splat scene, a web app stores and serves it with a case record, and a Unity AR app puts that scene back on top of the real room and runs physics inside it.

### The pipeline, as six handoffs

| # | Stage | Runs on | Produces |
|---|---|---|---|
| 1 | Capture walkthrough video | Phone | scene.mp4 |
| 2 | Upload, create case, enqueue job | Web (Next.js) | Job(status=PENDING) |
| 3 | Frames, SfM poses, splat training | GPU worker | scene.ply + metadata.json |
| 4 | Store asset, mark ready, log custody | Web | Job(status=READY) |
| 5 | Fetch, align to room, render splats | Unity AR | Anchored digital twin |
| 6 | Ballistics and spatter overlay | Unity + PhysX | Simulation overlay |

> [!WARNING]
> **Naming conflict in your own documents**
>
> Your three PDFs do not agree on the track letters. The flowchart and the tech stack diagram both say A=GPU, B=Web, C=Unity. The Gantt says A=Unity, B=Web, C=GPU. **This document uses A=GPU, B=Web, C=Unity** because two of three documents already do. Fix the Gantt, and do it before the defence, because "your own documents contradict each other" is the cheapest question a panel can ask.

### Scope discipline

The proposal commits to a lot. Build it in this order, and treat everything in the right column as explicitly deferred until the left column demonstrably works end to end.

| Area | First (by midterm, 16 Oct) | Second (by final, 11 Dec) |
|---|---|---|
| Reconstruction | One room, downscaled, capped splat count | 3 physical plus 3 synthetic scenes, adverse conditions |
| Auth | None, or a single hardcoded login | JWT sessions plus RBAC across three roles |
| Custody | SHA-256 at upload, stored | Append only hash chained audit log, verify on read |
| Encryption | TLS in transit only | AES-256-GCM at rest on assets and sensitive columns |
| AR alignment | Image marker, single device | Cloud anchor persistence, two devices concurrently |
| Colliders | Detected AR planes | Poisson mesh from splat centres, error measured |
| Physics | Ballistic arc with drag | Spatter ellipse proxy, parameter UI |

## Section 02. People, machines, devices

| Person | Owns | CUDA machine | Second machine |
|---|---|---|---|
| [T] Tayyab | Track C lead, Unity AR | Desktop PC, RTX 4060, 8 GB | Surface Book, i7, no CUDA |
| [W] Wahaj | Track A lead, Track B lead | Legion 5 16IRX9, i9 14th gen, RTX 4060, 8 GB | MacBook Air M5 13" |
| [F] Faizan | Track C second (with Tayyab), Track A second pair of hands | Victus 15, i5 13th gen, RTX 4050, 6 GB | MacBook Air M5 13" |

> [!NOTE]
> **Role change, 30 Aug 2026**
>
> Original split had Faizan leading Track B (Web) with Wahaj as second pair of hands. As of 30 Aug, **Faizan moves to Track C (Unity) as Tayyab's second**, and **Wahaj takes sole ownership of Track B** in addition to leading Track A. Faizan's Track A second-hand duties (frame extraction, SfM on the Victus) are unchanged. Every `[F]` tag elsewhere in this document that referred to Web/Track B work now reads as `[W]`; `[F]` on a GPU task still means Faizan.

### What this inventory actually means

- **Wahaj's Legion is the primary training box.** i9 plus 8 GB 4060 is your strongest single machine and COLMAP leans hard on CPU. Primary scenes train here.
- **Tayyab's desktop 4060 is a wasted asset unless you claim it.** He is doing Unity work, so his GPU sits idle. Register it as a third queue worker in week 3 and it doubles your reconstruction throughput for free.
- **Faizan's 4050 has 6 GB and is the tightest.** Use it for frame extraction and SfM, not for the scenes you are going to put in the report. Serving the web API is now Wahaj's responsibility.
- **Neither MacBook can train.** No CUDA. They are for Unity, Xcode, Next.js, and writing. Do not plan any reconstruction on them.
- **Tayyab owns Unity but has no Mac.** iOS builds need Xcode, which needs macOS. Decide now which of Wahaj or Faizan lends their MacBook Air on iOS build days, or accept Android first and iOS second.

> [!NOTE]
> **iOS on zero budget**
>
> You do not need the 99 USD Apple Developer Program to run your own app on your own iPhone. A free Apple ID does personal provisioning through Xcode, with the catch that the build expires after roughly 7 days and needs re-signing. That is fine for development and for a live demo. You only need the paid program for TestFlight or the App Store, which you do not need. `verify`

#### Device checklist [ALL]

- [ ] List every phone the three of you own, with model and OS version
- [ ] Check each Android against Google's ARCore supported devices list. Not all Androids qualify
- [ ] Check each iPhone supports ARKit. Note which have LiDAR, since those give you a free scene mesh for colliders and a free ground truth scanner
- [ ] Nominate one primary AR test device and one secondary, since multi user testing needs two
- [ ] Decide which MacBook Air is the iOS build machine and tell Tayyab

> [!WARNING]
> **Thermals**
>
> The Legion and the Victus are laptops. A 30 minute splat training run at full tilt will throttle on battery or on a soft surface. Plug in, set the vendor performance profile, raise the back of the laptop, and expect roughly 20 to 45 minutes per small scene. If training time doubles between runs, check temperature before you blame your config.

## Section 03. Blocking decisions, resolve in the first 48 hours

Each of these changes what you build. None of them should be discovered in November.

> [!IMPORTANT]
> **Decision 1 · Anchoring: adopted in the proposal, still unverified**
>
> The 22 Aug proposal draft already names ARCore Cloud Anchors plus printed image markers, so the retired Azure Spatial Anchors dependency is out of the document. The design splits "align the twin to the room" from "persist and share that alignment", which is the right shape:
>
> - **Alignment: a printed image marker of known physical size.** Use AR Foundation's tracked image manager. A marker gives you position, rotation, *and* real world scale in one detection, it is deterministic, it works in a bare white room, and it directly neutralises the feature poor environment risk. Print an A4 or A3 target, measure it, tape it to the floor at scene origin.
> - **Persistence and multi user: ARCore Cloud Anchors,** reachable from both Android and iOS through Google's ARCore Extensions package for AR Foundation. It hosts an anchor to the cloud and resolves it on another device or another session. There is a free quota and anchors can persist for up to a year. `verify`
>
> **Two verifications remain, and neither has been done against live sources.** First, Cloud Anchors is now your load bearing anchoring dependency: confirm availability, the free quota, the persistence window, and that ARCore Extensions supports your Unity and AR Foundation versions. Second, the switch away from ASA rests on the understanding that Microsoft wound it down around late 2024; check that too, because "we identified that a proposed dependency was retired and re-architected around it" is a genuinely good defence answer only if it is a checked fact.

> [!WARNING]
> **Decision 2 · Versions: the proposal now pins one set, make the diagrams match**
>
> The 22 Aug proposal pins Node 20 LTS, Next.js (App Router), Prisma 5, PostgreSQL 16, Redis, Ubuntu 22.04 (WSL2), CUDA 12.x, Python 3.10/3.11, COLMAP 3.x, Nerfstudio, and AR Foundation 6.x, which matches Section 04 here. The tech stack diagram still says Next.js 16, Prisma 8, PostgreSQL 18, Unity 6.5 LTS, Python 3.14.7. Update the diagram and the Gantt to the proposal's set before the defence, because "your own documents contradict each other" is the cheapest question a panel can ask.
>
> **One specific warning.** Do not use a bleeding edge Python for the GPU track. PyTorch and CUDA wheels lag new Python releases by months, and a Python that has no matching torch build will cost you a week for zero benefit. Use whatever version your reconstruction toolchain's own install docs specify, which will realistically be 3.10 or 3.11.

> [!NOTE]
> **Decision 3 · Reconstruction framework: resolved, Nerfstudio splatfacto**
>
> The 22 Aug proposal no longer cites Instant-NGP or the raw INRIA repo. It commits to Nerfstudio's `splatfacto`, which wraps data processing, training, evaluation, and `.ply` export behind three commands, uses a memory efficient rasteriser, and avoids compiling `tiny-cuda-nn`, a classic multi day install trap for students. Nothing left to decide here, just install it and confirm it runs on the Legion.
>
> If CUDA becomes a problem on any machine, **OpenSplat** and **Brush** can train splats without CUDA, which would let the MacBooks contribute. Worth 20 minutes of reading as a fallback, nothing more. `verify`

> [!NOTE]
> **Decision 4 · Where files live**
>
> Cloudflare R2 has a genuinely useful free tier and charges nothing for egress, but object storage providers generally want a payment method on file even at zero usage, which conflicts with a hard zero budget. `verify` Decide now between: put a card on file and use R2, or keep `.ply` files on the GPU machine's disk and expose them through a tunnel. For a semester project the second option works fine and costs nothing. Do not commit `.ply` files to git either way, they are large binaries and will wreck the repo.

> [!WARNING]
> **Decision 5 · Development or R&D, and it matters more than it looks**
>
> You are registered as a **Development** project, so you write chapters 1, 2, 3, 4 and 6 for DII, then 7 and 10 for DIII. R&D projects instead write a full literature review chapter, a methodology chapter, and **Chapter 9, Experimental Results and Discussion**.
>
> Here is the tension. The proposal's Objective 5 commits to a quantitative evaluation across mock scenes: reconstruction accuracy, anchor drift, physics plausibility, system latency, and custody overhead, with numeric thresholds like 28 dB PSNR and 2 cm elsewhere in the objectives. That is R&D shaped content, and as a Development project you have no results chapter to put it in. It is not fatal, you write it as test cases in Chapter 7 instead (Section 16), and plenty of strong Development projects do exactly that. An earlier draft had a sentence saying exactly this, that as a Development project the quantitative questions are framed as software test cases, and the current draft dropped it. Restore it, because it pre-empts precisely the question a panel will ask.
>
> **But ask your supervisor in your first meeting** which classification actually serves you, while changing it is still cheap. If the quantitative evaluation is the intellectual core of the project, R&D may be the better home for it. If the deliverable is the working system, stay Development and treat the evaluation as validation. Either answer is defensible. Discovering the mismatch in November is not.

#### Decisions to record [ALL]

- [x] Advisor confirmed and named in the proposal (Mr. Saifullah Tanvir, 22 Aug draft)
- [ ] Development versus R&D classification confirmed with the supervisor
- [ ] Official proposal template obtained and content moved into it
- [ ] Cloud Anchors verified against live sources, ASA retirement confirmed for the defence answer
- [x] One version set pinned in the proposal (22 Aug draft), pending: tech stack diagram and Gantt updated to match
- [ ] Reconstruction framework confirmed by actually installing splatfacto and training once
- [ ] Asset storage decided, card or tunnel
- [ ] Android first or iOS first decided

## Section 04. The pinned stack

Fill the right column in once, on day one, with the exact versions you install. Then nobody upgrades anything mid semester without telling the other two. A silent minor version bump that breaks one track in November is a self inflicted wound.

| Track | Component | Guidance | Pinned |
|---|---|---|---|
| [W] A | OS for GPU work | Ubuntu 22.04 under WSL2 on Windows | ___ |
| [W] A | CUDA toolkit | 12.x, matched to your torch wheel | ___ |
| [W] A | Python | 3.10 or 3.11, whatever your framework's docs say | ___ |
| [W] A | SfM | COLMAP 3.x. GLOMAP is a faster global alternative worth testing later `verify` | ___ |
| [W] A | Splat training | Nerfstudio splatfacto, or INRIA reference | ___ |
| [W] A | Serving | FastAPI plus a queue worker, boto3 if using S3 compatible storage | ___ |
| [W] B | Runtime | Node 20 LTS or newer LTS | ___ |
| [W] B | Framework | Next.js, App Router | ___ |
| [W] B | ORM and DB | Prisma plus PostgreSQL | ___ |
| [W] B | Queue | Redis plus BullMQ | ___ |
| [W] B | Auth | JWT sessions, roles in DB | ___ |
| [W] B | Local dev | Docker Compose for Postgres and Redis | ___ |
| [T] C | Editor | Unity 6 LTS, pin the exact patch `verify` | ___ |
| [T] C | Pipeline | URP | ___ |
| [T] C | AR | AR Foundation 6.x plus ARKit and ARCore provider packages | ___ |
| [T] C | Splat renderer | aras-p UnityGaussianSplatting, MIT `verify` | ___ |
| [T] C | Anchors | Per Decision 1 | ___ |
| [T] C | Physics | PhysX, bundled with Unity | ___ |

Keep this table in the repo as `STACK.md` and update it in the same commit as any upgrade.

## Section 05. Week 0 setup sprint, 21 to 30 Aug

Goal by end of 30 Aug: all three tracks scaffolded, all three machines able to talk to each other, and one real reconstruction finished. Work in parallel, do not wait on each other.

### Shared infrastructure [W]

Redis and Postgres need to be reachable from three different networks, so host them centrally and have every machine connect outward. No port forwarding, no static IPs.

- [ ] Create a private GitHub repo, add all three as collaborators
- [ ] Monorepo layout: `/gpu`, `/web`, `/unity`, `/docs`, plus `STACK.md` and a strict `.gitignore`
- [ ] Managed Postgres free tier, connection string shared privately
- [ ] Managed Redis free tier, connection string shared privately
- [ ] Tailscale on all three laptops, both MacBooks, and the AR test phones. Free tier covers you and this is how the phone reaches a dev API
- [ ] A shared `.env.example` committed, real `.env` never committed

> [!NOTE]
> **Free tier notes**
>
> Limits and terms change, so check the pricing page rather than trusting a number here. `verify` Two gotchas that actually bite student projects: some free Postgres tiers **suspend a database after a period of inactivity**, which will look like a mysterious outage the week you come back to it, and some free Redis tiers meter by request count, so do not write a worker that polls several times a second. Poll every few seconds, or use a blocking pop.

### Track A first light [W]

On the Legion, in this order. Do not skip ahead, each step tells you whether the previous one actually worked.

- [ ] Install WSL2 with Ubuntu 22.04 from Windows
- [ ] Update the Windows NVIDIA driver. Do not install a Linux GPU driver inside WSL, the Windows driver provides the GPU
- [ ] Run `nvidia-smi` inside WSL and see the 4060 listed. If not, stop and fix this before anything else
- [ ] Install the CUDA toolkit for WSL Ubuntu from NVIDIA's repo, then confirm `nvcc --version`
- [ ] Install Miniforge, create a clean env on the Python version your framework specifies
- [ ] Install torch built for your CUDA version, then confirm `torch.cuda.is_available()` is True
- [ ] Install COLMAP. Confirm it runs and reports whether CUDA is enabled
- [ ] Install the splat training framework, let its CUDA extensions compile, fix errors now not later
- [ ] Shoot a 60 second test video of a desk or a corner of a room
- [ ] Extract frames and run SfM. Success looks like a sparse point cloud plus per frame camera poses
- [ ] Train a splat scene at reduced resolution. Note peak VRAM, wall clock time, and final metrics
- [ ] Export a `.ply`, note its file size and splat count, hand it to Tayyab

> [!WARNING]
> **Two WSL traps**
>
> **Filesystem speed.** Keep datasets and outputs inside the Linux filesystem under your home directory. Working out of `/mnt/c/...` crosses a filesystem boundary on every file read and will make frame extraction and training crawl for no visible reason.
>
> **Memory ceiling.** WSL2 caps how much system RAM it takes by default. If SfM dies on a larger frame set, raise the limit in a `.wslconfig` file on the Windows side before you assume the scene is too big.

### Track B first light [W]

- [ ] Scaffold the Next.js app with TypeScript and the App Router in `/web`
- [ ] Docker Compose with Postgres and Redis for local work, so you are not dependent on the cloud tier while iterating
- [ ] Prisma schema from Section 06, then run the first migration
- [ ] Implement the three job endpoints, even if nothing consumes them yet
- [ ] Implement video upload to wherever Decision 4 landed
- [ ] Enqueue a job on upload, and write a throwaway worker that just flips status, to prove the queue works
- [ ] Compute SHA-256 of the upload and store it. Custody starts on day one, it is not a November feature

### Track C first light [T]

- [ ] Install Unity 6 LTS with Android build support, plus iOS build support on the designated MacBook
- [ ] New URP project in `/unity`, commit with a proper Unity `.gitignore`
- [ ] Add AR Foundation and the ARCore and ARKit provider packages, configure XR Plug-in Management
- [ ] Build the stock AR sample to a real phone. Camera feed plus plane detection on device before anything else
- [ ] Add the Gaussian splat renderer package, get a sample `.ply` rendering in the editor
- [ ] Build that same splat scene to the phone and measure frame rate. This is the real risk in Track C, find out in week 1
- [ ] Enable Unity Smart Merge (UnityYAMLMerge) so scene and prefab merge conflicts are survivable

> [!IMPORTANT]
> **The single most important measurement in week 1**
>
> Splat renderers were mostly built for desktop GPUs. Mobile is a different world. **Get a real splat file onto a real phone and read the frame rate before you build anything on top of it.** If it renders at 8 fps with a million splats, you need to know that in August, when your options are still open, not in November. Section 15 has the fallbacks, and they all take time you will not have later.

## Section 06. The contract

This is what makes three people working in three places actually parallel. Agree it in one sitting, write it into the repo, and treat a change to it as something that needs all three of you to know. If the contract drifts silently, you will spend the integration window debugging mismatches instead of integrating.

### Job state machine

Five states, one direction, no surprises. Anything unexpected ends at `FAILED` with a readable reason, because a job that silently vanishes is the worst thing to debug.

```
PENDING  ->  PROCESSING  ->  READY
   |              |
   +----------->  FAILED  (with error text)
                  CANCELLED  (user asked to stop)
```

### Data model [W]

Minimum viable, with custody built in from the start. Add fields as you need them, but do not remove the audit table.

```
// Case      : one investigation, owns many scenes
// Scene     : one capture, owns one reconstruction job
// Job       : reconstruction work, status per the machine above
// Asset     : an uploaded or produced file, with sha256 and byte size
// User      : id, email, passwordHash, role enum
// AuditLog  : append only. userId, action, targetType, targetId,
//             timestamp, prevHash, rowHash
```

> [!NOTE]
> **Why prevHash matters**
>
> Each audit row stores a hash computed over its own contents plus the previous row's hash. That makes the log tamper evident: change or delete any earlier row and every subsequent hash stops verifying. This is a few lines of code, it directly answers your Challenge 4 research question, and it gives you something concrete to measure for the report, which is the overhead it adds per write.

### Endpoints

#### Web, consumed by GPU workers

```
POST   /api/jobs                  create, returns { jobId }
GET    /api/jobs/next             worker claims one PENDING job
PATCH  /api/jobs/:id/status       { status, error?, progress? }
POST   /api/jobs/:id/result       attach .ply and metadata.json
```

If you use BullMQ properly the worker gets jobs off Redis rather than polling `/next`. Keep the endpoint anyway, it makes manual testing and recovery from a stuck queue trivial.

#### Web, consumed by the Unity client

```
POST   /api/auth/login            returns a token
GET    /api/scenes?status=READY    list, newest first
GET    /api/scenes/:id            metadata plus asset URLs
GET    /api/scenes/:id/asset      the .ply, or a redirect to it
POST   /api/scenes/:id/anchor     store cloud anchor id and transform
GET    /api/scenes/:id/anchor     second device resolves the same anchor
```

Those last two are how the second investigator sees the twin in the same place. Store the anchor identifier plus the transform from anchor space to scene space, so the alignment survives across sessions and devices.

### metadata.json, written by the GPU worker

Unity cannot place a scene correctly without this. Every field here exists because something breaks without it.

```
{
  "sceneId":       "...",
  "plyFile":       "scene.ply",
  "splatCount":    842113,
  "sourceFrames":  240,
  "handedness":    "right",          // so Unity knows what to flip
  "upAxis":        "y",              // or z, be explicit
  "unitScale":     0.0,              // metres per scene unit, see Section 11
  "scaleMethod":   "marker|lidar|none",
  "boundingBox":   { "min": [0,0,0], "max": [0,0,0] },
  "originHint":    [0,0,0],          // marker centre if detected
  "metrics":       { "psnr": 0.0, "ssim": 0.0, "lpips": 0.0 },
  "training":      { "iterations": 0, "minutes": 0.0, "peakVramMb": 0 },
  "sha256":        "...",
  "createdAt":     "..."
}
```

#### Contract sign off [ALL]

- [ ] All three have read the state machine and agree
- [ ] Endpoint list committed to the repo as `docs/API.md`
- [ ] `metadata.json` shape committed as a real example file, not prose
- [ ] A hand written sample `metadata.json` and a sample `.ply` committed so Web and Unity can build against them before Track A produces real output

## Section 07. Track A, GPU reconstruction [W]

### Capture technique, the cheapest quality win you have

Reconstruction quality is set at capture time. No amount of training fixes a bad video, and your PSNR objective lives or dies here. Write this into a one page capture protocol and follow it every single time, including for throwaway tests, so your results are comparable.

- **Move slowly and smoothly.** Walking pace, no sudden pans. Motion blur destroys both SfM and training.
- **Lock exposure, focus, and white balance** before you start. Auto adjustments mid capture make the same surface look like two different materials.
- **Bright, even light.** High shutter speed beats a clean ISO here. Blur is worse than grain.
- **Two or three loops at different heights.** Knee height, chest height, above head. Single height orbits give weak vertical coverage and the reconstruction shows it.
- **Keep 70 percent or more overlap** between adjacent frames, and never zoom during capture.
- **Include the surroundings,** not just the object of interest. Background features are what SfM uses to solve camera pose.
- **Put a scale reference in every scene.** Non negotiable, see Section 11.
- 1080p60 or 4K30, then extract roughly 200 to 400 frames. More frames is not automatically better, it costs SfM time and adds redundant views.

### The reconstruction run

1. **Frames.** Extract a target frame count with even spacing, dropping blurry frames if your tool supports it.
2. **SfM.** Feature extraction, matching, and sparse reconstruction. Output is camera intrinsics, per frame poses, and a sparse cloud. **Check it before training.** If fewer than about 80 percent of frames registered, or the cameras form an implausible path, retrain nothing. Recapture instead. Training on bad poses wastes 40 minutes and produces a scene that looks melted.
3. **Train.** Start with fewer iterations than the default to get a fast feedback loop, then raise it once the scene is known good.
4. **Evaluate.** Compute held out PSNR, SSIM, and LPIPS. Record them in `metadata.json` for every run. You will need this table for the report and reconstructing it later from memory is impossible.
5. **Export.** Write the `.ply`, record the splat count, upload, and mark the job READY.

### Fitting into 8 GB, and 6 GB

The proposal now honestly states the 8 GB and 6 GB VRAM bounds instead of the original 24 GB floor, but stating the bound is not the same as fitting inside it. Turn these knobs, in this order, and record what you settled on because it belongs in the report as an engineering constraint you handled.

| Knob | Effect | Cost |
|---|---|---|
| Downscale training images | Biggest single VRAM saving | Less fine detail, lower PSNR ceiling |
| Cap maximum splat count | Directly bounds memory and file size | Softer geometry in dense regions |
| Raise the densification threshold | Fewer new splats spawned | Under reconstructs high frequency areas |
| Prune low opacity splats harder | Cuts count with little visual loss | Can eat thin structures |
| Fewer iterations | Time, not memory | Under trained, blurrier |
| Smaller scene bounds | Crop to the room, drop far background | Loses context outside the crop |

> [!NOTE]
> **The constraint that dissolves this problem**
>
> Your delivery target is a phone, and a phone cannot smoothly render millions of splats regardless of what your training GPU could produce. So you were always going to ship a decimated scene. Establish your mobile splat budget first, from the measurement in Section 05, then train to that budget. Framed that way, **8 GB is not your bottleneck, the phone is**, and your hardware limitation mostly stops mattering. Put exactly that argument in the report.

### Three workers, three cities

From week 3, run the same worker on all three CUDA machines pointed at the shared Redis. Each connects outward, claims a job, works, and reports back. Nobody needs to be online at the same time and nobody coordinates by hand.

- **Legion,** Wahaj. Primary. The scenes that go in the report train here.
- **Desktop,** Tayyab. Second scenes and parameter sweeps. He does not have to operate it, jobs arrive by queue.
- **Victus,** Faizan. Frame extraction and SfM. Not report grade training.

Record which machine produced which result. When two machines disagree, and they will, you want to know which is which.

#### Track A milestones

- [ ] CUDA, COLMAP, and training framework all working on the Legion
- [ ] First `.ply` exported from your own capture
- [ ] Capture protocol written to `docs/CAPTURE.md`
- [ ] VRAM safe preset found and recorded for 8 GB
- [ ] FastAPI worker claiming jobs from the shared queue
- [ ] Metric scale solved and written into `metadata.json`
- [ ] All three machines registered as workers
- [ ] Poisson mesh export path working, for colliders
- [ ] Three physical scenes and three synthetic scenes reconstructed with metrics recorded

## Section 08. Track B, web platform [W]

Least uncertain track technically, which makes it the one to keep ahead of schedule so it is never the thing blocking integration.

### Build order

1. **Schema and migrations.** Everything else depends on the shape of the data.
2. **Upload and enqueue.** Accept a video, store it, hash it, create case, scene, job, enqueue.
3. **Worker contract.** Claim, progress, result, failure with a readable reason.
4. **Case dashboard.** List cases, show job status live, preview the result, download the asset.
5. **Auth.** Login, JWT session, three roles: investigator, supervisor, administrator.
6. **RBAC enforcement.** On the server, in one place. Never rely on hiding a button.
7. **Custody and audit.** Log every read and write of an asset, hash chained.
8. **Encryption at rest.** AES-256-GCM on assets and on sensitive columns.
9. **Hardening.** Rate limiting, input validation, consistent error responses, no stack traces to clients.

> [!WARNING]
> **RBAC mistake to avoid**
>
> Checking roles only in the UI is not access control, it is decoration. Enforce on every server route with one shared helper, and write a test per role per route asserting the failure case, not just the success case. A panel asking "what stops an investigator downloading another investigator's case" needs a code answer, not a screenshot.

> [!NOTE]
> **On claiming AES-256 at rest**
>
> Three different things get called encryption at rest, and they are not equally strong. Say which one you did.
>
> 1. **Provider disk encryption.** You get it for free and it protects almost nothing in your threat model, since your own app and anyone with DB credentials still read plaintext.
> 2. **Column encryption in the database.** Better. Sensitive fields are ciphertext at rest.
> 3. **Application level envelope encryption.** Encrypt the asset bytes with AES-256-GCM before they ever leave your server, keep the key outside the database, store the IV and auth tag alongside the ciphertext. Strongest of the three and still a modest amount of code.
>
> Do option 3 for assets and option 2 for sensitive columns, then state plainly in the report where the key lives and what an attacker with only database access can and cannot read. Honest scoping of a limitation scores better than an overclaim.

#### Track B milestones

- [ ] Schema migrated, seed script for a test user and case
- [ ] Upload, hash, enqueue working end to end
- [ ] Real GPU worker driving a job to READY
- [ ] Dashboard with live status and asset download
- [ ] Login plus JWT plus three roles enforced server side
- [ ] Hash chained audit log, with a verification endpoint
- [ ] AES-256-GCM asset encryption, with the key outside the DB
- [ ] Anchor store and resolve endpoints for Unity
- [ ] Rate limiting and uniform error handling
- [ ] Custody overhead measured, milliseconds per write, for Challenge 4

## Section 09. Track C, Unity AR client [T]

Highest risk track, because it is the one where the desktop research tooling meets a phone. Sequence it so the scariest unknowns are answered first.

### Build order, riskiest first

1. **Splat on device.** Render a real `.ply` on a real phone, measure frame rate. Everything else is pointless until this is known.
2. **Marker alignment.** Tracked image of known size becomes the scene origin. Gives you pose and scale at once.
3. **Fetch from API.** Download by scene id over Tailscale, render what the pipeline actually produced.
4. **Colliders.** Start with detected AR planes. Swap in the Poisson mesh from Track A later.
5. **Ballistics.** Parameter input, trajectory integration, impact point, persistent overlay.
6. **Persistence and multi user.** Host and resolve a cloud anchor, second device joins.
7. **Spatter proxy.** Ellipse distribution from impact geometry.
8. **Measurement tools and polish.** Point to point distance, scene navigation, the demo affordances.

> [!WARNING]
> **Mobile splat rendering, plan for it not working well**
>
> Splat renderers lean on compute shaders and per frame sorting, and mobile GPUs vary wildly in how well they cope. Assume you will need to cut splat counts hard. Ladder of fallbacks, in the order you should try them:
>
> 1. Decimate aggressively, prune low opacity splats, and cap count to whatever holds 30 fps on your actual test phone.
> 2. Distance based level of detail, so only nearby splats are drawn at full density.
> 3. **Ship a textured mesh on mobile** instead of splats. Poisson reconstruct from splat centres, bake colour to texture, and keep true splat rendering for the web dashboard preview where a desktop GPU is available. Your proposal already needs that mesh for colliders, so this is a reuse, not new work.
>
> Option 3 is a legitimate engineering answer, not a failure, as long as you say clearly what runs where and why. Decide by 9 Oct, see Section 15.

### Ballistics, the bug you will definitely hit

A bullet at even 300 m/s covers 6 metres in a single 20 ms physics step. A standard rigidbody will teleport straight through a wall between steps and report no collision. This will look like your physics is broken when in fact your integration is too coarse.

**Do not launch a rigidbody and hope.** Integrate the trajectory yourself in small substeps, and for each substep sweep from the previous position to the new one with a raycast or spherecast. First hit is your impact point. Draw the arc from the accumulated points.

```
// per substep dt (e.g. 1 ms), for N substeps
a  = gravity - (dragCoeff / mass) * |v| * v   // quadratic drag
v += a * dt
p1 = p0 + v * dt
if (Physics.Raycast(p0, p1 - p0, out hit, |p1 - p0|)) -> impact at hit.point
p0 = p1
```

This also gives you something to validate against, because a drag free run should match the analytic parabola almost exactly. If it does not, your integrator is wrong and you have found it cheaply.

### Blood spatter, the actual formula

Your scope is a directional ellipse proxy, not fluid dynamics, and standard bloodstain pattern analysis gives you exactly the relationship you need. For a droplet striking a surface at incidence angle theta, the stain's width to length ratio approximates `sin(theta)`. So the impact angle is recoverable as `theta = arcsin(width / length)`.

Implement it in that direction: emit droplets from an origin with a spread of directions and speeds, raycast each one, and at the hit point place an ellipse decal whose minor over major axis equals the sine of the incidence angle, with the major axis aligned to the incoming direction projected onto the surface. That is cheap, it is defensible against published BPA principles, and it matches the simplified proxy your scope explicitly promises. Cite the relationship in the report and be clear that you are not modelling rheology.

### Colliders from splats

Splats are not surfaces, so PhysX cannot collide with them directly. Two paths, and you should do both because the comparison is your research contribution.

| Path | How | Role |
|---|---|---|
| AR native mesh | Detected planes, or the device scene mesh on LiDAR iPhones | Robust baseline. Use this to unblock physics work in October |
| Splat derived mesh | Poisson reconstruct from splat centres, decimate to tens of thousands of triangles, import as a mesh collider | The research path. Measure its deviation against the baseline and against measured ground truth |

Your Challenge 1 question, how much mesh approximation error is tolerable before trajectories diverge, becomes directly answerable: fire identical shots into both collider sets, measure the impact point difference, and plot it against mesh decimation level. That is a real result, and it comes almost free once both paths exist.

#### Track C milestones

- [ ] AR sample running on a physical device
- [ ] Splat file rendering on device, frame rate recorded
- [ ] Mobile splat budget decided and told to Wahaj
- [ ] Marker alignment placing the scene at correct position, rotation, and scale
- [ ] Scene fetched from the real API and rendered
- [ ] Ballistic trajectory with substepped sweep, validated against the analytic parabola
- [ ] Impact detection against AR plane colliders
- [ ] Cloud anchor hosted and resolved on a second device
- [ ] Splat derived mesh colliders working
- [ ] Spatter ellipse proxy implemented
- [ ] Parameter input UI and measurement tool
- [ ] iOS build running on device

## Section 10. Integration

Your Gantt starts integration on 28 Sep and the midterm prototype lands 16 Oct. That is the real deadline, not December. Integrate in four steps, each with a test that either passes or does not, so "it is integrated" is never a matter of opinion.

| Step | Handoff | Passes when | By |
|---|---|---|---|
| I1 | Manual file pass | A `.ply` from Wahaj's own capture renders on Tayyab's phone, moved by hand | 6 Sep |
| I2 | Web in the middle | Same file uploaded through the API, downloaded by Unity by scene id, renders | 20 Sep |
| I3 | Queue closes the loop | Video uploaded from the dashboard drives a real GPU worker to READY with no manual step | 4 Oct |
| I4 | Full walkthrough | Record on the phone, wait, open AR, marker aligns, one shot fired, impact drawn | 11 Oct |

> [!NOTE]
> **Run I4 end to end at least three times before the midterm**
>
> Once is luck. The second and third runs are where you find the token that expired, the file path that only worked on one machine, and the anchor that resolved on Android but not iOS. Rehearse the demo as the demo, on the network you will actually present on.

### Where integration will actually break

1. **Handedness and up axis.** Section 11. Your first render will be mirrored or on its side. It is a convention mismatch, not a bug in anyone's code.
2. **Scale.** Section 11. Also not a bug, it is a property of monocular SfM.
3. **The phone cannot reach the dev API.** Put Tailscale on the phone.
4. **Files too large.** A dense splat file is tens of megabytes or worse over a phone connection. Decimate server side and stream.
5. **Auth on device.** Token storage and expiry behave differently on a phone than in a browser tab.
6. **One machine only.** Anything that works on exactly one laptop is not done. Absolute paths and machine local config are the usual culprits.

## Section 11. Scale and coordinates, read before you claim any accuracy

> [!IMPORTANT]
> **Monocular SfM has no absolute scale**
>
> This is the most important technical fact in the entire project, and the 22 Aug proposal now acknowledges it: monocular SfM recovers geometry **up to an unknown scale factor**, and a printed marker of known physical size at the scene origin is the stated fix. The reconstruction knows the room's shape but not whether it is 3 metres wide or 300. There is no setting that fixes this, it is a property of the mathematics. This section is how you actually implement and verify the commitment the proposal makes on paper.
>
> So Objective 1, positional accuracy within 2 cm at up to 5 m, is not measurable at all until you resolve scale. If you skip this, every accuracy number in your report is meaningless, and it is exactly the kind of thing an examiner who knows the field will ask about first.

### How to fix it, pick one and do it in every single capture

1. **Known length reference, simplest and free.** Place two markers an exactly measured distance apart, ideally around a metre, in view during capture. After reconstruction, measure that same distance in scene units. Scale factor is real metres divided by scene units. Write it into `metadata.json` as `unitScale`.
2. **Printed marker of known physical size, best option.** The same printed target Unity uses for alignment. Detect it in the reconstruction, and you get scale and origin and orientation together, which means Track A and Track C agree on the frame by construction rather than by luck.
3. **LiDAR reference.** If any of your phones has LiDAR, scan the room, then fit the reconstruction to the scan. This also gives you the geometric ground truth you need for evaluation anyway.

Do 2 as the working method and 3 as the evaluation reference. They double as each other's cross check.

### The frame convention, write it down once

SfM tooling and Unity do not share conventions. SfM output is typically right handed with Y down, following the computer vision camera convention. Unity is left handed with Y up. Somewhere in your pipeline exactly one conversion must happen.

> [!NOTE]
> **Rule**
>
> **Convert once, on the GPU side, before export.** Emit `.ply` files already in Unity's convention, and record `handedness` and `upAxis` in the metadata so the client can assert it rather than guess. Never let both Track A and Track C apply a flip, because two conversions cancel out and you will spend a day discovering it. Write the chosen convention into `docs/FRAMES.md` with a diagram and have all three of you initial it.

#### Sanity checks

- [ ] Reconstruct a scene containing an object of known size, measure it in scene units, and confirm the scale factor recovers the real measurement within a couple of centimetres
- [ ] Confirm text or an asymmetric object in the scene is not mirrored. Mirroring means a handedness error
- [ ] Place the scene in AR by marker and check a real doorway lines up with the rendered doorway
- [ ] `docs/FRAMES.md` written and agreed

## Section 12. Evaluation plan

Objective 5 promises a structured evaluation over at least three mock scenes. Build the measurement harness in October, not December, because a metric you cannot compute on demand does not make it into the report. Every row here maps to a table or figure you will actually print. Because you are a Development project there is no results chapter, so each row below becomes a numbered **test case in Chapter 7**, with a pass criterion. Section 16 has the format. Give each metric its test case ID now, so results drop straight into the report as they arrive.

| Metric | Method | Reported as | Owner |
|---|---|---|---|
| Render fidelity | Held out views, PSNR, SSIM, LPIPS | Per scene table plus mean | [W] |
| Metric accuracy | Tape measure N known distances, compare in scaled reconstruction | Mean absolute error in cm, and percent | [W] |
| Geometric deviation | Align to LiDAR reference, cloud to cloud distance in CloudCompare | Mean, RMSE, 95th percentile | [W] [F] |
| Adverse conditions | Recapture the same scene dim, blurred, and with specular objects, compare to the ideal baseline | PSNR delta per condition | [W] |
| Anchor accuracy | Mark a physical point, place a virtual marker, reopen across sessions and devices, measure offset | Mean and max cm, textured versus bare room | [T] |
| Texture threshold | Anchor success rate against feature density per square metre | Curve, answers Challenge 3 | [T] |
| Collider fidelity | Identical shots into splat mesh versus baseline colliders at several decimation levels | Impact point divergence in cm, answers Challenge 1 | [T] |
| Physics validity | Drag free run against the analytic parabola, then a low speed projectile at measured speed and angle | Predicted versus observed impact point | [T] |
| Latency | Timestamp every stage transition, upload to AR ready | Stacked breakdown per stage | [W] |
| Custody overhead | Write throughput with and without hash chained logging | Milliseconds per operation, answers Challenge 4 | [W] |
| Usability | System Usability Scale with 8 to 10 postgraduate participants | SUS score plus themed comments | [ALL] |

> [!WARNING]
> **Three things that will cost you marks if ignored**
>
> 1. **Report variance, not a single number.** Run each scene three times. One number with no spread invites "how do you know that is repeatable".
> 2. **State the held out split.** PSNR on training views is not a result. Say which frames were held out and how many.
> 3. **Check whether human participants need ethics clearance** at your department before you run the usability study. If they do and you skipped it, the data may be unusable.

> [!NOTE]
> **Do not test real firearms**
>
> Validate the integrator against the analytic solution, then validate at safe low speeds with a measured launcher, and argue the model extrapolates. Say plainly in the report that high velocity validation was out of scope for safety reasons. That is a completely normal and accepted limitation.

## Section 13. Deliverables and test gates

What actually exists on each date, and the test that proves it. Every acceptance test below is binary on purpose. "It mostly works" is not a state, and a deliverable you cannot test in front of someone else is not a deliverable.

> [!NOTE]
> **The two dates that matter most**
>
> **6 Sep** is the first date you have something to *show*: your own room, reconstructed, rendering on a phone. **11 Oct** is the first date you have something to *test* as a system: video in, anchored twin with physics out, no human touching anything in the middle. Everything before 6 Sep is setup, and everything between the two is plumbing.

### Deliverable schedule

| ID | Date | What you have | Acceptance test | Own |
|---|---|---|---|---|
| D1 | 30 Aug | **Three working dev environments.** A trained `.ply` from your own capture, a web app that accepts an upload and enqueues a job, an AR app on a phone rendering a sample splat | Each owner screen records their piece running. Peak VRAM, training minutes, and phone frame rate written into `docs/RESULTS.md` | [ALL] |
| D2 | 04 Sep `gate` | **Project proposal, submitted on the official template.** Already in the 22 Aug draft: advisor named, versions pinned, marker plus Cloud Anchors, scale acknowledged, 17 week parallel plan. Still to fix: orphaned line on page 2 and blank page 4, references [9] [10] [11] cited in the body, timeline and risk table and dataset strategy sections added, "abandons a linear progression model" reworded to state the plan directly, test-cases-as-research-questions sentence restored, Section 4 upgraded with real first-light numbers if COLMAP is running | Submitted on `D1 Project Proposal Template.dotx` per the general submission instructions. All three of you can answer any question about any track | [ALL] |
| D3 | 06 Sep | **I1. First showable artifact.** A reconstruction of a real room you captured, rendering on a real phone. File moved by hand | Hand a phone to someone outside the team. They see the room. Nothing else has to work yet | [W] [T] |
| D4 | 13 Sep | **Metric scale, solved.** Reconstructions carry a real `unitScale` and a capture protocol that guarantees a scale reference | Measure a door frame with a tape. Measure the same edge in the scaled reconstruction. Agreement within a couple of centimetres, or the pipeline is not metric yet | [W] |
| D4a | 14 Sep | **Chapter 4 and Chapter 6 skeletons.** Headings, figure placeholders, and the requirement list derived from the Section 06 contract | Both chapters exist as outlines in the official template with every figure named. Neither is blank on 1 Oct | [ALL] |
| D5 | 18 Sep `gate` | **Proposal defence.** Faculty panels review the submitted proposal and return written feedback through the committee, so there is likely nothing to present. Confirm the format with your coordinator | Feedback received, read, and converted into repo issues within the same week. Anything affecting scope goes into DII | [ALL] |
| D6 | 20 Sep | **I2. The web layer carries the asset.** Unity fetches a scene by id over the API instead of from local storage | Replace the file server side. Restart the app. The phone shows the new scene with no rebuild | [W] [T] |
| D7 | 27 Sep | **Access control.** Login, JWT sessions, three roles enforced on the server | A test per role per route, asserting the denial cases. An investigator cannot fetch another investigator's case, proven by a failing request not a hidden button | [W] |
| D8 | 04 Oct | **I3. Automated pipeline.** Dashboard upload drives a real GPU worker with no manual step | Upload a video, close the laptop, come back. Status is READY and the asset is downloadable. Zero human intervention in between | [W] |
| D9 | 07 Oct `gate` | **DII report.** Chapters 1, 2, 3, 4 and 6, plus Abstract, Executive Summary and References | Submitted on the official report template. Each track's sections written by the person who built that track | [ALL] |
| D10 | 11 Oct | **I4. Minimum viable system.** Capture to reconstruction to anchored AR to one ballistic shot with a visible impact point | Run the whole thing three times on three different days. Three passes, or it is not done. This is the deliverable the midterm is graded on | [ALL] |
| D11 | 16 or 23 Oct `gate` | **Midterm evaluation.** Signed tape bound hard copy with the Mid compliance form delivered to the Academic Office, plus a 7 to 10 minute presentation, plus D10 as the prototype, plus a screen recording as backup | Paperwork accepted, which is what permits you to present at all. Presentation covers every required item in Section 16 and the prototype runs without a rescue | [ALL] |
| D12 | 01 Nov | **Multi user and custody.** Cloud anchor host and resolve, AES-256-GCM assets at rest, hash chained audit log | Two phones see the twin in the same physical place at the same time. Custody verify endpoint returns pass, then tamper one row directly in the database and confirm it returns fail | [T] [W] |
| D13 | 08 Nov | **Research apparatus.** Splat derived mesh colliders, all six scenes reconstructed, evaluation harness runnable on demand | One command produces the fidelity metrics table. Identical shots into both collider types give a measurable impact divergence in centimetres | [W] [T] |
| D14 | 11 Nov `gate` | **DIII report.** Chapter 7 with implementation and test cases, Chapter 10 with conclusions and the mandatory FYP-2 plan, earlier chapters updated with panel feedback, plus both Turnitin reports | Submitted. Every number in Chapter 7 is reproducible from `docs/RESULTS.md`. Plagiarism and AI reports both at 20 percent or less | [ALL] |
| D15 | 29 Nov | **Full results set.** Every Section 12 metric, three repeats per scene, usability study complete | Every table and figure the final report needs exists as a file. No metric still pending | [ALL] |
| D16 | 02 Dec `gate` | **Signed report to the Academic Office.** Updated, supervisor signed, tape bound, double sided | Accepted by the FSC Academic Office. Turnitin obtained with a week of margin, both reports at 20 percent or less | [ALL] |
| D17 | 11 Dec `gate` | **Final evaluation.** Updated signed hard copy with the Final compliance form and both Turnitin first pages, plus a supervisor approved 7 to 10 minute presentation, plus the working prototype and a backup recording | Paperwork accepted. Presentation covers the prototype, goals achieved, and FYP-2 future goals. Metrics tables in hand for questions | [ALL] |

### What testable means at each stage

Worth being precise about this, because "we can test it" means something different in September than in November.

| From | You can test | You still cannot test |
|---|---|---|
| 30 Aug | Each track in isolation, on its owner's machine | Anything crossing a track boundary |
| 06 Sep | Reconstruction quality, visually, on a phone | Accuracy in real units, since scale is unsolved until D4 |
| 13 Sep | Reconstruction accuracy in centimetres | Anything automated. Files still move by hand |
| 04 Oct | The pipeline unattended, plus end to end latency | AR alignment quality and physics, not integrated yet |
| 11 Oct | **The whole system, as a system.** Latency, alignment, one physics case | Multi user, persistence across sessions, custody, encryption |
| 01 Nov | Anchor drift across devices and sessions, custody tamper evidence | Collider fidelity study, full metric sweep |
| 08 Nov | Everything. All four research questions are answerable from here | Nothing. From here it is repeats and writing |

### Final handover inventory

What physically exists at the end. Build this list into the repo now and tick it off, because assembling it in December from memory is how things get left out.

- [ ] Source repository: `/gpu`, `/web`, `/unity`, `/docs`, with a README that gets a new machine running
- [ ] Six capture datasets: three physical mock scenes, three synthetic, with the raw video
- [ ] Six reconstructions: `.ply` plus `metadata.json` plus metrics per scene
- [ ] Ground truth references: LiDAR scans or measured distance sets per physical scene
- [ ] Android build, installable, plus iOS build if D16 risk did not force the cut
- [ ] Running web platform, plus the Docker Compose path so it runs with no cloud account
- [ ] `docs/RESULTS.md`: every measurement, with the machine and config that produced it
- [ ] `docs/CAPTURE.md`, `docs/API.md`, `docs/FRAMES.md`, `STACK.md`
- [ ] Usability study: instrument, raw responses, computed score
- [ ] Demo video of the full walkthrough
- [ ] All three reports, plus the signed final and the Turnitin receipt

## Section 14. Calendar, today to 11 Dec

Dates and deadline gates from your own Gantt. Week numbers follow the roadmap, starting 17 Aug. Note the deliberate pause in weeks 9 and 10, which is the midterm window, and matches the gaps in your flowchart.

**W1 to W2** (17 to 30 Aug)

**Setup sprint** (Section 05)

- [ALL] Lock the advisor. Resolve every Section 03 decision.
- [ALL] Agree the contract, commit `docs/API.md` and sample files.
- [W] CUDA, COLMAP, training framework, first `.ply`.
- [W] Repo, cloud Postgres and Redis, Next.js scaffold, schema, upload.
- [T] Unity, AR Foundation, AR sample on device, splat on device with frame rate measured.

> **Fri 04 Sep** `gate`
>
> **DI Project proposal due,** on the official `D1 Project Proposal Template.dotx`. The 22 Aug draft already has the advisor named, one pinned version set, marker plus Cloud Anchors, the scale limitation acknowledged, and a 17 week parallel plan matching the Gantt. Still open: the stray page breaks (orphaned line on page 2, blank page 4), uncited references [9] [10] [11], the missing timeline, risk table and dataset strategy sections, and moving onto the official template. Full list in D2, Section 13.

**W3** (31 Aug to 6 Sep)

- [ALL] Proposal final pass and submission.
- [W] Capture protocol written. VRAM safe preset locked.
- [W] Job endpoints live, dashboard shell.
- [T] Marker alignment working in editor.
- **I1 passes:** a real `.ply` renders on the phone, moved by hand.

**W4** (7 to 13 Sep)

- [ALL] **Start chapters 4 and 6.** Skeletons with named figures by 14 Sep. These are the only DII chapters you have not already half written.
- [W] Metric scale solved, `unitScale` populated.
- [W] Worker contract complete, real status transitions.
- [T] Scene fetch from API, marker alignment on device.

> **Fri 18 Sep** `gate`
>
> **Proposal defence.** Per the instructions this is a paper review: the committee circulates your proposal to faculty panels, they leave written feedback online, and it comes back to you. So there is probably nothing to present, but confirm with your coordinator. Either way, expect the feedback to raise scale, the anchoring dependency, and the 8 GB question. Sections 11, 03 and 07 are your answers, and having D3 already running is what makes them credible.

**W5** (14 to 20 Sep)

- [ALL] Panel feedback arrives. Act on it this week, while changes are still cheap, and log what you changed for DIII.
- [W] Auth and JWT.
- [T] Splat budget agreed with Wahaj.
- **I2 passes:** file travels through the Web API to Unity.

**W6** (21 to 27 Sep)

- [W] Second and third machine registered as workers.
- [W] RBAC enforced server side. Audit log.
- [T] Ballistics with substepped sweep, validated against the parabola.
- [ALL] Chapters 4 and 6 drafted, not just outlined. Chapters 1, 2 and 3 assembled from the proposal into the official template.
- [ALL] Define the Chapter 7 test case IDs now, empty, so October results have somewhere to land.

**W7** (28 Sep to 4 Oct)

**Integration window opens.**

- **I3 passes:** dashboard upload drives a real worker to READY with no manual step.
- [T] AR plane colliders, impacts registering.
- [ALL] First full team integration session. Same room if at all possible.

> **Wed 07 Oct** `gate`
>
> **DII report.** Chapters 1 Introduction, 2 Project Vision, 3 Related Applications, 4 Software Requirement Specifications, 6 High-level and Low-level Design, plus Abstract, Executive Summary and References. Chapters 4 and 6 are new writing, see Section 16. Every member drafts their own track's sections, one member owns formatting and consistency.

**W8** (5 to 11 Oct)

- DII submission.
- **I4 passes:** full walkthrough, capture to anchored AR to one shot fired.
- [ALL] Rehearse the midterm demo at least twice on the real network.

> **9 Oct** `gate`
>
> **Decision deadline: mobile splat rendering.** If you cannot hold 30 fps at your target splat count on a real device, switch to the textured mesh fallback now (Section 09). Later than this and you will not have time.

> **16 and 23 Oct** `gate`
>
> **Midterm evaluations.** Hard copy in first, signed and tape bound with the Mid compliance form, or you do not present. Then 7 to 10 minutes covering problem, scope, goals, related applications, architecture with constraints, and the prototype. Target for the prototype: capture, reconstruct, anchor, one ballistic shot with visible impact, live. No new features in these two weeks, only reliability. A smaller demo that never fails beats a larger one that crashes once.

**W9 to W10** (12 to 25 Oct)

**Midterm window, feature freeze.** Fix, harden, rehearse. Both flowchart and roadmap leave this gap, so use it as intended rather than cramming features into it.

**W11** (26 Oct to 1 Nov)

**Hard features resume.**

- [W] Poisson mesh export for colliders.
- [W] AES-256-GCM at rest, hash chained custody complete.
- [T] Cloud anchor host and resolve, second device joins.

**W12** (2 to 8 Nov)

- [T] Splat derived mesh colliders. Spatter ellipse proxy.
- [W] All six scenes reconstructed, three physical and three synthetic, metrics recorded.
- [W] Rate limiting, error handling, latency instrumentation.
- [ALL] Evaluation harness runnable on demand.

> **Wed 11 Nov** `gate`
>
> **DIII report.** Chapter 7 Implementation and Test Cases, Chapter 10 Conclusion and Future Work with a mandatory FYP-2 work plan, plus earlier chapters updated with panel feedback, plus the Turnitin plagiarism and AI reports at 20 percent or less. Your Section 12 measurements land in Chapter 7 as test cases, so they must exist by now. Email the library for Turnitin at least a week before this date.

**W13** (9 to 15 Nov)

- DIII submission. **Feature freeze from 12 Nov.**
- Final testing window opens per your Gantt.

**W14 to W15** (16 to 29 Nov)

**Final testing, all tracks.**

- Full evaluation runs on all scenes, three repeats each.
- Anchor drift across sessions and devices, textured and bare rooms.
- Collider divergence study at several decimation levels.
- Usability study with participants.
- Bug fixes only. No new features, no exceptions.

> **Wed 02 Dec** `gate`
>
> **Signed report to the FSC Academic Office.** Tape bound, double sided except the title page, supervisor signed. Run Turnitin with at least a week in hand. Your related applications chapter is the risky section for similarity, so use your own phrasing and cite everything.

**W16** (30 Nov to 6 Dec)

Report finalisation. Demo video recorded as insurance against live failure. Rehearse the final demo at least three times end to end.

> **Fri 11 Dec** `gate`
>
> **Final evaluation.** Updated signed hard copy with the Final compliance form and the first page of both Turnitin reports attached, or you do not present. Then 7 to 10 minutes, approved by your supervisor in advance, covering the working prototype, goals achieved, and future goals for FYP-2. Bring the recording as backup and your metrics tables printed.

## Section 15. Risk register with pre decided cuts

The value here is the middle column. Decide the fallback now, while you are calm and have time, and attach a date to it. Then when the date arrives you execute a decision instead of panicking.

| Risk | Trigger and fallback | Decide by |
|---|---|---|
| **Splats too slow on mobile**<br>Highest technical risk you have | Under 30 fps at target count on a real device, go to distance based LOD, then to textured mesh on mobile with splats kept for the web preview | 9 Oct |
| **Anchoring service unavailable** | Marker only alignment, plus store the transform yourself server side for persistence. You lose automatic cross device resolve, you keep everything else. Report it as a dependency change | 25 Aug |
| **8 GB will not fit a real room** | Downscale further, cap splats, tighten scene bounds, split the room into two overlapping captures. Last resort, a few hours of rented cloud GPU | 13 Sep |
| **SfM fails on a scene** | Recapture with the protocol followed strictly, add texture to bare surfaces, more frames, slower motion. If a scene keeps failing, substitute a different room. Do not burn a week on one hostile scene | ongoing |
| **Poisson colliders unusable** | Ship AR plane and device mesh colliders, and present the splat mesh comparison as the research finding it is, including where it fails | 8 Nov |
| **iOS blocked**<br>signing, Mac access, device | Android only for the graded demo, document iOS as designed and partially built. Do not let it consume November | 1 Nov |
| **Free tier lapses or DB suspends** | Local Docker Compose plus Tailscale is a complete substitute for a demo. Keep it working the whole semester as your fallback, not just at the start | ongoing |
| **A member is unavailable**<br>illness, other courses | Contract in Section 06 is what makes this survivable. GPU already cross covers (Wahaj/Faizan) and, since 30 Aug, so does Unity (Tayyab/Faizan). Web is now the single point of failure, Wahaj alone owns it, so at least one other person should be able to build and run `/web` by 1 Nov | 1 Nov |
| **Evaluation left too late** | Most common way good FYPs lose marks. Harness must run on demand by 8 Nov, even if partial | 8 Nov |
| **Laptop dies** | Everything except large binaries in git, pushed daily. A dead laptop should cost you a day, not a track | ongoing |
| **Chapters 4 and 6 not written**<br>due 7 Oct, currently nonexistent | Draft both from the Section 06 contract, which already contains the substance. If they are still skeletons on 28 Sep, one person stops coding entirely and writes until they are done. A weak DII costs marks you cannot earn back with code | 28 Sep |
| **Paperwork blocks a presentation**<br>binding, signature, compliance form, Turnitin | Assemble each submission a full week early. Supervisor signatures need the supervisor physically available, and library Turnitin turnaround is not under your control. Missing paperwork means you are not permitted to present at all, regardless of the software | 1 week before each gate |
| **Turnitin above 20 percent**<br>plagiarism or AI report | Write your own prose from the start and cite properly. Submit for checking early enough to rewrite and resubmit. Related applications and introduction are where similarity accumulates | 04 Nov |

## Section 16. The graded deliverables, exactly as FAST wants them

These are the real institutional requirements, not an approximation. Your project is registered as a **Development** project, which changes the chapter set, so read the chapter map carefully. Anything marked required is a gate: miss the paperwork and you are not permitted to present, regardless of how well the software works.

| Deliverable | Due | Hand in |
|---|---|---|
| **DI** Project Proposal | Fri 04 Sep<br>week 3 | Proposal on the official `D1 Project Proposal Template.dotx`, per the general submission instructions |
| **Proposal defence** | Fri 18 Sep<br>week 5 | Nothing new. The committee circulates your submitted proposal to faculty panels, they give written feedback online, and the committee passes it back to you |
| **DII** FYP Report | Wed 07 Oct<br>week 8 | Chapters 1, 2, 3, 4 and 6 on the official report template, plus Abstract, Executive Summary and References |
| **Midterm evaluation** | Fri 16 or 23 Oct<br>week 9 or 10 | Signed hard copy report to the FSC Academic Office, tape bound, double sided except the title page, with the signed **Compliance Form FYP I Mid** at the back. Then a 7 to 10 minute presentation |
| **DIII** FYP Report | Wed 11 Nov<br>week 13 | Earlier chapters updated with panel feedback, plus Chapter 7 and Chapter 10, plus Turnitin plagiarism report and Turnitin AI report |
| **Signed report** | Wed 02 Dec<br>week 16 | Signed hard copy to the FSC Academic Office |
| **Final evaluation** | Fri 11 Dec<br>week 17 | Updated signed hard copy, tape bound, with **Compliance Form FYP I Final** and the first page of both Turnitin reports. Then a 7 to 10 minute presentation, approved in advance by your supervisor |

### Chapter map for a Development project

You skip chapters 5, 8 and 9, which R&D projects have to write. That has one consequence worth planning around, covered in the box below.

| Ch | Title | Due | What of yours goes in it |
|---|---|---|---|
| 1 | Introduction | DII | Your abstract and introduction, largely reusable from the proposal |
| 2 | Project Vision | DII | The five objectives, and the included versus excluded scope lists. Already written |
| 3 | Related Applications | DII | Your comparison table against 2D photography, photogrammetry and terrestrial LiDAR. Trim the literature review down into this, since Development projects do not get a full review chapter |
| 4 | Software Requirement Specifications | DII | **Does not exist yet.** Functional and non functional requirements, actors, use case diagrams and descriptions, constraints |
| 6 | High-level and Low-level Design | DII | **Mostly does not exist yet.** Your tech stack diagram is a start. Needs architecture, component and deployment views, the data model, sequence diagrams for the six pipeline stages, and the API contract from Section 06 |
| 7 | Implementation and Test Cases | DIII | What you actually built, plus every measurement from Section 12 written as test cases |
| 10 | Conclusion and Future Work | DIII | Results summary, limitations, and a **mandatory plan of work for FYP-2** |

> [!IMPORTANT]
> **There is no results chapter, so your metrics become test cases**
>
> R&D projects get Chapter 9, Experimental Results and Discussion. You do not. So every number from Section 12 has to live in **Chapter 7 as a test case** instead, which actually suits you well: each of your five objectives already carries a numeric threshold, so write each one as a test with a stated pass criterion, method, and observed result.
>
> ```
> TC-07  Reconstruction fidelity
>   Requirement : Objective O1, PSNR above 28 dB
>   Method      : 3 physical scenes, held out views, 3 repeats each
>   Expected    : mean PSNR > 28 dB
>   Observed    : ___
>   Verdict     : pass / fail, with discussion
> ```
>
> Build the table of test case IDs in October, before you have the numbers, and fill in the observed column as results arrive. That way Chapter 7 assembles itself instead of being written from scratch in November.

> [!WARNING]
> **The two chapters nobody has started**
>
> Chapter 4 and Chapter 6 are due **07 Oct**, roughly seven weeks out, and neither exists in any form in your current documents. This is the most underestimated work in the whole semester, because it feels like paperwork but it is genuinely several days of careful writing, and it needs diagrams.
>
> The efficient way to do it: write Chapter 6 *from the contract* you agreed in Section 06. Your endpoint list, state machine, data model and frame convention are already the substance of a low level design chapter. Chapter 4 comes from the same place, read backwards: every endpoint implies a functional requirement, every role implies an actor, and every objective threshold is a non functional requirement. Start both by **14 Sep**.

### Printing and paperwork, both evaluations

Boring, and it will stop you presenting if you get it wrong. Do this the week before, not the night before, because it needs your supervisor physically present to sign.

- [ ] Report printed **double sided**, except the title page
- [ ] **Tape binding only.** Not spiral, not comb, not a folder
- [ ] Supervisor signature on the report
- [ ] Correct compliance form attached at the **end** of the report, checked and signed by the supervisor. Mid form for the midterm, Final form for the final
- [ ] Final only: first page of the Turnitin plagiarism report and the Turnitin AI report attached
- [ ] Delivered to the FSC Academic Office, not to the panel on the day
- [ ] Final only: presentation approved by your supervisor beforehand

> [!WARNING]
> **Turnitin, two reports, 20 percent each**
>
> You need a plagiarism report **and** an AI report, and each has to come in at 20 percent or less. Get them by emailing your report to the library at `librarylhr@nu.edu.pk`, which means turnaround time you do not control. Send it at least a week before the deadline, and send it early enough that you can fix a bad result and resubmit.
>
> Practical consequence for how you write: **write the report in your own words from the start.** Both thresholds are much easier to clear by writing genuinely than by editing your way out of a bad score afterwards, and the AI report in particular is easiest to pass when the prose is actually yours. Where you cite, quote properly and reference it. The literature and related applications sections are where similarity usually accumulates, so give those your own phrasing and structure.

### Midterm presentation, 7 to 10 minutes, 16 or 23 Oct

The required coverage is specified, so structure the talk around it rather than around a demo. The prototype is one item on the list, not the whole talk. Suggested timing:

| Min | Cover | Notes |
|---|---|---|
| 0:00 | Problem statement and elaboration | The LiDAR cost barrier and the loss of spatial context in 2D documentation. One slide |
| 1:00 | Scope, in and out | Say the exclusions out loud. Panels reward a bounded project |
| 2:00 | Goals achieved and future goals | Against your five objectives, honestly. Achieved, partial, not started |
| 3:00 | Related applications | Your comparison table, one slide |
| 4:00 | Architecture and design | The three track diagram, the six stage pipeline, plus key requirements, constraints and limitations. Name the 8 GB constraint and the scale problem here, before anyone asks |
| 6:00 | Prototype | Deliverable D10. Reconstructed scene, AR alignment, one ballistic shot |
| 9:00 | Questions | Whoever owns the track answers for that track |

### Final presentation, 7 to 10 minutes, 11 Dec

Shorter than you think, and two thirds of it is required content rather than demo. Get it approved by your supervisor with time to change it.

| Min | Cover | Notes |
|---|---|---|
| 0:00 | Problem and approach recap | 30 seconds. The panel has seen this before |
| 0:30 | Working prototype | The required centrepiece. Capture or a pre reconstructed scene, dashboard with roles and custody, AR alignment, second device joining, ballistics and spatter |
| 4:30 | Results from FYP-1 | Fidelity, metric accuracy, anchor drift, collider divergence, latency. Numbers on screen, not adjectives |
| 6:30 | Goals achieved | Objective by objective, with the measurement that proves each one |
| 8:00 | Future goals for FYP-2 | Required. Must match Chapter 10. Section 18 is where this comes from |
| 9:00 | Questions |  |

### Documentation, running in parallel

- **Everyone writes their own track.** You built it, you can explain it, and you will write it in a tenth of the time it takes someone else.
- **One member owns consistency:** template compliance, figure numbering, citation style, terminology. Rotate per report if you like, but it is always exactly one person.
- **Use the official template from the start.** Reformatting a finished document into someone else's template is miserable and it always eats a day you needed for something else.
- **Log as you go.** Every metric, config, and failure into `docs/RESULTS.md` the day it happens. Reconstructing November's numbers in December is impossible.
- **Incorporate panel feedback visibly.** DIII explicitly requires updated earlier chapters. Keep a short changelog of what you changed in response to which comment, and the panel will notice you listened.

> [!WARNING]
> **Record the demo in advance**
>
> Have a clean screen recording of the full walkthrough ready before 11 Dec. If the venue wifi fails or a device refuses to localise, you show the recording and narrate it live. Panels are fine with that. They are much less fine with five minutes of silent debugging.

## Section 17. Working agreements

| Practice | Rule |
|---|---|
| Daily standup | Async, one message each: did, doing, blocked. Two minutes. A blocker unreported for a day is a day lost |
| Weekly sync | Same 45 minutes every week, ideally Friday. Integration state, next week's gate, one risk reviewed |
| Branches | One per feature, named by track. Never commit directly to main |
| Review | One other person reads every merge. Cheapest quality mechanism you have, and it spreads knowledge across the team |
| Main is green | Main always builds and runs. If you break it, fixing it is your only task |
| Contract changes | Section 06 changes need all three to acknowledge. No silent edits |
| Versions | No upgrades without telling the others. `STACK.md` updated in the same commit |
| Binaries | No `.ply`, no videos, no build output in git. Share large files over Tailscale or object storage |
| Unity conflicts | Smart merge configured before the first scene conflict, not after |
| Results log | Every measurement into `docs/RESULTS.md` the same day, with the machine and config that produced it |
| Secrets | Only `.env.example` is committed. Real credentials shared privately, rotated if leaked |
| Definition of done | Works on a machine that is not yours, is committed, and is written down. All three, or it is not done |

## Section 18. Building past the deadlines, and what FYP-2 becomes

The deadlines are a floor, not a target. You want the whole product finished inside FYP-1, and that is achievable, because the graded minimum for FYP-1 is genuinely modest: a prototype at the midterm and a working prototype with results at the final. Nothing stops you shipping the complete system by December. Two rules make it safe to try.

> [!IMPORTANT]
> **The two rules for building ahead**
>
> 1. **Never trade a gate for a feature.** Every gate in Section 16 is pass or fail and mostly paperwork. Losing the right to present because you were coding through the week the compliance form needed signing would be the worst possible outcome. Gates first, always, then build.
> 2. **Earn the stretch, do not assume it.** The stretch list below unlocks in order, and only after D10 passes three times on 11 Oct. Building extra surface before the core pipeline is reliable is how projects end up with eight half features and no demo.

### The product ceiling, in unlock order

Each tier assumes the tier above it is done and stable. Do not skip down the list because something looks more fun.

| Tier | Unlocks after | What you add | Why it is worth it |
|---|---|---|---|
| T0 | D10, 11 Oct | **Everything in the proposal.** Multi user anchors, RBAC, AES-256 at rest, hash chained custody, splat derived colliders, spatter proxy, all six scenes | This is already the full proposal scope. Finishing it by December means FYP-1 delivered the whole system, which is the thing you actually asked for |
| T1 | T0 | **Make it a product, not a demo.** Deployed and reachable on a real URL, real error states, job retry and resume, upload progress, a scene that loads on a cold device without a developer present | The single biggest gap between student project and product. Also the cheapest way to look far ahead of your cohort |
| T2 | T1 | **Investigator workflow.** Case export to PDF with scene screenshots and custody log, in AR annotation and measurement pinned to the twin, evidence markers with notes, side by side comparison of two captures of the same room | These are what an actual investigator would ask for on day one, and none of them are technically hard once the pipeline works |
| T3 | T2 | **Depth on the hard problems.** Automatic capture coverage heatmap during recording, LOD streaming so large scenes load progressively, offline mode with a cached scene, anchor fallback chain when cloud resolve fails | Directly attacks the limitations you will have written up, and each one is a defensible engineering contribution |
| T4 | T3 | **Research grade evaluation.** The four research questions answered with proper studies rather than single runs, statistical treatment, more participants | This is the material that turns a good FYP into something publishable |

> [!WARNING]
> **If you finish everything, Chapter 10 gets harder, not easier**
>
> Chapter 10 requires a plan of work for FYP-2. If you ship the entire product in FYP-1, "we already built it" is not an acceptable FYP-2 plan, and you will have written yourself into an awkward corner in the one chapter you cannot skip.
>
> The fix is to plan the pivot now: **if FYP-1 delivers the system, FYP-2 becomes depth rather than features.** The four research questions, a proper evaluation at scale, real user studies with practitioners rather than postgraduate stand ins, robustness under adverse capture, and a paper. Write Chapter 10 around that, and finishing early becomes a strength in the report instead of a problem. Decide which framing you are using by **1 Nov**, so Chapter 10 is not being invented the week it is due.

### What genuinely belongs in FYP-2 either way

Confirm the actual FYP-2 requirements with your department, since they vary. Assuming a continuation semester:

- **The four research questions, answered properly.** Collider approximation error against trajectory divergence, reconstruction fidelity under adverse capture, the feature density threshold for reliable anchoring, and custody verification overhead. Each deserves a real study rather than a paragraph. This is the intellectual contribution that distinguishes a project from a demo.
- **Evaluation at scale.** More scenes, more participants, statistical treatment rather than single numbers.
- **Robustness.** The failure paths you stubbed in FYP-1: retry on failed jobs, partial capture recovery, graceful degradation when anchoring fails.
- **Whatever you cut.** Every fallback you took from Section 15 is a candidate to do properly, and by then you will have the measurements to justify the choice either way.
- **Publication.** If the collider or anchoring studies produce clean results, they are plausibly a workshop paper. Worth asking your supervisor about in October, not April.

> [!NOTE]
> **The honest summary**
>
> This project is well scoped and technically credible, and your track split fits three people. Shipping the whole thing inside FYP-1 is a realistic goal.
>
> The five things most likely to hurt you are not the hard research problems. They are the still unverified anchoring dependency, scale handling that exists on paper but not yet in the pipeline, mobile splat performance, chapters 4 and 6 being due on 7 Oct with nothing written, and leaving evaluation until December. Four of the five are addressable in the next six weeks, and three of them are addressable this week.

---

*Forensic NeRF Reconstructor · internal build handbook · drafted 21 Aug 2026, revised 22 Aug 2026 against the latest proposal draft (advisor named, splatfacto committed, marker plus Cloud Anchors adopted, versions pinned, scale acknowledged)*

*Items marked `verify` were not checked against live sources and must be confirmed before you depend on them.*

*Supersedes the earlier build guide. Where this disagrees with the proposal, flowchart, or Gantt, reconcile deliberately and update all of them.*
