---
name: shipping-a-pr
description: Use when work is ready to land on main, when creating a branch for new work, or when tempted to push to main directly "because it's small".
---

# Shipping a PR

## Overview

Nothing lands on main directly. Main is what gets demoed at every graded gate, so it stays green and demoable. The full rules live in AGENTS.md; this is the procedure.

## The procedure

1. **Branch** off fresh main: `<track>/<short-description>` (`gpu/colmap-worker`, `web/job-endpoints`, `unity/marker-align`). Repo-wide changes use `docs/` or `repo/` as the prefix.
2. **Build with tests.** Behaviour added means tests added, in the same PR (targets per track are listed in AGENTS.md). Run them before opening the PR and paste real output, not "tests pass".
3. **Rebase on main** before opening and again before merge.
4. **Open the PR** with a description that says what changed and why, and names anything the other two must know (contract changes especially). Update the session log.
5. **Someone else reviews and merges.** Never self-merge. A PR unreviewed for two days gets raised in the group chat, not merged by its author.

## Rationalization table

| Excuse | Reality |
|---|---|
| "It's a one-line fix" | One-line fixes break demos too. The PR takes five minutes |
| "Nobody's around to review" | Two days of patience, then chase in chat. Still no self-merge |
| "It's just docs" | Docs PRs get the same flow. The handbook and contract are load bearing |
| "CI isn't set up yet, so skipping tests is fine" | Tests run locally until CI exists. Paste the output |

## Red flags

- `git push origin main` typed at all
- A PR opened by and merged by the same person
- "Tests pass" claimed with no output shown
