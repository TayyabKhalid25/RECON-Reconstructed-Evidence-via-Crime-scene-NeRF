# Pinned stack

Fill the right column with the exact version you actually installed, on the day you install it.
Nobody upgrades anything mid semester without telling the other two. A silent minor version bump
that breaks one track in November is a self inflicted wound.

| Layer | Pinned | Installed (exact) | Who |
|---|---|---|---|
| Node | 20 LTS | | W |
| Next.js | App Router, current stable | | W |
| Prisma | 5 | | W |
| PostgreSQL | 16 | | W |
| Redis + BullMQ | current stable | | W |
| OS (GPU track) | Ubuntu 22.04 under WSL2 | Ubuntu 22.04.5 LTS, WSL2 | W |
| CUDA toolkit | 12.x | 12.6, nvcc V12.6.85 | W |
| Python | 3.10 or 3.11, whatever Nerfstudio's docs specify | 3.11.16 (Miniforge env `recon`) | W |
| PyTorch | matched to the CUDA toolkit | 2.7.1+cu126 (torchvision 0.22.1+cu126) | W |
| COLMAP | 3.x | 3.13.0, conda-forge build `cuda_126h5ca8012_3` | W |
| Nerfstudio (splatfacto) | current stable | 1.1.5 (gsplat 1.4.0, kernels JIT-built for sm_89) | W |
| Unity | 6 LTS with URP | | T |
| AR Foundation | 6.x | | T |
| Splat renderer | aras-p/UnityGaussianSplatting | | T |
| Physics | PhysX, bundled with Unity | | T |

Rule for the GPU track: no bleeding edge Python. Torch and CUDA wheels lag new Python releases
by months. Use the version the reconstruction toolchain's own install docs specify.

## GPU track, as installed 2026-08-23 (Legion, RTX 4060 Laptop, 8 GB)

- Toolkit installed as `cuda-toolkit-12-6` from NVIDIA's `wsl-ubuntu` repo. Never the `cuda` or
  `cuda-12-6` metapackages: those pull a Linux display driver, and under WSL the GPU comes from
  the Windows driver.
- Environment is a Miniforge env named `recon`, Python 3.11. Miniforge's own base env is Python
  3.14, which has no torch wheels; do not install into base.
- Torch was installed from the cu126 index to match nvcc 12.6. Verified: `torch.cuda.is_available()`
  is True, device reports as RTX 4060 Laptop GPU, and a 4096x4096 matmul runs on device.
- **Build flag for CUDA extensions: `TORCH_CUDA_ARCH_LIST=8.9`.** The card is sm_89 (Ada), but
  torch 2.7.1's bundled arch list stops at sm_86 and sm_90 with no sm_89 cubin — it runs here via
  binary compatibility inside the 8.x family. Anything we compile ourselves (gsplat, tiny-cuda-nn)
  should target 8.9 explicitly, both for correctness of intent and to avoid compiling every arch.
- COLMAP comes from conda-forge, **not** apt: jammy's `colmap` package is built without CUDA,
  and a source build needs a newer CMake than jammy ships. Installed into the same `recon` env
  after a `--dry-run` confirmed it was install-only (160 packages, 392 MB, no upgrades,
  downgrades or removals, and it does not touch python or numpy). torch was re-verified against
  the GPU afterwards and is unaffected. Env is now 6.8 GB.
- Pick a **3.x** COLMAP build deliberately. conda-forge also ships 4.x, which is outside the
  pinned range and would change the CLI that `ns-process-data` drives.
- The CUDA build matters more in WSL than it looks: COLMAP's non-CUDA GPU SIFT path goes through
  OpenGL and wants a display, which a headless WSL box does not have. The CUDA path needs no
  display.
- `ceres-solver` is the **CPU** build (`cpugplhc142d66_210`), so bundle adjustment runs on CPU
  while SIFT extraction and matching use the GPU. Fine at room scale; worth knowing if BA time
  ever dominates a run.
- conda pulled `cuda-version 12.9` and `cuda-cudart 12.9.79` alongside torch's pip-installed
  cu126 runtime. Both coexist: torch resolves its own bundled libraries, and it was verified
  working after the COLMAP install. Do not "align" these by hand.
- **Nerfstudio 1.1.5 does not speak COLMAP 3.13's CLI.** COLMAP 3.13 renamed the SIFT option
  groups; Nerfstudio 1.1.5 still emits the old names, so `ns-process-data` dies with
  `Failed to parse options - unrecognised option '--SiftExtraction.use_gpu'`:

  | Nerfstudio 1.1.5 emits | COLMAP 3.13 expects |
  |---|---|
  | `--SiftExtraction.use_gpu` | `--FeatureExtraction.use_gpu` |
  | `--SiftMatching.use_gpu` | `--FeatureMatching.use_gpu` |

  Fixed by **`tools/patch_nerfstudio_colmap313.py`**, which edits the installed
  `nerfstudio/process_data/colmap_utils.py`. The edit lives in `site-packages`, so it **does not
  survive rebuilding the env or reinstalling nerfstudio** — re-run the script after either.
  `--check` reports status without changing anything (exit 1 if unpatched), so it is safe in a
  setup script. This is a direct consequence of pinning COLMAP 3.13.0 in FTW-23; an older 3.x
  would not need it.
- **gsplat ships no compiled kernels.** `gsplat-1.4.0` is a `py3-none-any` wheel: pip reports
  success having built no CUDA code at all. The kernels are JIT-compiled by torch on first real
  use, into `~/.cache/torch_extensions/py311_cu126/gsplat_cuda/`, **not** into the env. First
  build here took 7.5 minutes; a warm load is ~7 seconds.
- **`TORCH_CUDA_ARCH_LIST=8.9` must be set at run time, not just at install time.** torch's JIT
  cache is keyed on the build config, so a run with the variable unset does not reuse the cached
  sm_89 build — it starts a fresh all-architectures compile and discards the old one. This is set
  automatically by `envs/recon/etc/conda/activate.d/recon_cuda.sh` and in `~/.bashrc`; do not
  remove either.
- **Clearing `~/.cache/torch_extensions` costs 7.5 minutes**, and so does rebuilding the env.
  If a training run ever stalls for minutes before the first iteration, this is why.
- `ninja` must be on `PATH` for the JIT build. It is installed in the env, so run inside an
  activated env; invoking `envs/recon/bin/python` directly by full path fails with "Ninja is
  required to load C++ extensions".
- `tiny-cuda-nn` is deliberately **not** installed. splatfacto uses gsplat; tcnn only accelerates
  nerfacto's hash encoding, so it is a long compile for a model this project does not ship.
- Installing nerfstudio downgraded **numpy 2.4.6 to 1.26.4** (its constraint). torch and COLMAP
  were both re-verified afterwards and are unaffected.
- Nerfstudio's published install docs still say CUDA 11.8 with torch 2.1.2. That is stale: its
  current `pyproject.toml` asks only for `torch>=1.13.1` and dev-pins torch 2.7.1 with gsplat
  1.4.0. The proposal's CUDA 12.x pin stands; do not "fix" it to 11.8.

Once this table is filled, the proposal, the tech stack diagram, and the Gantt all match it.
