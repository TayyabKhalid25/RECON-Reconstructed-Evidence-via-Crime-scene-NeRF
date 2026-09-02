# Results log

Every training run and every measurement lands here the day it happens. Reconstructing this
table from memory in November is impossible, and Chapter 7's test cases are filled from it.

## Reconstruction runs

| Date | Scene | Machine | Frames | Registered % | Iterations | Minutes | Peak VRAM (MB) | PSNR | SSIM | LPIPS | Splats | .ply MB | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2026-08-26 | 1 | legion | 304 | 100% | 7000 | 4.5 | not sampled | 31.37 | 0.956 | 0.103 | 178333 | 43 | First light. splatfacto, num_downscales 2, 4K source. Eval fps 74.0. Metric: unitScale 0.369573 via marker (2.1% spread). Export in Unity frame (splat_unity.ply) |
| 2026-08-26 | 1 (VRAM probe) | legion | 304 | 100% | 7000 | 2.3 | **1197** | - | - | - | 180137 | - | Rerun of scene 1 purely to measure VRAM, sampled every 0.5 s. Peak 1197 of 8188 MiB = 14.6%. Max 74 C, 75 W, 2700 MHz, no throttling. Peak host RAM 2.4 GB. Started on battery, switched to mains mid-run: timing indicative, VRAM valid |
| 2026-08-26 | 2 | legion | 316 | 100% | 7000 | 2.1 | **1441** | 30.13 | 0.946 | 0.118 | 251879 | - | Second capture through CAPTURE.md + PRESET.md, no OOM. 4K60 **portrait**. unitScale 0.313341 via marker, spread 1.86% (marker larger in frame than scene 1). Peak 1441 of 8188 MiB = 17.6%. Mains power throughout |

### Scene 2: the preset validated on a second capture

FTW-26 required a second capture through the protocol and preset without OOM. `20260825_161856.mp4`
(42 s, 4K60 **portrait**) delivered it.

| | Scene 1 | Scene 2 |
|---|---|---|
| Aspect | landscape 4K | **portrait 4K** |
| Frames / registered | 304 / 100 % | 316 / **100 %** |
| Wall clock | 2 min 17 s | 2 min 07 s |
| **Peak VRAM** | 1197 MiB (14.6 %) | **1441 MiB (17.6 %)** |
| Splats | 180 137 | 251 879 |
| PSNR / SSIM / LPIPS | 31.37 / 0.956 / 0.103 | 30.13 / 0.946 / 0.118 |
| unitScale | 0.369573 | 0.313341 |
| Marker detected in | 102 frames | **151 frames** |
| unitScale spread | 2.1 % | **1.86 %** |

Two captures, two aspect ratios, no OOM, and peak VRAM stayed under 18 percent both times. The
preset holds.

**The bigger marker paid off measurably.** Scene 2 was shot with the marker larger in frame, and
detection rose from 102 to 151 frames with the spread tightening from 2.1 to 1.86 percent. That
confirms the `docs/CAPTURE.md` guidance that scale accuracy is bounded by marker size in frame, and
it is the cheapest available lever on accuracy.

Slightly lower PSNR than scene 1 with 40 percent more splats is consistent with a wider, more
complex scene rather than a regression.

### The two-sparse-models trap, and how scene 2 nearly got thrown away

`ns-process-data` initially reported:

```
Colmap matched 4 images
COLMAP only found poses for 1.27% of the images. This is low.
```

That is not what happened. COLMAP's incremental mapper emitted **two** disconnected models:
`sparse/0` with 4 images and `sparse/1` with all **316**, the latter carrying 71 402 points against
scene 1's 62 024 — a *better* reconstruction than first light. Nerfstudio reads `sparse/0`
unconditionally, so it built `transforms.json` from the 4-image fragment and reported catastrophic
failure.

This presents exactly like a bad capture, and the natural response — reshoot — throws away a good
take. `tools/pick_colmap_model.py` now detects it, promotes the largest model to `sparse/0`, and
regenerates `transforms.json`; `--check` reports without changing anything.

Two diagnoses were wrong on the way here and are recorded so nobody repeats them: the run was first
read as a genuine registration failure, then blamed on motion blur. A sharpness check built on that
theory flagged **scene 1** — the 100 percent scene — as too blurred, because scene 1 scores *lower*
variance-of-Laplacian than scene 2 (36 vs 43). Blur was never the cause, and the checker was
discarded rather than committed. Match quality was near-identical between the two scenes throughout
(median inliers per verified pair 543 vs 561), which was the clue that the mapper, not the imagery,
was where the problem lay.

### What the VRAM measurement means

Peak VRAM on the locked preset is **1197 MiB of 8188, i.e. 14.6 percent**. At these settings the
8 GB card is not the binding constraint, which is measured confirmation of the handbook's framing:
the **phone**, not the training GPU, is the ceiling. There is headroom to raise iterations or
resolution when quality matters more than loop speed.

