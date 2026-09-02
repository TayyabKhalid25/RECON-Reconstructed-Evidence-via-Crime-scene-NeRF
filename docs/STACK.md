# Pinned stack

Fill the right column with the exact version you actually installed, on the day you install it.
Nobody upgrades anything mid semester without telling the other two. A silent minor version bump
that breaks one track in November is a self inflicted wound.

| Layer | Pinned | Installed (exact) | Who |
|---|---|---|---|
| Node | 22 LTS (was 20 LTS, see note) | v22.23.2 | W |
| Next.js | App Router, current stable | 16.3.4 (React 19.2.8) | W |
| Prisma | 5 | 5.22.0 (`prisma` + `@prisma/client`) | W |
| PostgreSQL | 16 | 16, `postgres:16` image via `web/docker-compose.yml` | W |
| Redis + BullMQ | current stable | Redis 7 (`redis:7-alpine`), BullMQ 6.3.4, ioredis 6.0.0 | W |
| Dashboard data layer | SWR | 2.5.1, added 2026-09-02 (see note below) | W |
| Asset storage | disk + Tailscale tunnel, no object storage | `ASSET_STORAGE=disk`, decided 2026-09-01 | W |
| OS (GPU track) | Ubuntu 22.04 under WSL2 | Ubuntu 22.04.5 LTS, WSL2 | W |
| CUDA toolkit | 12.x | 12.6, nvcc V12.6.85 | W |
| Python | 3.10 or 3.11, whatever Nerfstudio's docs specify | 3.11.16 (Miniforge env `recon`) | W |
| PyTorch | matched to the CUDA toolkit | 2.7.1+cu126 (torchvision 0.22.1+cu126) | W |
| COLMAP | 3.x | 3.13.0, conda-forge build `cuda_126h5ca8012_3` | W |
| Nerfstudio (splatfacto) | current stable | 1.1.5 (gsplat 1.4.0, kernels JIT-built for sm_89) | W |
| Unity | 6 LTS with URP | 6000.3.22f1 | T |
| URP | 17.x | 17.3.0 | T |
| AR Foundation | 6.x | 6.6.1 | T |
| ARCore / ARKit | 6.5.x | 6.5.0 | T |
| XR Management | 4.6.x | 4.6.0 | T |
| Android Build Tools | Unity default | OpenJDK 17, Android SDK 34 | T |
| Splat renderer | aras-p/UnityGaussianSplatting | Git hash 2c6fed3 (will be embedded and modified for mobile) | T |
| Physics | PhysX, bundled with Unity | | T |

Rule for the GPU track: no bleeding edge Python. Torch and CUDA wheels lag new Python releases
by months. Use the version the reconstruction toolchain's own install docs specify.

## SWR, added 2026-09-02

The dashboard was first written with hand-rolled `useEffect` fetching and no new dependency, on the
grounds that this repo does not add packages casually. That did not survive the toolchain:
`react-hooks/set-state-in-effect` — part of React Compiler's rule set, on by default in
`eslint-config-next` 16 — rejects `setState` reachable from an effect body, `await` or no `await`,
and points at "You Might Not Need an Effect". The rule is right (cascading renders on every fetch)
and the accepted answer is a data layer, which is what the `building-dashboard-ui` skill
recommended in the first place.

**SWR 2.5.1. One dependency, not the five shadcn/ui would have brought.** UI primitives stay
hand-rolled in `web/src/components/ui.tsx`, because the angular dark-blue look is not shadcn's
default and most of the library would have been fought rather than used.

It also gets the polling policy right: `refreshInterval` is a function of the last response, so a
job reaching READY or FAILED stops polling by itself, and SWR pauses while the tab is hidden. Both
matter against a free-tier database.

## Asset storage, decided 2026-09-01 (handbook Decision 4, FTW-13)

**Assets live on the GPU machine's disk and are served through the existing Tailscale tunnel.
Cloudflare R2 is not adopted this semester.** `ASSET_STORAGE=disk`, `ASSET_DIR=./storage`.

- **Volume does not justify object storage.** The first-light `.ply` is 44 MB, and captures are
  1080p per `docs/CAPTURE.md`, not 4K. Six scenes with repeats is single-digit GB; the current
  working set is 441 MB of `outputs/` plus 85 MB of `exports/`.
