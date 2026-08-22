---
name: running-reconstruction
description: Use when capturing a scene, running COLMAP or splatfacto, hitting CUDA out-of-memory, seeing melted or blurry reconstructions, or when training time suddenly doubles between runs.
---

# Running a reconstruction

## Overview

Video in, metric `.ply` plus `metadata.json` out. The expensive mistake is training on bad inputs: 40 minutes of GPU time on bad poses produces a melted scene. Every stage has a checkpoint that tells you whether to proceed.

## The run, with checkpoints

1. **Capture.** Slow walkthrough, 60 to 120 seconds, good even lighting, printed marker visible and flat at the scene origin. No marker means no metric scale, which means the scene is useless for any accuracy claim.
2. **Frames + SfM** (`ns-process-data video`). **Checkpoint: registration.** Fewer than ~80 percent of frames registered, or an implausible camera path? Do not train. Recapture instead.
3. **Train** (`ns-train splatfacto`). First run on a new scene: reduced resolution and fewer iterations for a fast feedback loop. Raise once the scene is known good.
4. **Evaluate.** Held-out PSNR, SSIM, LPIPS. Into `metadata.json` and `docs/RESULTS.md` the same day, with date and machine.
5. **Export.** `.ply` in Unity convention (left handed, Y up, metres, per `docs/FRAMES.md`), `unitScale` computed from the marker, sha256 recorded.

## Out of memory: turn knobs in this order

Downscale images → cap splat count → raise densification threshold → prune harder → tighten scene bounds. Record what you settled on; it belongs in the report.

## Symptom table

| Symptom | First check |
|---|---|
| Training time doubled between runs | Laptop temperature. On battery or a soft surface it throttles. Plug in, hard surface, before touching config |
| Frame extraction or training crawls | Dataset under `/mnt/c/...`. Move it into the Linux filesystem |
| SfM dies on larger frame sets | WSL2 RAM cap. Raise it in `.wslconfig` on the Windows side |
| Scene looks melted | Bad poses. Check registration percentage, recapture |
