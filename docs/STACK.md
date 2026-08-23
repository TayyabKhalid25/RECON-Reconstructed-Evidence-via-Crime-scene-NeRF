# Pinned stack

Fill the right column with the exact version you actually installed, on the day you install it.
Nobody upgrades anything mid semester without telling the other two. A silent minor version bump
that breaks one track in November is a self inflicted wound.

| Layer | Pinned | Installed (exact) | Who |
|---|---|---|---|
| Node | 20 LTS | | F |
| Next.js | App Router, current stable | | F |
| Prisma | 5 | | F |
| PostgreSQL | 16 | | F |
| Redis + BullMQ | current stable | | F |
| OS (GPU track) | Ubuntu 22.04 under WSL2 | Ubuntu 22.04.5 LTS, WSL2 | W |
| CUDA toolkit | 12.x | 12.6, nvcc V12.6.85 | W |
| Python | 3.10 or 3.11, whatever Nerfstudio's docs specify | 3.11.16 (Miniforge env `recon`) | W |
| PyTorch | matched to the CUDA toolkit | 2.7.1+cu126 (torchvision 0.22.1+cu126) | W |
| COLMAP | 3.x | | W |
| Nerfstudio (splatfacto) | current stable | | W |
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
- Nerfstudio's published install docs still say CUDA 11.8 with torch 2.1.2. That is stale: its
  current `pyproject.toml` asks only for `torch>=1.13.1` and dev-pins torch 2.7.1 with gsplat
  1.4.0. The proposal's CUDA 12.x pin stands; do not "fix" it to 11.8.

Once this table is filled, the proposal, the tech stack diagram, and the Gantt all match it.