- **Zero budget is the deciding factor.** R2 generally wants a payment method on file even at zero
  usage. A card on a metered service is a live risk on a student project: one misconfigured loop or
  an accidentally public bucket is a bill nobody agreed to. Disk has no billing surface.
- **The tunnel already exists and is proven.** Tailscale with MagicDNS is up (FTW-5, FTW-6),
  `docs/NETWORK.md` documents phone-to-dev-API over it, and Taildrop already moved the first-light
  `.ply` between machines. Assets also stay next to where they are produced, which removes an
  upload step from the worker.
- **`docs/API.md` does not change.** `GET /api/scenes/:id/asset` is already specified as "the
  `.ply`, or a redirect to it". Disk serving returns bytes; object storage would return a redirect.
  Keep that seam — it is what makes this reversible.

Accepted consequences: assets are reachable only while the Legion is up and on the tailnet, the
same constraint the dev API already has. Availability is not graded, and the handbook names local
Docker Compose plus Tailscale as a complete demo substitute. Backups are manual — `.ply` files stay
out of git (`exports/` is gitignored) and anything a report depends on gets copied off this disk.

Revisit only on a real trigger: assets needed with the Legion off, a demo where the tailnet is
unavailable, or storage outgrowing the disk.

## Encryption at rest, implemented 2026-09-02

Handbook Section 08 asks us to say **which** of the three things called "encryption at rest" we
did, and to state plainly where the key lives and what an attacker with only database access can
read. This is that statement, and the report should reuse it rather than paraphrasing.

**What was implemented: option 3, application level envelope encryption.** Asset bytes are
encrypted with AES-256-GCM before they are ever written to disk. The key lives in
`ASSET_ENCRYPTION_KEY` in `.env`, outside the database. The 96-bit IV and the 128-bit auth tag are
stored alongside the ciphertext in the file itself.

On-disk layout, `web/src/lib/encryption.ts`:

```
MAGIC(8) | IV(12) | TAG(16) | ciphertext
"RECONAG1"
```

The magic prefix is what makes the rollout reversible in both directions: a reader can tell an
encrypted file from a plaintext one, so encryption can be switched on for a deployment that already
has assets on disk with no migration, no schema change, and no field in `docs/API.md`.

### What an attacker actually gets

| Attacker has | Can read | Cannot read |
|---|---|---|
| Database only | Metadata: filenames, plaintext sizes, SHA-256 of plaintext, case/scene structure, the whole audit chain | **No byte of any video or reconstruction** |
| Filesystem only | Ciphertext, and file sizes | Nothing useful without the key |
| Database **and** the key | Everything | — |
| Provider disk encryption alone | (this is option 1, which we did *not* rely on) | — |

The SHA-256 in the custody record is of the **plaintext** the investigator uploaded, so custody
verification is unaffected by whether encryption is on. `Asset.byteSize` is likewise the plaintext
length; the file on disk is 36 bytes larger.

### Limitations, stated rather than buried

- **Streaming decryption emits plaintext before the auth tag is checked.** GCM authenticates the
  whole ciphertext but a streamed decrypt only discovers a bad tag at the end, so a tampered 44 MB
  `.ply` is partly written to the response before the stream errors. We stream anyway, because
  buffering 44 MB per request to serve a phone over the tunnel is worse. Detection is not weakened,
  only moved: the asset route sends `X-Asset-SHA256` from the custody record and both the GPU worker
  and the Unity client re-hash what they received.
- **One key per environment, and rotation is future work.** There is no key hierarchy and no HSM,
  and the report should say so plainly rather than implying otherwise.
- **Sensitive columns are not yet encrypted.** Section 08 asks for option 2 alongside option 3;
  only the asset half is done. Nothing in the current schema is more sensitive than a case title,
  but that is a reason to scope the claim, not to claim both.
- **Off by default in code.** An existing checkout keeps working; `.env.example` ships `on`, so a
  fresh setup gets it. A missing or malformed key throws rather than silently writing plaintext,
  because "encryption was enabled and quietly did nothing" is the worst available outcome.

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
