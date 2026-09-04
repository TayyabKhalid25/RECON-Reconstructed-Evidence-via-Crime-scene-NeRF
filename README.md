# RECON-Reconstructed-Evidence-via-Crime-scene-NeRF
Basically Batman's Detective Vision from Arkham Origins, minus the cowl, the trauma, and the billion-dollar trust fund. Made by Tayyab, Wahaj and Faizan as part of our FYP at FAST.

Start here: `AGENTS.md` for how we work, `SKILLS.md` for the agent skills,
`docs/Forensic-NeRF-FYP-Handbook.md` for the full plan.

## Running it in dev

Verified end to end on 2026-09-02. Ubuntu 22.04 under WSL2 with an RTX 4060; exact versions in
`docs/STACK.md`.

### Web app (Track B)

One-time, per machine:

```bash
# Docker Engine inside the WSL distro, not Docker Desktop. Full block in web/SETUP-DOCKER.md
# (needs sudo, so an agent cannot do it for you).
sudo service docker start          # WSL2 has no systemd by default
newgrp docker                      # or open a NEW terminal: usermod only applies at login

cp .env.example .env               # then fill it in, see below
cd web && npm ci
```

`.env` values that matter, and the traps:

| Key | Local value | Trap |
|---|---|---|
| `DATABASE_URL` | `postgresql://recon:recon@localhost:5432/recon` | `.env.example` ships a `host` placeholder; leaving it gives a DNS error on "host" |
| `REDIS_URL` | `redis://localhost:6379` | the example is `rediss://` (TLS). Local Redis has no TLS, so this must be `redis://` |
| `JWT_SECRET` | `openssl rand -hex 32` | ships as `change-me` |
| `ASSET_ENCRYPTION` | `on` | optional; off is also valid |
| `ASSET_ENCRYPTION_KEY` | `openssl rand -hex 32` | with encryption on, a missing or wrong-length key **throws** rather than silently writing plaintext. Do not append this twice — two keys in one file means whichever loses makes already-encrypted assets unreadable |

Every run:

```bash
cd web
npm run db:up                  # postgres 16 + redis 7, both bound to 127.0.0.1 only
docker compose ps              # BOTH must read "healthy" before continuing
npm run db:migrate             # first run creates prisma/migrations/
npm run db:seed                # 3 users + 1 case
npm run dev                    # http://localhost:3000
```

Seeded logins (dev only): `admin@recon.local` / `admin-dev`,
`investigator@recon.local` / `investigator-dev`, `viewer@recon.local` / `viewer-dev`.

Before every push:

```bash
npm run verify   # typecheck + lint + check:custody + check:encryption
```

CI runs that exact command, so "passed locally" and "passed in CI" mean the same thing.

Two gotchas that cost real time:

- **The Docker daemon does not survive a WSL restart.** Either `sudo service docker start` each
  time, or set `systemd=true` under `[boot]` in `/etc/wsl.conf` and `wsl --shutdown` once from
  Windows.
- **`.next/types` goes stale across branch switches** and typecheck then fails on a route that
  does not exist on this branch. Fix: `npx next typegen`.

### Reconstruction pipeline (Track A)

One command, video in to Unity-convention `.ply` plus `metadata.json` out:

```bash
conda activate recon           # NOT a full path to the env's python: the ns-* commands
                               # and ninja live in the env's bin/ and need it on PATH
python gpu/run_scene.py --video ~/datasets/1.mp4 --scene 1 --machine legion
```

Eleven stages, any of which can be the start or end point, so a failed run resumes rather than
recomputing COLMAP:

```
preflight  process  pickmodel  register  train  eval  export  unitscale  convert  metadata  results
```

```bash
python gpu/run_scene.py --scene 1 --from unitscale    # resume after a crash
python gpu/run_scene.py --scene 1 --only register     # just re-check a gate
python gpu/run_scene.py --scene 3 --video x.mp4 --dry-run
```

Three gates stop the run rather than letting it produce a confident wrong answer: registration
below 80 %, a `unitScale` that disagrees with itself across view pairs, and an export that does
not declare the Unity frame. Details and the other tools: `gpu/README.md`.

**Do not switch git branches while a pipeline is running.** It reads `tools/` from the working
tree; a branch switch mid-run cost twelve minutes of COLMAP once.

Then, for a mobile-sized asset:

```bash
python tools/decimate_splats.py exports/1/splat_unity.ply out.ply --sh-degree 0   # 43 MB -> 12 MB
python tools/splat_to_mesh.py exports/1/splat_unity.ply mesh.ply --method poisson  # collider mesh
```

### The whole loop

```
phone video
  -> gpu/run_scene.py                    .ply (scene units, Unity frame) + metadata.json
  -> POST /api/uploads                   sha256 + audit row + job enqueued
  -> worker drives job to READY          (not built yet: see FTW-37 follow-ups)
  -> GET /api/scenes/:id/asset           Unity fetches by scene id
  -> Unity applies unitScale             the twin lands at real size
```

`unitScale` is the seam to be careful with: the `.ply` stays in **scene units** and Unity applies
`unitScale` from `metadata.json`. Applying it in both places is the double-scale bug, and it looks
like a modelling error rather than a unit error. See `docs/FRAMES.md`.

### Unity (Track C)

Open `Unity/Recon` in the pinned editor (`docs/STACK.md`). Mobile splat rendering is the open
risk; the renderer options and the settings checklist are in `docs/MOBILE-SPLAT-OPTIONS.md`.

## Jira

The board lives at [reconfyp.atlassian.net, project FTW](https://reconfyp.atlassian.net/jira/software/projects/FTW/boards/1).
The backlog is seeded; tickets with your name in the title are yours, assign them to yourself
once you have joined.

**Join (once):** accept the email invite to the Jira space. No invite? Ask Wahaj.

**Hook your agent up (once per machine), pick one:**

1. **MCP, recommended.** The repo already ships `.mcp.json` pointing at Atlassian's MCP
   server. In a Claude Code session inside this repo, run `/mcp`, pick `atlassian`, and
   complete the browser OAuth with your own Atlassian account. Done: your agent can read,
   create, update, and transition tickets ("move FTW-14 to In Review").
2. **REST fallback.** Create an API token at
   [id.atlassian.com](https://id.atlassian.com/manage-profile/security/api-tokens), copy
   `.env.example` to `.env` (untracked), and fill the four `JIRA_*` values with your own
   email and token. Agents then drive Jira over REST. Never commit the token; CI fails the
   build if a `.env` lands in the tree.

**Conventions:**

- Branch names carry the ticket key: `gpu/FTW-14-first-light`, `web/FTW-15-scaffold`.
- Mention the key (`FTW-14`) in commits and PRs; the GitHub-for-Jira app is installed, so
  those auto-link to the ticket.
- Labels: `track-gpu` / `track-web` / `track-unity` / `infra` / `proposal`. Due-dated tickets
  are real deadlines from the handbook, not decoration.
