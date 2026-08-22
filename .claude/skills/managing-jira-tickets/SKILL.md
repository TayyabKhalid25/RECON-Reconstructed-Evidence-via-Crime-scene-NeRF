---
name: managing-jira-tickets
description: Use when creating, updating, transitioning, commenting on, or searching Jira tickets in the FTW project, or when starting or finishing work that a ticket tracks.
---

# Managing Jira tickets

## Overview

The board (`reconfyp.atlassian.net`, project `FTW`, team-managed) mirrors reality. A ticket
column that lies about the state of the work is worse than no board. Access: the `atlassian`
MCP server from `.mcp.json` (OAuth once via `/mcp`), or REST with the `JIRA_*` values in the
untracked `.env`.

## Lifecycle, tied to the git workflow

| Moment | Board action |
|---|---|
| Work starts | Move to In Progress, assign the person actually driving |
| PR opens | Move to In Review; the branch name (`gpu/FTW-14-first-light`) and PR title carry the key so it links |
| PR merges | Move to Done |
| Work stalls | Leave In Progress, comment what it is blocked on |

**Done means merged and verified, never "the code is written".** Query the available
transitions per ticket instead of hardcoding transition ids; team-managed boards renumber them.

## Creating tickets

- **Search first** (`project=FTW AND text ~ "..."`). A duplicate ticket splits the discussion.
- Title states the deliverable; description carries observable done-when criteria ("phone
  loads http://legion:3000", not "works").
- Labels from the fixed set: `track-gpu` / `track-web` / `track-unity` / `infra` / `proposal`.
  Due dates only for real deadlines from the handbook, not decoration.
- Descriptions over REST are ADF (Atlassian Document Format), not markdown.
- Bulk-creating tickets happens only when a human asked for it.

## Etiquette

- **Never grab or reshape someone else's ticket.** A ticket with a teammate's name in the
  title belongs to them even while unassigned (they have not joined the space yet). Don't
  assign it to anyone else, don't rewrite their descriptions; add a comment instead.
- **Comment only substance:** a decision taken, a blocker, a measurement, a scope change.
  Column moves speak for themselves; "moving this to In Progress" as a comment is noise.
- **Scope grows, tickets split.** New requirements become a new linked ticket, not a silently
  expanding old one.
- **Never delete a ticket.** A dead ticket is closed with a comment saying why; deletion
  destroys the record.
- **Jira does not replace the session log.** The ticket tracks the task, the session log in
  `docs/sessions/` tracks the day. An update to one is not an update to the other.

## Red flags

- A ticket moved to Done with its PR still open
- A comment that restates a column move
- Editing a description on a ticket titled with someone else's name
- Creating a ticket without searching for an existing one first
