# 2026-08-22 · Wahaj · MacBook Air

**Goal:** stand up the Jira integration and seed the backlog.

- Jira space settled: site `antiwahaj.atlassian.net`, project RECON, key `REC` (team-managed).
  The Back_End Dev / Front_End Dev spaces are leftovers, to be trashed.
- Credentials: personal API token in the untracked `.env` (JIRA_* block, mirrored in
  `.env.example` via PR #2). The `.mcp.json` OAuth route from PR #1 remains the recommended
  path for Tayyab and Faizan.
- Seeded 19 tickets, REC-2 through REC-20, over REST: per-person Tailscale tasks (customized:
  Wahaj Legion+WSL2, Faizan dev-API exposure, Tayyab AR phone), the seven proposal-defect
  tasks due 4 Sep, blocking verifications (Cloud Anchors/ASA, device audit, storage decision),
  the three track first lights due 30 Aug, and tooling (GitHub app installs, Jira onboarding).
- REC-2, REC-3, REC-12, REC-16 assigned to Wahaj; everything for Tayyab and Faizan left
  unassigned with their name in the title until they accept invites.
- README gained a Jira section: board link, join steps, both agent-integration routes,
  branch/commit key conventions.

**Handoff:** PR #1 and #2 were merged to main earlier today (PR #1 self-merged by Wahaj,
bootstrap). This README change ships as its own PR. Open on the team: Tayyab and Faizan accept
Jira invites and OAuth MCP (REC-20), Tayyab's GitHub app installs (REC-19). Wahaj's next real
work is REC-16, Track A first light on the Legion.
