# Results log

Every training run and every measurement lands here the day it happens. Reconstructing this
table from memory in November is impossible, and Chapter 7's test cases are filled from it.

## Reconstruction runs

| Date | Scene | Machine | Frames | Registered % | Iterations | Minutes | Peak VRAM (MB) | PSNR | SSIM | LPIPS | Splats | .ply MB | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2026-08-26 | 1 | legion | 304 | 100% | 7000 | 4.5 | not captured | 31.37 | 0.956 | 0.103 | 178333 | 43 | First light. splatfacto, num_downscales 2, 4K source. Eval fps 74.0. Non-metric: no marker scale, no metadata.json. Export is Z-up, not yet converted per FRAMES.md |

## Mobile rendering

| Date | Phone | Splat count | FPS | Notes |
|---|---|---|---|---|
| | | | | |

## Scale checks

| Date | Scene | Real measurement | Reconstructed measurement | Error (cm) |
|---|---|---|---|---|
| 2026-08-26 | 1 | not done | not done | - |

Scene 1 has **no scale check and cannot have one**: the capture does not establish a marker of
known size at the origin, so `unitScale` is undefined and `scaleMethod` is `none`. Per
`docs/FRAMES.md` this scene must not be used for any accuracy claim. It is valid evidence that
the pipeline runs end to end, and valid for the PSNR/SSIM/LPIPS row above, nothing more.

