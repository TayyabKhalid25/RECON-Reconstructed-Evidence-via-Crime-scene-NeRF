# RECON-Reconstructed-Evidence-via-Crime-scene-NeRF
Basically Batman's Detective Vision from Arkham Origins, minus the cowl, the trauma, and the billion-dollar trust fund. Made by Tayyab, Wahaj and Faizan as part of our FYP at FAST.

Start here: `AGENTS.md` for how we work, `SKILLS.md` for the agent skills,
`docs/Forensic-NeRF-FYP-Handbook.md` for the full plan.

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
