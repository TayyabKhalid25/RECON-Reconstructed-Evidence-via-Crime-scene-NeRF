# 2026-08-22 · Wahaj · MacBook Air

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
- Restructured session logs: one file per session in `docs/sessions/`, named
  `YYYY-MM-DD-name-slug.md`, format in `docs/sessions/README.md`. The old single
  `docs/SESSION_LOG.md` is gone, its two entries split into dated files.
- Added devops plumbing (same PR): PR template with the AGENTS.md checklist, task and bug
  issue templates, CI workflow (hygiene job blocks binaries, oversized files, and committed
  .env from day one; web and gpu jobs self-activate when `web/package.json` and `gpu/*.py`
  appear), `.mcp.json` wiring the Atlassian remote MCP server for the new Jira space (each
  person OAuths on first use), and `docs/NETWORK.md` with the Tailscale setup.

**Handoff:** shipped as PR #1 (branch `repo/agent-skills`), awaiting review from Tayyab or
Faizan. Next steps unchanged: Track A first light on the Legion, proposal defect list before
4 Sep. Jira space exists; Tayyab still has to install "GitHub for Jira" and run
/install-github-app for Claude PR reviews (repo owner steps). Tailscale installs are manual
per machine, steps in docs/NETWORK.md.
