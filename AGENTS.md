# Agent instructions for this repo

This is RECON, an FYP at FAST Lahore: phone video in, 3D Gaussian splat scene out, viewed in
Unity AR with a physics overlay. Three tracks: `gpu/` (Wahaj), `web/` (Faizan), `Unity/`
(Tayyab). Read `docs/Forensic-NeRF-FYP-Handbook.md` before doing anything substantial; it is the
single source of truth for the plan, and `docs/API.md` is the contract the three tracks build
against.

## Session log, non negotiable

Maintain `docs/SESSION_LOG.md`. Do not treat it as an end-of-session chore:

- **At session start**, append a new entry: date, who is driving, machine, goal.
- **As you work**, keep the entry updated after each meaningful step: files changed, commands
  that mattered, decisions taken and why, anything measured (times, VRAM, PSNR, FPS also goes
  to `docs/RESULTS.md`).
- **Before the session ends**, finish the entry with state of the tree (committed or not),
  what is broken or half done, and the exact next step so the next session starts cold in
  under a minute.

If a session dies mid-work, the log is the recovery path. An entry that only says "worked on
stuff" is worse than no entry.

## Rules that keep the repo healthy

- Never commit videos, images datasets, `.ply`, `.splat`, or anything in `captures/`,
  `outputs/`, `datasets/`. The .gitignore enforces this; do not weaken it. Large binaries move
  over Tailscale or a drive link.
- `docs/API.md` is a contract. Changing it requires all three team members to know first.
- The frame convention in `docs/FRAMES.md` is fixed: convert once, GPU side, before export.
  Never add a coordinate flip on the Unity side.
- Record exact installed versions in `docs/STACK.md` the day you install them. No silent
  upgrades mid semester.
- Numbers land in `docs/RESULTS.md` the day they are produced, with date and machine.
- Do not overclaim in any document: claims stay consistent with the stated objectives
  (28 dB PSNR, 2 cm positional accuracy). Version-sensitive facts get flagged for verification,
  not asserted.
- Report prose for graded deliverables is written by the team, not generated. Help structure
  and revise, do not produce large blocks of finished prose for pasting into reports.
