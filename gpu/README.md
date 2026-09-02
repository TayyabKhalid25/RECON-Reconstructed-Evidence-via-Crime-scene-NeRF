# Track A · GPU reconstruction

Owner: Wahaj. Faizan is the second pair of hands (unchanged as of the 30 Aug 2026 role update).

Video in, metric-scaled `.ply` plus `metadata.json` out. COLMAP for SfM poses, Nerfstudio
`splatfacto` for training, running on Ubuntu 22.04 under WSL2 with CUDA 12.x.

Bring-up order is the Track A first light checklist in handbook Section 05. Do not skip ahead;
each step tells you whether the previous one actually worked.

Two WSL traps worth repeating:

- Keep datasets and outputs inside the Linux filesystem (`~/...`), never under `/mnt/c/...`,
  or every file read crosses a filesystem boundary and training crawls.
- WSL2 caps RAM by default. If SfM dies on a larger frame set, raise the limit in `.wslconfig`
  on the Windows side before assuming the scene is too big.

## One command

```bash
conda activate recon          # not a full path to the env's python, see below
python gpu/run_scene.py --video ~/datasets/1.mp4 --scene 1 --machine legion
```

`gpu/run_scene.py` runs the whole docs/CAPTURE.md sequence and produces a Unity-convention
`.ply` plus a complete `metadata.json`. Track B's worker invokes this, so it returns a non-zero
exit code and a readable reason rather than half-succeeding.

Eleven stages, any of which can be the start or end point, so a failed run resumes instead of
recomputing COLMAP:

```
preflight  process  pickmodel  register  train  eval  export  unitscale  convert  metadata  results
```

```bash
python gpu/run_scene.py --scene 1 --from unitscale     # resume after a crash
python gpu/run_scene.py --scene 1 --only register      # just re-check the gate
python gpu/run_scene.py --scene 3 --video x.mp4 --dry-run
python gpu/run_scene.py --self-test
```

Measurements from finished stages persist in `docs/results/<scene>-run-state.json`, so resuming
does not silently produce a `metadata.json` with a null `unitScale` and no metrics.

### Three gates, and why each exists

| Gate | Stops when | Because |
|---|---|---|
| `register` | fewer than `--min-registered` percent of frames posed (default 80) | Training on bad poses yields a confident wrong scene. Its message reminds you to rule out the two-sparse-models trap before recapturing |
| `unitscale` | relative spread above `--max-spread` (default 5 %) | A scale that disagrees with itself across view pairs is not a scale, and every distance measured later inherits the error |
| `convert` | the written `.ply` header does not declare left-handed, Y-up | Per `../docs/FRAMES.md`. Catches a mirrored or sideways room before it reaches a phone |

`metadata` additionally refuses to write an incomplete `metadata.json` unless you pass
`--allow-partial`, and names exactly which fields are missing.

**Scale is deliberately not baked into the `.ply`.** The export stays in scene units and
`unitScale` travels in `metadata.json` for Unity to apply, so `convert` is invoked *without*
`--unit-scale` on purpose. Baking it here and applying it there again is the double-scale bug,
and it presents as a modelling error rather than a unit error. Verified against first light:
`splat_unity.ply` measures 25.1 units across, 9.28 m once scaled.

**Peak VRAM is sampled during training** into both `metadata.json` and the results row, because
that number only exists while the process is alive — it was missed on the first real run and
could not be recovered.

Two practical notes. Run inside an **activated** env: the `ns-*` entry points and `ninja` live in
the env's `bin/`, so invoking the env's `python` by full path fails (`preflight` catches this, and
a missing binary now explains itself instead of dumping a traceback). And pass `--machine legion`
if the hostname is not the name used in `../docs/RESULTS.md` — the default is the real hostname,
which on this laptop is `Laptop`.

## Tools, in pipeline order

| Tool | What it is for |
|---|---|
| `gpu/run_scene.py` | The whole pipeline as one command, with the gates above. `--self-test` covers the pure logic |
| `tools/patch_nerfstudio_colmap313.py` | Reconciles Nerfstudio 1.1.5's CLI with COLMAP 3.13. Re-run after any env rebuild; `--check` is read-only |
| `tools/pick_colmap_model.py` | Detects the two-sparse-models trap and promotes the largest model to `sparse/0`; `--check` is read-only |
| `tools/compute_unitscale.py` | Finds the printed marker in the reconstruction and derives metres per scene unit |
| `tools/convert_ply_to_unity.py` | The one and only frame conversion, per `../docs/FRAMES.md`. Never flip on the Unity side |
| `tools/decimate_splats.py` | Shrinks an export toward a mobile budget, and measures what it cost |

Each takes `--self-test` or `--check` where the operation is destructive enough to want one.

### Decimating for mobile

Two levers, and the order matters:

```bash
# See the trade before choosing. Read-only.
python tools/decimate_splats.py exports/1/splat_unity.ply --sh-report

# Spherical harmonics: 3.65x smaller, no geometry change at all.
python tools/decimate_splats.py in.ply out.ply --sh-degree 0
```

`--sh-degree` costs view-dependent colour only. Positions, scales, rotations, opacity and base
colour come out bit-identical, so `unitScale` and every accuracy figure derived from it are
unaffected — pull this lever first. Measured cost on scene 1 is a mean of 3.9 sRGB levels out of
255, with the p99 and worst-case columns in `../docs/RESULTS.md`; quote all three, not just the mean.

`--max-splats` costs geometry, so it can move a measurement. It has no default on purpose: FTW-38
requires the budget to come from a real device frame rate, and until FTW-16/FTW-30 produce one there
is no honest number to hardcode. An opacity *threshold* is not the right ranking here — splatfacto
already pruned at `cull-alpha-thresh 0.1`, leaving only 1.3 percent of splats below it — so the cap
ranks by opacity x cross-section instead.

Every run's numbers go to `../docs/RESULTS.md` the same day.
