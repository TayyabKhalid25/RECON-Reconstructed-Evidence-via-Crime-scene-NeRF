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
| OS (GPU track) | Ubuntu 22.04 under WSL2 | | W |
| CUDA toolkit | 12.x | | W |
| Python | 3.10 or 3.11, whatever Nerfstudio's docs specify | | W |
| COLMAP | 3.x | | W |
| Nerfstudio (splatfacto) | current stable | | W |
| Unity | 6 LTS with URP | | T |
| AR Foundation | 6.x | | T |
| Splat renderer | aras-p/UnityGaussianSplatting | | T |
| Physics | PhysX, bundled with Unity | | T |

Rule for the GPU track: no bleeding edge Python. Torch and CUDA wheels lag new Python releases
by months. Use the version the reconstruction toolchain's own install docs specify.

Once this table is filled, the proposal, the tech stack diagram, and the Gantt all match it.
