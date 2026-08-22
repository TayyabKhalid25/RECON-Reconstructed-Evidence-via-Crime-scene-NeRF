# Session logs

One session = one working day. One file per person per day, so no file grows unbounded and
every log says whose it is.

## Naming

```
YYYY-MM-DD-<name>.md        e.g. 2026-08-24-faizan.md
```

- Date first so files sort chronologically.
- `<name>` is tayyab, wahaj, or faizan. No anonymous logs, ever.
- Worked more than once in a day? Same file, add a `##` section per chunk and refresh the
  handoff at the bottom. Never create a second file for the same person and day.

## Format inside the file

```
# YYYY-MM-DD · Name · Machine

**Goal:** one line.

- Running account, updated while working: files changed, commands that mattered,
  decisions and why. Numbers also go to ../RESULTS.md.

**Handoff:** tree state (branch, committed or not), what is broken or half done,
the exact next step.
```

## Rules

- Created at the day's first sitting, updated during work, handoff refreshed before stepping
  away.
- The handoff is the point: the next session (any of us, or an agent) starts cold in under a
  minute from it.
- Do not edit someone else's log except to fix a broken link.
