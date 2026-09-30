# Mobile splat rendering: the options, and what each one changes

Written 2026-09-02 (Wahaj) after PR #14 established that the stock renderer does not compile on
mobile. Purpose: cost the alternatives *before* days go into shader work, and say exactly what
deviating costs each track. Track C owns the decision; this is input to it, not a decision taken.

> **Decision Update (2026-09-30, Tayyab):** Track C selected **Option 1 (`arloopa/UnitySplats`)**,
> resolving FTW-44 without a custom shader fork. Pinned in `docs/STACK.md`.

Deadline pressure to be honest about: integration gate **I1 is 6 Sep** (a `.ply` renders on
Tayyab's phone, file moved by hand), and the proposal defence is **18 Sep**.

## The blocker, restated precisely

Tayyab's diagnosis in PR #14 is correct and is the single most useful Track C finding so far. The
mechanism, verified in the actual sources rather than taken on trust:

`aras-p/UnityGaussianSplatting`, `package/Shaders/SplatUtilities.compute` declares at lines 32-34

```
#pragma require wavebasic
#pragma require waveballot
#pragma use_dxc
```

**and that same file holds all 19 kernels** — 15 of which have nothing to do with sorting
(`CSCalcViewData`, `CSCalcDistances`, `CSSetIndices`, the edit/selection kernels), plus the 4
radix-sort kernels. HLSL pragmas are file-scoped, so on a GPU without wave intrinsics the whole
file fails to compile and **rendering dies, not just sorting**. That is why the symptom is "no
splats at all" rather than "splats in the wrong order".

Upstream is explicit that this will not be fixed. `readme.md`:

> :warning: The only platforms where this is known to work are the ones that use D3D12, Metal or
> Vulkan graphics APIs.
> Mobile may or might not work. Some iOS devices definitely do not work (#72), some Androids do
> not work either (#112)

and the maintainer in issue #26: *"Oh, OpenGL or OpenGL ES is definitely not going to be
supported... Do I want to do it myself? Definitely no."*

**But "aras-p does not work on mobile" is not the same as "splats do not work on mobile", and the
PR's conclusion conflates the two.** Meta Quest 3 (Adreno 740) is supported upstream as of v1.0.0
via PR #146, which already proves Adreno-class hardware can render splats.

## The finding that matters most

**The `gsplat-unity` fork that PR #14 removes is the one that is documented to work on Android.**
Its README says, verbatim:

> I have only tested on Windows, Mac and **Android**

with an Android-specific setup step (uncheck `Apply display rotation during rendering` under
Vulkan Settings). The structural reason it survives where aras-p does not, verified by auditing
pragma placement across every `.compute` file in both repos:

| Repo | File | Kernels | wave/dxc pragmas |
|---|---|---|---|
| aras-p | `SplatUtilities.compute` | **19** (15 non-sorting) | **3** |
| gsplat-unity | `Gsplat.compute` | 5 (sort only) | 3 |
| gsplat-unity | `CalcDepth.compute` | 1 | **0** |
| gsplat-unity | `InitOrderSpark.compute` | 1 | **0** |
| gsplat-unity | `InitOrderUncompressed.compute` | 1 | **0** |
| gsplat-unity | `GsplatMergeOrderBuffers.compute` | 3 | **0** |
| gsplat-unity | `CalcDepthSpark.compute` | 1 | **0** |
| gsplat-unity | `GsplatCopyBuffer.compute` | 2 | **0** |

gsplat-unity **quarantines the wave pragmas in the sort file**. A sort-shader compile failure
therefore costs sorting, not rendering. That is the fix FTW-44 proposes to write by hand, except
it already exists upstream and is already Android-tested.

So the reverting of `wu.yize.gsplat` deserves a second look before FTW-44 starts. (Note the revert
is also not actually in the PR's diff — both packages are still added in `manifest.json`. Raised
separately on the PR.)

## Option table

Ranked by risk-adjusted cost to reach I1. "Contract change" means `docs/API.md` and the Prisma
schema, which needs all three of us per AGENTS.md.

| # | Option | Effort | Risk | Contract change | Splats on phone? |
|---|---|---|---|---|---|
| 1 | `arloopa/UnitySplats` | hours | low-medium | none | yes |
| 2 | `wuyize25/gsplat-unity` | hours | medium | none | yes |
| 3 | Hand-patch aras-p (FTW-44 as written) | days-weeks | **high** | none | yes, eventually |
| 4 | Textured mesh on mobile (handbook Option 3) | days | low | **yes** | no |
| 5 | WebView / PlayCanvas viewer | days | high for AR | maybe | yes, but no AR |

### Option 1 — `arloopa/UnitySplats` (recommended first try)

MIT, Unity 6000.0+, URP supported. Its README platform table states:

| Platform | Graphics APIs | Sorting |
|---|---|---|
| Android | Vulkan | GPU radix sort or CPU fallback |
| Android | **OpenGL ES 3.1** | **Asynchronous CPU sort** and portable sampled-texture draw path |

> Maintainer testing has covered multiple Android and Apple devices using OpenGL ES, Vulkan, and
> Metal, including Samsung and HONOR Android hardware and Meta Quest 3 and Meta Quest 3S headsets.

Verified in source, not just the README — `Runtime/GsplatSorter.cs` (1107 lines) contains a real
async CPU radix sort and, at line 451, exactly the defence we need:

```csharp
catch (Exception)
{
    // Some backends strip wave-op kernels entirely. The fallback warning below
    // reports the selected path once a renderer actually needs it.
    m_sortPass = null;
}
```

A wave-intrinsic compile failure **degrades to a CPU sort instead of killing the app**. It also
loads PLY at runtime, which is what fetching scenes from our API needs, and reads SPZ/SOG.

**Honest caveat: this package is new** — created 2026-07-22, last release 1.2.0 on 2026-08-18,
32 stars. Low adoption is a real risk for a load-bearing dependency; it wants an audit and a
pinned commit in `docs/STACK.md`, not blind trust. It is a fork of gsplat-unity, so Option 2 is
its natural fallback.

### Option 2 — `wuyize25/gsplat-unity`

More mature, Android-tested, already in our lockfile at hash `a2bf458d`. Two things to check
before committing:

- **It requires Gamma colour space.** Our URP AR project is almost certainly Linear. Switching
  colour space affects the whole app's look, not just splats, and interacts with the AR camera
  background. aras-p handles Linear correctly because it renders to a separate RT, so this is a
  genuine point in aras-p's favour if we must stay Linear.
- It ships an **`Opacity Prune Threshold`**; the README reports 0.05 removing ~40% of splats
  "with little visible change". Compare against my measured numbers before trusting that on our
  data — see the caveat below.

### Option 3 — hand-patch aras-p (FTW-44 as currently written)

**I would not start here, and specifically I would not write a bitonic sort.** Evidence:

- Issue #112 has been open since 2024 and was still being fought on 2026-01-30.
- The known-good recipe (from a Niantic engineer, "works fine on Samsung S20") is not a sort
  rewrite. It is: force Vulkan, **disable multithreaded rendering**, split `float4x4` in
  StructuredBuffer into 4x`float4`, reduce `GROUP_SIZE` 1024 -> 512 and
  `InitDeviceRadixSort` numthreads 1024 -> 256, and remove every `[unroll]`. A later commenter
  applying all of it still reported splats "not correct".
- There is **no success report for a bitonic replacement**, and Unity's own tracker documents
  bitonic sort exceeding max workgroup size on Adreno/Mali.
- The Vulkan wave-size bug FTW-44 partly targets is **already patched upstream** — `SortCommon.hlsl`
  works around it by ballot-counting under `#if defined(VULKAN)`.

If the ticket proceeds anyway, the cheap version is not a sort rewrite: **split
`SplatUtilities.compute` into two files** so the 15 non-sorting kernels compile without the wave
pragmas. That is the same structural fix gsplat-unity already has, and it is an afternoon rather
than a fortnight.

### Option 4 — textured mesh on mobile (handbook Section 09/15, Option 3)

The only option that changes my tracks, so this is the part I can cost precisely.

**What breaks today.** `web/src/app/api/scenes/[id]/asset/route.ts` hardcodes the asset kind:

```ts
const asset = await prisma.asset.findFirst({
  where: { sceneId: id, kind: 'SPLAT_PLY' },
```

and `AssetKind` in `web/prisma/schema.prisma` has exactly three values:
`SOURCE_VIDEO`, `SPLAT_PLY`, `METADATA_JSON`. `docs/API.md` specifies one asset per scene:
`GET /api/scenes/:id/asset` -> "the `.ply`, or a redirect to it". Shipping a mesh to mobile while
keeping splats for the dashboard means **two deliverables per scene**, which the contract does not
express.

**Concrete cost, per track:**

- **Contract** (`docs/API.md`, all three of us must agree): add `MESH_GLB` (or similar) to
  `AssetKind`; make the asset endpoint select a kind, e.g. `GET /api/scenes/:id/asset?kind=mesh`
  defaulting to `ply` so existing callers keep working; add the mesh's `sha256`/`byteSize` to
  `metadata.json`.
- **Web** (mine): Prisma migration for the enum, the endpoint's `where` clause and its custody
  audit row per kind, plus tests for "mesh requested but only ply exists". Half a day, low risk —
  the "or a redirect" seam FTW-13 preserved is what makes this cheap.
- **GPU** (mine): a meshing step in the pipeline, and this is the real work — not the endpoint.
  Poisson from splat centres is no longer the obvious default; `gsplat-unity` merged an in-editor
  **voxel marching cubes** mesh generator (PR #43, 2026-08-02) modelled on PlayCanvas
  `splat-transform`. Important: **the PlayCanvas design targets collision and outputs geometry
  only, no colour**, so "textured mesh" needs a separate colour-bake step (render splat views and
  project, or transfer per-vertex colour from nearest splat centres). Alternative with no
  retraining: `3DGS-to-PC` (ICCVW 2025), whose own paper concedes surface noise for larger
  gaussians. SuGaR/Frosting give better meshes but **require retraining**, so they do not apply to
  our finished splatfacto output.
- **Unity**: renders a mesh, which is ordinary work, and drops the splat renderer dependency
  entirely on mobile.

**What it costs the project's argument.** Meshing discards exactly what 3DGS is good at:
view-dependent appearance, soft/thin structures, semi-transparency. Metric geometry survives. The
handbook already frames this as "a legitimate engineering answer, not a failure, as long as you say
clearly what runs where and why" — and it is, *provided* we still demonstrate splats somewhere,
which the dashboard preview does.

**We should build the mesh regardless of which option wins**, because the ballistics overlay needs
collision geometry (handbook Section 09, "Colliders from splats") and the deviation-vs-baseline
comparison is named as a research contribution. Doing it for colliders and having it available as
the rendering fallback is reuse, not new work.

### Option 5 — WebView / PlayCanvas

PlayCanvas/SuperSplat is genuinely good on mobile (its documented budget is ~1M splats, and we are
at 178k), and a transparent Android WebView can composite over Unity. **But a WebView cannot
receive the ARCore camera pose**, so marker alignment and AR registration — the core of the
project — are lost. Keep it as a **dashboard / desktop scene viewer**, which Track B wants anyway,
and not as the AR path. Browser-path performance is also materially worse than native (Adreno 740
via WebGL reported at 5-20 fps).

## Performance expectation for our 178k scene

180k splats at 30 fps on a mid-range Adreno phone looks realistic; **60 fps should not be
promised.** Supporting figures, all from primary sources and all secondhand to us until FTW-30
measures our own scene:

| Source | Figure |
|---|---|
| ninjamode VR fork README | Quest 3 (Adreno 740), 72 fps stable to ~400k splats, stereo |
| PlayCanvas mobile guidance | ~1M splat budget |
| gsplat-unity issue #10 | Quest 3, 500k splats, 20 fps (pessimistic anchor) |
| gsplat-unity PR #18 | splat downscale took Quest 3 from 29 -> 45 fps |
| aras-p issue #187 | Adreno 740 fine; >100k at full res and consistent 60 fps "wants something very fast" |

Stereo VR is roughly 2x a phone's cost, so 178k mono has real headroom. **Fill rate, not sort
cost, is the likely binding constraint** — dense splats mean dozens to hundreds of fragments per
pixel — so disable MSAA and consider a lower render scale before touching splat count.

**Device choice matters and is nearly free to get right: prefer a Snapdragon/Adreno phone for the
test device.** Adreno 740/750 evidence is solid across sources; Mali evidence is thin and the one
report found was negative (Mali-G68, nothing displayed). This should feed FTW-12's device audit.

## Settings checklist (any splat option)

Cheap to get wrong, cheap to get right:

- Vulkan only in the Android graphics API list — do not leave GLES3 as a silent fallback onto a
  path that cannot run the sort. (PR #14 currently sets Vulkan **then** GLES3.)
- Uncheck `Apply display rotation during rendering` (Vulkan Settings) — gsplat-unity requires it.
- Disable multithreaded rendering.
- Disable MSAA; consider render scale < 1.
- **Never the Low/VeryLow quality preset**: aras-p issue #44 notes those use **BC7 compression,
  which mobile does not support**. The presets that look like the obvious mobile choice are the
  broken ones. Use High/VeryHigh, or ASTC.
- Sort every Nth frame with a camera-movement threshold. gsplat-unity PR #20 reports 1-in-60
  having "almost no effect on the feel" for a "massive performance boost".

## What I have already done from my side

`tools/decimate_splats.py` (FTW-38, branch `gpu/FTW-38-mobile-splat-budget`) shrinks the export
**43 MB -> 12 MB with zero geometry change**, by truncating spherical harmonics: 45 of the 62 float
properties are SH, 72.6% of the file. Positions, scales, rotations, opacity and base colour come
out bit-identical, so `unitScale 0.369573` and every measurement derived from it are untouched.
Measured colour cost at degree 0 is a mean of 3.9/255 sRGB levels, p99 19.2, worst case 87 — full
table and method in `docs/RESULTS.md`.

This helps **every** option including the aras-p port, and it does not need the device measurement
first, which is why it was safe to do before FTW-30 lands.

One caveat that bears on Option 2's prune threshold: **an opacity threshold is nearly useless on
our exports**, because splatfacto already prunes at `cull-alpha-thresh 0.1`. Only 1.32% of scene
1's splats sit below sigmoid opacity 0.1. gsplat-unity's "0.05 removes ~40% of splats" will not
reproduce on our data — that figure comes from scans trained without that culling.

## Suggested sequence

1. **Try Option 1, then Option 2, on the phone with a real `.ply`.** Hours, not days, and either
   one lands I1 (6 Sep) with splats intact. Use the 12 MB degree-0 export.
2. **Get any FPS number into `docs/RESULTS.md`**, even a bad one. The Mobile rendering table is
   empty, and it unblocks FTW-38's count budget and FTW-30.
3. **Rewrite FTW-44 only if both fail.** If it proceeds, split `SplatUtilities.compute` rather than
   writing a bitonic sort.
4. **Build the collision mesh anyway**, for ballistics — it is also the Option 4 fallback.
5. **Decide by 9 Oct**, the date the handbook already sets for this call.

## Verification status

- Verified by reading the sources directly: the pragma/kernel colocation table, aras-p's README
  platform warning, gsplat-unity's Android and Gamma statements, UnitySplats' platform table and
  licence and its `GsplatSorter.cs` CPU-sort fallback, gsplat-unity's `Opacity Prune Threshold`.
- Verified by measurement on our own data: every decimation number, and the opacity-distribution
  claim.
- **Not verified, treat as secondhand**: all FPS figures (other people's devices and scenes; ours
  is FTW-30's job), the Niantic patch recipe, and the claim that UnitySplats works on our specific
  phone. Nothing here substitutes for one build on one real device.
- **Could not access**: SIGGRAPH Labs '25 course "Optimizing Real-Time Gaussian Splat Rendering for
  Mobile and Standalone VR" (doi 10.1145/3721251.3734056) is paywalled. It is the closest thing to
  an authoritative guide to this exact problem and is worth pulling through FAST's ACM access.
