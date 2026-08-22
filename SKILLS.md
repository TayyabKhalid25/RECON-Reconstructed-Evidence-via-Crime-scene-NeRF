# Agent skills for this repo

Project skills live in `.claude/skills/`. Claude Code picks them up automatically for anyone
working in this repo; other agent harnesses can read the same files by path. Each skill is a
short procedure with checkpoints, written so an agent (or a tired human) does the right thing
without re-reading the 45-page handbook. AGENTS.md holds the always-on rules; skills hold the
step-by-step for specific situations.

| Skill | Fires when | One line |
|---|---|---|
| [session-logging](.claude/skills/session-logging/SKILL.md) | Any session starts, resumes, or ends | Keep `docs/SESSION_LOG.md` updated while working, close every entry with a handoff |
| [running-reconstruction](.claude/skills/running-reconstruction/SKILL.md) | Capture, COLMAP, splatfacto, OOM, melted scenes, training suddenly slow | The capture-to-.ply run with the checkpoints that stop you training on bad inputs |
| [changing-the-contract](.claude/skills/changing-the-contract/SKILL.md) | Any edit to `docs/API.md`, the state machine, the data model, or metadata.json | All three know before merge, every consumer updated in the same PR |
| [debugging-frames-and-scale](.claude/skills/debugging-frames-and-scale/SKILL.md) | Mirrored, sideways, wrong-size, or misaligned scenes | Symptom table for coordinate and scale bugs, one conversion rule |
| [shipping-a-pr](.claude/skills/shipping-a-pr/SKILL.md) | Work is ready to land, or a direct push to main is tempting | Branch, test with output shown, rebase, cross review, never self-merge |
| [writing-fyp-deliverables](.claude/skills/writing-fyp-deliverables/SKILL.md) | Proposal, report chapters, test cases, Turnitin, presentations | Development-project format, TC-XX test cases, the two blocking gates, the prose rule |

## Adding a skill

New skill = new folder under `.claude/skills/<verb-first-name>/SKILL.md` with `name` and
`description` frontmatter, where the description says only *when* to use it (start with "Use
when...", never summarize the procedure, or agents follow the summary and skip the skill).
Keep it under a page, prefer a symptom or rationalization table over prose, and add a row
here. Skills land via PR like everything else.

These skills have not been pressure-tested against subagent baselines yet; if one misfires in
practice, fix the wording in the same PR as the work it misfired on.
