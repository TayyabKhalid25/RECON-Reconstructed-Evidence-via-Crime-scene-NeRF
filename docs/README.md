# The docs, and when to read which

Nine documents is past the point where you can find things by guessing. This index says what
each one owns and when you need it, so nobody re-derives a decision that is already written
down.

**If you are new to the project, read in this order:** the handbook, then `API.md`, then
`FRAMES.md`. That is enough to work on any track without breaking someone else's.

| Doc | Owns | Read it when |
|---|---|---|
| [Forensic-NeRF-FYP-Handbook.md](Forensic-NeRF-FYP-Handbook.md) | The plan. Single source of truth for scope, deadlines, deliverables, risks | Before anything substantial. Start here |
| [API.md](API.md) | **The contract.** Job state machine, data model, endpoints, `metadata.json` | Before touching anything that crosses a track boundary. Changing it needs all three of us to know first |
| [FRAMES.md](FRAMES.md) | The coordinate convention and `unitScale`. Convert once, GPU side, before export | Any time a scene looks mirrored, rotated, or the wrong size. Never add a flip on the client |
| [ANCHORING.md](ANCHORING.md) | Marker alignment, ARCore Cloud Anchors, where anchors are and are not needed | Before any AR alignment or multi-device work. Closes handbook Decision 1 |
| [VIEWER-AND-EDITING.md](VIEWER-AND-EDITING.md) | Remote 3D viewing, splat editing, per-object parts, POIs and offline sync | Before building the web viewer, segmenting a scene, or anything that annotates |
| [CAPTURE.md](CAPTURE.md) | How to shoot a scene, and the VRAM-safe preset | Before recording a capture |
| [STACK.md](STACK.md) | Exact installed versions, recorded the day they were installed | When adding a dependency, or when something works on one machine only |
| [RESULTS.md](RESULTS.md) | Every measured number, with date and machine | The day you measure something. Nothing goes in a report that is not here first |
| [NETWORK.md](NETWORK.md) | Tailscale, hostnames, how the three machines reach each other | When a worker cannot reach the API |
| [sessions/](sessions/README.md) | One log per person per day | Every day. See `sessions/README.md` for the format |

Also in here: `results/` holds the raw JSON and CSV behind `RESULTS.md`, and `samples/` holds
the machine-readable `metadata.example.json` that the contract points at.

## Decisions that are settled, so you do not have to re-argue them

Each of these is written up in full in the doc named, with the evidence and the date it was
checked. If you think one is wrong, say so on the ticket rather than working around it.

- **Convert coordinates exactly once, on the GPU side.** Two conversions cancel out and cost a
  day to find. `FRAMES.md`
- **A `unitScale` of 0.0 means the scene is not metric.** The client refuses it loudly rather
  than rendering something that looks plausible and measures wrong. `FRAMES.md`
- **The marker aligns, the cloud anchor persists.** Cloud Anchors never give scale, so the
  printed 170 mm marker stays load bearing. The 2 cm accuracy objective belongs to the marker
  path, not the anchor path. `ANCHORING.md`
- **One cloud anchor per scene, at the marker origin.** Never one per POI or per object part.
  `ANCHORING.md`
- **Keyless authorization is mandatory for anchors**, not preferred: an API key caps anchor
  lifetime at 24 hours. `ANCHORING.md`
- **Nothing about remote viewing needs an anchor at all.** The web viewer and 3D navigation
  have no physical room to align to. `ANCHORING.md`, `VIEWER-AND-EDITING.md`
- **"Remote 3D viewer" and "VR" are different things.** One is an orbit camera on the stack we
  already have; the other is a stereo headset nobody on the team owns. Do not let a document
  promise the second while we ship the first. `VIEWER-AND-EDITING.md`
- **Cleanup, segmentation and annotation are three different operations** with three different
  custody treatments, and they must not share a code path. Annotation must be *incapable* of
  modifying splat geometry — enforced by a test, not a comment. `VIEWER-AND-EDITING.md`
- **The archival `.ply` stays authoritative.** Compressed delivery formats are lossy and ship
  alongside it; measurements come from the sealed original. `VIEWER-AND-EDITING.md`
- **A client never computes a custody chain hash.** Offline edits are a journal of intents; the
  server orders them and hashes them. `VIEWER-AND-EDITING.md`
- **Learned per-Gaussian segmentation is excluded on a stated VRAM bound**, not on preference.
  `VIEWER-AND-EDITING.md`

## Where the other rules live

Repo process — branches, PRs, reviews, the session log, secrets — is in
[AGENTS.md](../AGENTS.md), not here. Situation-specific playbooks are skills, indexed in
[SKILLS.md](../SKILLS.md).
