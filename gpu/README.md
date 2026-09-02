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

## Tools, in pipeline order

| Tool | What it is for |
|---|---|
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
