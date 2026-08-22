# Track A · GPU reconstruction

Owner: Wahaj. Faizan is the second pair of hands.

Video in, metric-scaled `.ply` plus `metadata.json` out. COLMAP for SfM poses, Nerfstudio
`splatfacto` for training, running on Ubuntu 22.04 under WSL2 with CUDA 12.x.

Bring-up order is the Track A first light checklist in handbook Section 05. Do not skip ahead;
each step tells you whether the previous one actually worked.

Two WSL traps worth repeating:

- Keep datasets and outputs inside the Linux filesystem (`~/...`), never under `/mnt/c/...`,
  or every file read crosses a filesystem boundary and training crawls.
- WSL2 caps RAM by default. If SfM dies on a larger frame set, raise the limit in `.wslconfig`
  on the Windows side before assuming the scene is too big.

Every run's numbers go to `../docs/RESULTS.md` the same day.