The splat budget therefore remains **provisional**. 180 137 splats train comfortably, but nobody
has measured what a phone does with them; FTW-16 sets the real budget. Full preset and caveats:
`gpu/PRESET.md`. Raw samples: `docs/results/2026-08-26-scene1-vram-samples.csv`.

## Mobile rendering

| Date | Phone | Splat count | FPS | Notes |
|---|---|---|---|---|
| | | | | |

**Still empty, and it is the most important empty table in this file.** Nothing has yet rendered
on a phone, so the mobile splat budget remains provisional and FTW-38 stays blocked on the
measurement. What we do know as of 2026-09-02 is a negative result from Track C (PR #14): the
stock `aras-p/UnityGaussianSplatting` and the `wuyize25/gsplat-unity` fork both use HLSL wave
intrinsics (`wavebasic`, `waveballot`) in the `SplatUtilities.compute` radix sort, which fail to
compile on Mali/Adreno and take the whole shader file down with them. So the FPS figure is
blocked on a renderer that runs at all, not on a capture.

### Export decimation, measured 2026-09-02 (legion)

`tools/decimate_splats.py` on `exports/1/splat_unity.ply` (scene 1, 178 333 splats, 62 properties).
Spherical-harmonic truncation is the first lever because it is the only one that costs no geometry.

| Keep | Properties | Size | Shrink | mean dC | p99 dC | max dC | Splats lost |
|---|---|---|---|---|---|---|---|
| degree 3 (as exported) | 62 | 44.23 MB | 1.00x | - | - | - | - |
| degree 2 | 41 | 29.25 MB | 1.51x | 2.4 / 255 | 12.8 / 255 | 68 / 255 | **none** |
| degree 1 | 26 | 18.55 MB | 2.38x | 3.2 / 255 | 16.4 / 255 | 77 / 255 | **none** |
| **degree 0** | **17** | **12.13 MB** | **3.65x** | **3.9 / 255** | **19.2 / 255** | **87 / 255** | **none** |

dC is absolute radiance error against full degree-3 SH, evaluated over 400 random view directions
on a 20 000-splat sample, in sRGB levels out of 255. Regenerate with
`python tools/decimate_splats.py exports/1/splat_unity.ply --sh-report`.

The 45 `f_rest_*` floats are 72.6 percent of the file and carry a mean absolute coefficient of
0.014 against the base colour's 0.514. Dropping all of them leaves every splat's position, scale,
rotation, opacity and base colour **bit-identical** — verified by column comparison, with the
bounding box unchanged at 9.28 x 7.03 x 8.81 m. **unitScale 0.369573 and every measurement derived
from it therefore survive SH truncation untouched**, which is what makes this lever safe to pull
before the accuracy work in FTW-36 is finished.

Read the three columns honestly rather than quoting only the mean. A mean error of 3.9 levels is
below what a viewer notices across a room, but p99 is 19 levels and the worst splats shift 87. The
large excursions are specular highlights on glass and gloss, which is where view-dependent colour
was doing real work. For a forensic scene the geometry is the evidence and the gloss is not, so
degree 0 is defensible; it should still be stated as a trade, not as a free win.

An opacity **threshold** is not a useful lever on our exports: splatfacto has already pruned at
`cull-alpha-thresh 0.1`, so only 1.32 percent of scene 1's splats sit below sigmoid opacity 0.1 and
0.02 percent below 0.01. Cutting splat *count* therefore has to rank by contribution
(opacity x cross-section), and it costs geometry, so it waits for a device measurement per FTW-38.

For illustration only, not a calibrated budget: degree 0 plus a 100 000-splat cap gives 6.80 MB,
a 6.50x reduction, dropping 43.9 percent of splats.

## Scale checks

| Date | Scene | Real measurement | Reconstructed measurement | Error (cm) |
|---|---|---|---|---|
| 2026-08-26 | 1 | marker outer edge 170.0 mm | 0.45999 scene units -> 170.0 mm at unitScale 0.369573 | 0 by construction; 2.1% spread across 27 view pairs |

Scene 1 **is metric**. `tools/compute_unitscale.py` detected the marker's outer black border in
102 of 304 frames, triangulated its corners through the COLMAP poses, and found 27 of 28 view
pairs self-consistent: **unitScale 0.369573**, i.e. 1 scene unit = 0.3696 m.

The scale check above is circular by construction — the marker defines the scale, so recovering
170.0 mm from it is a closure check, not independent evidence. The meaningful figures are the
**2.1% spread across view pairs** and the independent plausibility check: the scene bounding box
comes out at 9.3 x 7.0 x 8.8 m, which is a sane room. An independent check against a second
measured object is still wanted before quoting a positional-accuracy figure.

**Stated uncertainty: about 2%.** The marker was small in frame for much of this capture, which
bounds corner precision. A closer capture would tighten it, and is the cheapest available
improvement to accuracy claims.

