# Locked training preset, 8 GB class GPU

Every scene after first light trains with this preset so results are comparable. Deviations get
recorded in `docs/RESULTS.md` next to the numbers they produced.

## The preset

```bash
conda activate recon                 # sets TORCH_CUDA_ARCH_LIST=8.9
ns-train splatfacto \
  --data ~/datasets/<scene> \
  --max-num-iterations 7000 \
  --pipeline.model.num-downscales 2
```

Everything else stays at the splatfacto defaults, which are recorded below so a future change is
visible as a change.

| Knob | Value | Source |
|---|---|---|
| `max-num-iterations` | 7000 | reduced from 30000 for a fast feedback loop |
| `num-downscales` | 2 | trains at 1/4 resolution initially, doubling on the schedule |
| `resolution-schedule` | 3000 | default |
| `warmup-length` | 500 | default |
| `refine-every` | 100 | default |
| `cull-alpha-thresh` | 0.1 | default |
| `densify-grad-thresh` | 0.0008 | default |
| `stop-split-at` | 15000 | default; above `max-num-iterations`, so splitting never stops early at 7000 |
| `sh-degree` | 3 | default |

## Measured, not assumed

Scene 1 (304 frames from 4K source, RTX 4060 Laptop 8 GB, WSL2), sampled every 0.5 s with
`nvidia-smi` for the whole run:

| Measurement | Value |
|---|---|
| **Peak VRAM** | **1197 MiB of 8188 (14.6 %)** |
| Mean VRAM | 924 MiB |
| Wall clock | **2 min 17 s** |
| Splats produced | 180 137 |
| Max GPU temp | 74 C |
| Max power | 75 W |
| Max SM clock | 2700 MHz (no throttling observed) |
| Peak system RAM | 2.4 GB |

## Validated on a second capture

FTW-26 asked for a second capture through this preset without OOM. Scene 2 (4K60 **portrait**, 316
frames) delivered it:

| | Scene 1 (landscape) | Scene 2 (portrait) |
|---|---|---|
| Peak VRAM | 1197 MiB (14.6 %) | **1441 MiB (17.6 %)** |
| Wall clock | 2 min 17 s | 2 min 07 s |
| Splats | 180 137 | 251 879 |
| PSNR | 31.37 | 30.13 |

Peak VRAM stayed under 18 percent across both aspect ratios, so the preset is not scene-specific in
any way that matters at this scale. Scene 2 produced 40 percent more splats for 244 MiB more VRAM,
which is the scaling to expect: memory tracks splat count, and splat count tracks scene complexity.

## What this changes

**8 GB is not the binding constraint at these settings.** The run used 14.6 percent of available
VRAM. The handbook's framing is the right one and is now backed by a measurement: *the phone, not
the training GPU, is the ceiling*. Put that argument in the report with these numbers.

Consequences:

- There is large headroom to raise quality: more iterations, `num-downscales 1` or 0, or a bigger
  splat budget. Scene 1 stayed at 7000 iterations for a fast loop, not because 8 GB forced it.
- The OOM ladder in `docs/CAPTURE.md` has not been exercised, because nothing came close to OOM.
  It stays documented as the procedure if a much larger scene ever needs it.
- **The splat budget is provisional.** 180 137 splats trained comfortably here, but nobody has
  measured what a phone does with them. FTW-16 (splat FPS on a real phone) is the number that sets
  the real budget, and until it lands the export splat count is an open question, not a locked one.

## Caveats on these figures

- Peak VRAM is trustworthy: memory use is unaffected by clock throttling.
- The **timing** figure comes from a run that began on battery power and was switched to mains
  mid-run. No throttling signature appeared (clocks held 2700 MHz, temperature peaked at 74 C), so
  2 min 17 s is believable, but a clean mains-only rerun would confirm it. Thermal throttling is the
  first thing to check if a later run takes markedly longer, per `.claude/skills/running-reconstruction`.
- One scene, one machine. A second capture through this preset is what makes it a preset rather
  than one observation, and is the outstanding half of FTW-26.
