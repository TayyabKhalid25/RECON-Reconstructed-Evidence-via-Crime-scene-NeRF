---
name: session-logging
description: Use when starting, resuming, or ending any working session in this repo, or when about to end a turn after changing files without having touched the session log.
---

# Session logging

## Overview

`docs/SESSION_LOG.md` is the team's recovery path. If a session dies mid-work, the log is how the next person (or agent) starts cold in under a minute. It is written while working, not reconstructed at the end.

## The procedure

1. **On session start:** append a new entry at the top: date, driver, machine, goal.
2. **After each meaningful step:** update the entry. Files changed, commands that mattered, decisions taken and why. Numbers (VRAM, minutes, PSNR, FPS) also go to `docs/RESULTS.md`.
3. **Before ending:** close the entry with tree state (committed or not, branch name), what is broken or half done, and the exact next step.

## Quality bar

| Bad | Good |
|---|---|
| "worked on gpu stuff" | "ns-train splatfacto OOM'd at 1080p on the Legion, retried at 720p, peak 6.9 GB, PSNR 26.1, preset saved to gpu/configs/legion-720.yaml" |
| Written at session end from memory | Updated as it happened |
| No next step | "Next: raise densification threshold, retry at 1080p" |

## Red flags

- Ending a turn that changed files with no log update
- "I'll write the log after this one more thing"
- An entry with no handoff state

All of these mean: update the log now, then continue.
