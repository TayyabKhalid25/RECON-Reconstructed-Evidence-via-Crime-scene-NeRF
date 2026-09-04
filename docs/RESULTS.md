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

## Collider mesh from splats, measured 2026-09-02 (legion)

Handbook Section 07 wants a "Poisson mesh export path working, for colliders", and Section 09 names
the comparison of this mesh against the AR-plane baseline as the project's research contribution.
`tools/splat_to_mesh.py` on `exports/1/splat_unity.ply` (scene 1, 178 333 splats).

| | Poisson (depth 9) | Voxel marching cubes (0.05 units) |
|---|---|---|
| Splats used | 141 522 of 178 333 (79.4 %) | same |
| Triangles before decimation | 386 356 | 512 518 |
| Triangles after | 50 000 | 49 999 |
| Vertices | 25 129 | 21 250 |
| Disconnected clusters | **1 091** | **26 705** |
| Removed as noise (<100 tris) | 12 187 triangles | 300 419 triangles |
| Bounding-box coverage per axis | 79 / 71 / 84 % | 58 / 49 / 44 % |
| Vertex-to-splat distance, median | 0.105 units (≈3.9 cm) | 0.039 units (≈1.4 cm) |
| Vertex-to-splat distance, p95 | 0.334 units (≈12.3 cm) | 0.069 units (≈2.5 cm) |
| Watertight | no | no |

Distances are in scene units; the centimetre figures apply unitScale 0.369573 and are indicative
only. Raw report: `docs/results/2026-09-02-scene1-collider-mesh.json`.

**Poisson is the right default and the cluster count is why.** A splat cloud is sparse oriented
points, not a dense scan: at 0.05 scene units the occupancy grid fragments into **26 705**
disconnected shells, and cleaning those away deletes half the room. Poisson fits one global
implicit surface, so it produces 1 091 clusters and keeps 79-84 % of the extent.

**The two methods fail in opposite directions, which is exactly why both are implemented.** Voxel
sits closer to the splats (median 1.4 cm against 3.9 cm) because it only ever puts surface where
splats were — but it leaves holes, and a hole in a collider means shots pass through a wall.
Poisson covers the room but smooths, and it invents surface in unobserved regions, which is what
`--density-quantile` trims. For Challenge 1 ("how much mesh approximation error is tolerable before
trajectories diverge") this table is the starting point: the same shot fired into these two
colliders should diverge by something close to the difference in these deviation figures.

Neither mesh is watertight yet, and coverage is not 100 %. Some of that shortfall is intended — the
density trim removes the sparse fringe, which is where the bounding-box extremes are — but it has
not been separated from genuine gaps. **Nothing here is validated against a physical measurement
yet**, so these are geometry-vs-geometry numbers, not accuracy numbers.

A bug found and fixed while producing this, recorded because it would have been invisible: the
cleanup step originally dropped clusters smaller than 0.1 % of total triangles. That relative
threshold silently cut the mesh to 8.0 x 4.7 x 7.7 scene units against a 24.7 x 15.5 x 23.5 splat
extent — a collider covering a third of the room, with physics still running and shots passing
through the rest. The threshold is now absolute, and the tool prints per-axis coverage on every run
so the same class of failure is loud rather than silent.

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

