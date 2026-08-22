# Session log

Newest entry at the top. Every working session gets one, updated while the work happens.
Format: date, driver, machine, goal, then a running account, then handoff state.

---

## 2026-08-22 (later) · Wahaj · MacBook Air

**Goal:** agent skills for the repo.

- Added seven build skills under `.claude/skills/` with real implementation content:
  building-the-job-pipeline (upload size trap, guarded transitions, idempotent retries),
  building-the-web-backend (Prisma singleton, uniform errors, server-side RBAC),
  building-the-gpu-worker (stage runner, error taxonomy, VRAM sampling),
  building-dashboard-ui, rendering-splats-in-unity (runtime .ply loading is the week-1 risk),
  simulating-ballistics-and-spatter (substep sweep code, parabola test, sin-alpha ellipse),
  implementing-custody-and-encryption (canonical hash chain, streaming SHA-256, AES-256-GCM).
- Kept four project skills: running-reconstruction, debugging-frames-and-scale,
  changing-the-contract, writing-fyp-deliverables. Dropped session-logging and shipping-a-pr
  as pure AGENTS.md restatements.
- Vendored nine Superpowers process skills (MIT, obra/superpowers): brainstorming, TDD,
  systematic-debugging, verification-before-completion, writing/executing-plans,
  requesting/receiving-code-review, finishing-a-development-branch. License copied to
  `.claude/skills/SUPERPOWERS-LICENSE`.
- `SKILLS.md` at root indexes all of it; AGENTS.md points there.
- AGENTS.md: recorded the emergency self-merge exception (private repo, rule is discipline;
  self-merge only for genuinely urgent gates, announced in chat, noted on PR and here, with
  retroactive review).

**Handoff:** shipped as PR #1 (branch `repo/agent-skills`), awaiting review from Tayyab or
Faizan. Next steps unchanged: Track A first light on the Legion, proposal defect list before
4 Sep. Jira setup guidance given to the team; no tooling committed for it yet.

---

## 2026-08-22 · Wahaj · MacBook Air

**Goal:** stand the repo up to the handbook's layout.

- Handbook revised against the 22 Aug proposal draft: advisor, splatfacto, marker plus Cloud
  Anchors, pinned versions, and scale acknowledgement marked as done; remaining proposal
  defects (page breaks, uncited refs [9][10][11], missing timeline/risk/dataset sections,
  template question) moved into Section 00 and the D2 gate row.
- Scaffolded `gpu/`, `web/`, `docs/` alongside the existing `Unity/`.
- Committed the contract as `docs/API.md` with `docs/samples/metadata.example.json`.
- Added `docs/STACK.md` (pinned versions, installed column empty), `docs/FRAMES.md` (convert
  once, GPU side, left handed Y up metres), `docs/RESULTS.md` (empty measurement tables).
- Extended root .gitignore: captures, outputs, datasets, .ply/.splat/.mp4/.mov, web
  subfolder artifacts.
- Added `AGENTS.md` with the session log rule and repo health rules.
- Moved the handbook into `docs/`.
- Added the branch/PR/test workflow to `AGENTS.md`: no direct commits to main, cross review
  before merge, unit tests ship with the code, CI on PRs once tests exist. That commit is
  itself the last direct push to main.

**Handoff:** repo skeleton committed. Nothing runs yet anywhere. Next steps: Wahaj starts the
Track A first-light checklist on the Legion (handbook Section 05), Faizan scaffolds `web/`,
Tayyab creates the Unity project and gets a splat rendering on a real phone. Proposal defect
list is the other open front, due 4 Sep.

---
