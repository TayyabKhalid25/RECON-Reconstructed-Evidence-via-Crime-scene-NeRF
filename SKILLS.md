# Agent skills for this repo

Project skills live in `.claude/skills/`. Claude Code picks them up automatically for anyone
working in this repo; other agent harnesses can read the same files by path. AGENTS.md holds
the always-on rules; skills hold the how-to for specific work. When a task matches a row below,
the agent reads and follows that skill before improvising.

## Build skills (the meat)

| Skill | Fires when |
|---|---|
| [building-the-job-pipeline](.claude/skills/building-the-job-pipeline/SKILL.md) | Upload, enqueue, BullMQ, worker claiming, status transitions, stuck or duplicated jobs. Carries the video-upload size-limit trap and the guarded-transition pattern |
| [building-the-web-backend](.claude/skills/building-the-web-backend/SKILL.md) | Route handlers, Prisma, JWT and RBAC, validation. Prisma singleton, uniform error shape, per-route authorization |
| [building-the-gpu-worker](.claude/skills/building-the-gpu-worker/SKILL.md) | The Python worker around COLMAP and splatfacto: stage runner with timeouts, error taxonomy, VRAM sampling, idempotent retries |
| [building-dashboard-ui](.claude/skills/building-dashboard-ui/SKILL.md) | Case lists, upload flow, live job status. Gentle polling, four designed states per view, real upload progress |
| [rendering-splats-in-unity](.claude/skills/rendering-splats-in-unity/SKILL.md) | UnitySplats integration, runtime .ply loading (the week-1 risk), marker alignment tree, mobile FPS knobs |
| [simulating-ballistics-and-spatter](.claude/skills/simulating-ballistics-and-spatter/SKILL.md) | Substepped swept-raycast ballistics with the free parabola test, BPA sin-alpha ellipse spatter code |
| [implementing-custody-and-encryption](.claude/skills/implementing-custody-and-encryption/SKILL.md) | Hash-chained audit log (canonical serialization, serialized appends), streaming SHA-256, AES-256-GCM at rest |

## Project skills

| Skill | Fires when |
|---|---|
| [running-reconstruction](.claude/skills/running-reconstruction/SKILL.md) | Capture, COLMAP, splatfacto, OOM, melted scenes, training suddenly slow |
| [debugging-frames-and-scale](.claude/skills/debugging-frames-and-scale/SKILL.md) | Mirrored, sideways, wrong-size, or misaligned scenes |
| [changing-the-contract](.claude/skills/changing-the-contract/SKILL.md) | Any edit to docs/API.md, the state machine, the data model, or metadata.json |
| [writing-fyp-deliverables](.claude/skills/writing-fyp-deliverables/SKILL.md) | Proposal, report chapters, TC-XX test cases, Turnitin, presentations |
| [managing-jira-tickets](.claude/skills/managing-jira-tickets/SKILL.md) | Creating, updating, transitioning, or searching REC tickets; starting or finishing tracked work |

## Process skills (vendored from Superpowers)

General engineering discipline, vendored from [obra/superpowers](https://github.com/obra/superpowers)
(MIT, license at `.claude/skills/SUPERPOWERS-LICENSE`) so nobody needs to install anything.

| Skill | Fires when |
|---|---|
| [brainstorming](.claude/skills/brainstorming/SKILL.md) | Before building any new feature or component, to pin down intent and design first |
| [writing-plans](.claude/skills/writing-plans/SKILL.md) | A spec or multi-step task exists and needs a plan before code |
| [executing-plans](.claude/skills/executing-plans/SKILL.md) | Executing a written plan with checkpoints |
| [test-driven-development](.claude/skills/test-driven-development/SKILL.md) | Implementing any feature or bugfix (test first, watch it fail) |
| [systematic-debugging](.claude/skills/systematic-debugging/SKILL.md) | Any bug or unexpected behaviour, before proposing fixes |
| [verification-before-completion](.claude/skills/verification-before-completion/SKILL.md) | About to claim something works, is fixed, or passes |
| [requesting-code-review](.claude/skills/requesting-code-review/SKILL.md) | Work finished, PR about to go up |
| [receiving-code-review](.claude/skills/receiving-code-review/SKILL.md) | Review feedback arrives (verify before implementing, no blind agreement) |
| [finishing-a-development-branch](.claude/skills/finishing-a-development-branch/SKILL.md) | Implementation done, deciding how the branch lands |

## Adding a skill

New folder under `.claude/skills/<verb-first-name>/SKILL.md` with `name` and `description`
frontmatter, where the description says only *when* to use it (start with "Use when...", never
summarize the procedure, or agents follow the summary and skip the body). Keep it to a page,
prefer a code pattern and a symptom table over prose, flag unverified version-sensitive facts
with `verify`, and add a row here. Skills land via PR like everything else.

Build and project skills are distilled from the handbook; they have not been pressure-tested
against agent baselines. If one misfires in practice, fix the wording in the same PR as the
work it misfired on.
