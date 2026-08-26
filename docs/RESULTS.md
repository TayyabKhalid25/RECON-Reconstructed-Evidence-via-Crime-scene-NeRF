# Results log

Every training run and every measurement lands here the day it happens. Reconstructing this
table from memory in November is impossible, and Chapter 7's test cases are filled from it.

## Reconstruction runs

| Date | Scene | Machine | Frames | Registered % | Iterations | Minutes | Peak VRAM (MB) | PSNR | SSIM | LPIPS | Splats | .ply MB | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2026-08-26 | 1 | legion | 304 | 100% | 7000 | 4.5 | not captured | 31.37 | 0.956 | 0.103 | 178333 | 43 | First light. splatfacto, num_downscales 2, 4K source. Eval fps 74.0. Metric: unitScale 0.369573 via marker (2.1% spread). Export in Unity frame (splat_unity.ply) |

## Mobile rendering

| Date | Phone | Splat count | FPS | Notes |
|---|---|---|---|---|
| | | | | |

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

