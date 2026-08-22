---
name: writing-fyp-deliverables
description: Use when working on the proposal, DII or DIII report chapters, test cases, presentations, Turnitin submission, or any document that gets graded by FAST.
---

# Writing FYP deliverables

## Overview

RECON is a **Development** project at FAST. That classification decides the document structure, and two hard gates (paperwork and Turnitin) can block a presentation regardless of how good the software is. Full detail: handbook Sections 13 and 16.

## The rules that differ from a normal report

- **No results chapter exists.** Every measurement lands in Chapter 7 as a numbered test case with a stated pass criterion:

  ```
  TC-07  Reconstruction fidelity
    Requirement : Objective O1, PSNR above 28 dB
    Method      : 3 physical scenes, held out views, 3 repeats
    Expected    : mean PSNR > 28 dB
    Observed    : ___
    Verdict     : pass / fail, with discussion
  ```

  Build the empty table early so results drop in as they arrive.
- **Chapter 6 comes from `docs/API.md`** (endpoints, state machine, data model, frame convention are already a low-level design). Chapter 4 is Chapter 6 read backwards: every endpoint implies a functional requirement, every objective threshold a non-functional one.
- **Claims match objectives.** The objectives say 28 dB PSNR and 2 cm. Never write "sub-centimetre" or any number the objectives do not support. Version-sensitive facts get flagged for verification, not asserted.

## The two gates

| Gate | Rule |
|---|---|
| Turnitin | Plagiarism and AI reports each 20 percent or less, obtained by emailing `librarylhr@nu.edu.pk`. Turnaround is outside our control: send a full week early |
| Paperwork | Missing compliance form or signature means no permission to present at all. Assemble each submission a week early; signatures need the supervisor physically present |

## The prose rule

The graded prose is written by the team. An agent's job is structure, outlines, defect lists, and revision feedback on the team's own drafts. Generating finished paragraphs for pasting into a graded document risks the 20 percent AI gate and is off limits, even when asked casually. Point at this rule instead of complying.
