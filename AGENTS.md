# Agent instructions for this repo

This is RECON, an FYP at FAST Lahore: phone video in, 3D Gaussian splat scene out, viewed in
Unity AR with a physics overlay. Three tracks: `gpu/` (Wahaj), `web/` (Faizan), `Unity/`
(Tayyab). Read `docs/Forensic-NeRF-FYP-Handbook.md` before doing anything substantial; it is the
single source of truth for the plan, and `docs/API.md` is the contract the three tracks build
against. Project skills for common situations (reconstruction runs, contract changes, frame
bugs, PRs, graded deliverables) are indexed in `SKILLS.md`; when one matches the task at hand,
follow it.

## Session log, non negotiable

Every working session gets its own file in `docs/sessions/`, named
`YYYY-MM-DD-<name>-<slug>.md` (e.g. `2026-08-24-faizan-job-endpoints.md`) so every log is
dated and says whose it is, and no file grows unbounded. Format and rules:
`docs/sessions/README.md`. Do not treat it as an end-of-session chore:

- **At session start**, create the file: date, who is driving, machine, goal.
- **As you work**, keep it updated after each meaningful step: files changed, commands that
  mattered, decisions taken and why, anything measured (times, VRAM, PSNR, FPS also goes to
  `docs/RESULTS.md`).
- **Before the session ends**, close it with state of the tree (branch, committed or not),
  what is broken or half done, and the exact next step so the next session starts cold in
  under a minute.

If a session dies mid-work, the log is the recovery path. An entry that only says "worked on
stuff" is worse than no entry.

## Branches, PRs, and tests

- **No direct commits to main.** All work happens on a feature branch named
  `<track>/<short-description>` (`gpu/colmap-worker`, `web/job-endpoints`, `unity/marker-align`)
  and lands via a pull request.
- **Someone other than the author reviews and merges.** The author never merges their own PR.
  With three people there is always a reviewer; if a PR sits unreviewed for two days, say so in
  the group chat rather than self-merging.
- **Emergency exception, and it is recorded.** The repo is private on a free plan, so GitHub
  does not physically block a self-merge; the rule is discipline. Self-merging is allowed only
  when something genuinely urgent is on fire (a graded gate is hours away, or main is broken
  and blocking everyone). Even then: say it in the group chat first, note "self-merged, reason"
  on the PR and in the session log, and the change still gets a retroactive review within two
  days. "I wanted to keep moving" is not urgent.
- **Keep PRs small and rebased.** Rebase on main before opening and before merging. A PR that
  touches one thing gets reviewed the same day; a two-week branch gets conflict surgery.
- **Unit tests ship with the code, not after it.** Every PR that adds behaviour adds tests for
  it. The natural targets per track:
  - `web/`: endpoint handlers, job state transitions (illegal transitions rejected), the
    custody hash chain (verify passes, then tamper a row and verify fails), SHA-256 on upload.
  - `gpu/`: metadata.json writer against the schema, the frame conversion (a known point in,
    the expected Unity-convention point out), unitScale math from marker detection.
  - `Unity/`: Unity Test Framework, starting with ballistics against the analytic parabola
    (a drag-free shot must match it), spatter ellipse ratio equal to sin of incidence angle.
- **Main stays demoable.** Main is what gets shown at every gate. If a merge breaks the demo
  path, fixing it outranks new work.
- **CI when the first tests exist:** a GitHub Actions workflow running lint plus unit tests on
  every PR, and a passing run required before merge. Unity tests can stay local if CI minutes
  or licensing make them awkward; web and gpu tests are cheap to run in CI from day one.
- **Never force-push main.** Force-push on your own feature branch is fine.
- **Secrets never enter git.** Connection strings and keys live in `.env`, shared privately.
  If a secret does land in a commit, rotating it is the fix; deleting the commit is not enough.

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
