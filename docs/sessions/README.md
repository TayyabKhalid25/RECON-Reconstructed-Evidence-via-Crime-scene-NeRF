# Session logs

One file per working session, so no file grows unbounded and every log says whose it is.

## Naming

```
YYYY-MM-DD-<name>-<slug>.md        e.g. 2026-08-24-faizan-job-endpoints.md
```

- Date first so files sort chronologically.
- `<name>` is tayyab, wahaj, or faizan. No anonymous logs, ever.
- `<slug>` is two or three words on what the session is about. A second session by the same
  person on the same day gets a different slug, or `-2` if it is genuinely the same topic.

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

- Created at session start, updated during, closed with the handoff before the session ends.
- The handoff is the point: the next session (any of us, or an agent) starts cold in under a
  minute from it.
- Do not edit someone else's log except to fix a broken link.
