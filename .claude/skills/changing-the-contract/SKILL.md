---
name: changing-the-contract
description: Use when editing docs/API.md, the job state machine, endpoint shapes, the Prisma data model, or any field in metadata.json, including "just adding one field".
---

# Changing the contract

## Overview

`docs/API.md` is what lets three people in three cities work in parallel. A silent contract change turns the integration window into a debugging window. Changes are cheap; unannounced changes are not.

## The procedure

1. **Say it before you build it.** The change goes to all three team members (group chat) before the PR merges. "All three know" is a merge requirement, not a courtesy.
2. **Change every artifact in the same PR:** `docs/API.md`, `docs/samples/metadata.example.json` if metadata changed, the Prisma schema if the model changed, and every consumer you can reach (worker parsing, Unity assertions).
3. **New metadata fields get a reason.** Every field in `metadata.json` exists because something breaks without it. A field nobody consumes is deleted, not kept "for later".
4. **State machine changes are near-frozen.** Five states, one direction. Adding a state or transition needs a failure story that the existing `FAILED`-with-error-text cannot represent.
5. **Log it.** The change and its reason go in the session log entry.

## Red flags

- "It's just one optional field, no need to tell anyone"
- Sample json updated but the Unity assert not checked
- A consumer discovers the change by crashing

All of these mean: stop, notify, sweep the consumers, then merge.
