# 2026-08-22 · Wahaj · MacBook Air

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
