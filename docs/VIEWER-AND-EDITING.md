# Viewing, editing and annotating scenes

The scope the anchoring work does *not* cover: looking at a scene when you are nowhere near the
room, carving it into objects, and putting points of interest on it that survive a plane flight
and a dead network. Most of this is FYP-2, but three decisions have to be made in FYP-1 because
the pipeline and the contract bake them in.

Read `docs/ANCHORING.md` first for the on-site half. Checked against live sources **2026-09-02**.

## The short version

- **"VR" here almost certainly means a 3D viewer, not a headset.** Say which one you mean in the
  report, because they are different amounts of work and only one of them needs hardware you
  may not own.
- **Do not build a splat editor.** Segment offline in an existing MIT-licensed tool, ship the
  parts, and build annotation in our own UI. For a forensic tool, *editing the evidence geometry
  is the thing you must not casually allow*.
- **ML segmentation is off the table on our hardware.** SAGA wants a 24 GB 3090; Gaussian
  Grouping peaks around 61 GB. We have 8 GB and 6 GB. Geometric selection is the route.
- **Offline POI editing and the hash chain can coexist, but only if the client never computes a
  chain hash.** The client keeps a journal of intents; the server orders and hashes them.

## Three things called "editing", and why the distinction is a custody requirement

Lumping these together is how a forensic tool quietly becomes inadmissible. They get different
permissions, different audit treatment, and different places in the pipeline.

| | What it is | When | Custody treatment |
|---|---|---|---|
| **Cleanup** | Cropping floaters, trimming the scene box, removing the ceiling to see in | Pre-ingest, before the asset is hashed and sealed | It **modifies evidence**. Allowed, but the crop must be recorded and the original retained. Never in-place on a sealed asset |
| **Segmentation** | Deriving "these Gaussians are the table" | Pre-ingest or a later derived pass | Additive. It produces *new* parts alongside the original, never mutates it. The original's sha256 stays valid |
| **Annotation** | POIs, evidence markers, notes, measurements, trajectories | Any time, by investigators, this is the actual product | Database rows in scene coordinates, inside the audit chain. **Never touches splat data** |

The rule that falls out: **annotation must be incapable of modifying geometry.** Not "we won't",
*can't* — no code path from the POI UI to the splat asset. That is a one sentence answer to
"how do we know the investigator didn't move the body", and it costs nothing to design in now
and a great deal to retrofit.

A corollary worth stating in the report: cleanup is legitimate and universal (every published
splat capture crops floaters), but "we cropped the scene" is a sentence that needs a log entry
behind it. `FTW-66`.

## Remote viewing, and what "VR" means

> **Terminology, settle it before the report.** The Gemini exchange used "VR" for *not being in
> the room*, which is really **a 3D viewer with an orbit or first-person camera**: mouse,
> keyboard, touch. Actual **VR** means a stereo headset over WebXR or Unity XR. The first is a
> week of work on a stack we already have. The second needs a headset none of us has listed in
> handbook Section 02. Write "remote 3D viewer" where you mean the first, and scope the second
> as a stretch item behind a hardware check.

Anchoring drops out entirely here. A splat is a self contained coordinate system; with no
physical room to align to there is nothing to anchor. `docs/ANCHORING.md` says the same thing
from the other direction. What you get for free by not needing AR: no marker, no cloud anchor,
no keyless auth, no network at view time once the asset is cached.

Three surfaces, in increasing cost:

1. **Web dashboard viewer.** Next.js is already the stack, and this is the one that makes the
   project demoable to anyone with a link. Highest value per hour of the three.
2. **Unity as a desktop viewer.** Nearly free: the AR scene minus the AR session, with an orbit
   camera. Physics, ballistics and colliders all keep working because they were always virtual.
   Worth having as the fallback if mobile splat rendering stays painful.
3. **Headset VR.** WebXR through the same web viewer, or Unity XR. Real work, needs a device,
   and adds nothing a panel is grading. Stretch item.

### Web viewer: engine options

| Option | Stack | Why it might win | Why it might not |
|---|---|---|---|
| **Spark** (`sparkjsdev/spark`, World Labs) | three.js + WebGL2 | Renders **multiple splat objects in one scene**, which is exactly what per-object parts need. Documented React usage, targets desktop, iOS, Android **and VR**, reads PLY, SPZ, SPLAT, KSPLAT, SOG. v2 adds LOD streaming | Newest of the three, and the LOD format (`.RAD`) is its own thing. Pin a version |
| **PlayCanvas** (engine, or `@playcanvas/react`) | PlayCanvas | Strongest splat tooling anywhere, WebGPU streaming, and **SuperSplat is built on it**, so viewer and editor share an engine. SOG is theirs | A second engine to learn next to three.js. The "2x FPS, 80 % less memory versus GaussianSplats3D" figure is one forum report, not a benchmark `verify` |
| **mkkellogg/GaussianSplats3D** | three.js | The most widely used three.js splat renderer, lots of prior art, KSplat ecosystem | Known Next.js integration friction (issue #247). Single splat object focus |

**Recommendation: Spark for the dashboard viewer.** It is the only one of the three whose
documented feature list already contains the two things RECON specifically needs — several
splat objects in one scene, and a VR path — on the stack we are already committed to.
`FTW-59` measures it rather than trusting the table.

### Formats, and where they fit our pipeline

Track A currently exports `.ply` (converted to Unity convention, per `docs/FRAMES.md`) and
`tools/decimate_splats.py` truncates SH for size. A 44 MB PLY is fine over Tailscale and
unacceptable over a hotel network to a browser.

| Format | Size vs PLY | Use |
|---|---|---|
| **PLY** | 1x | **Archival and the hashed original.** Never replace this: it is what `sha256` in `metadata.json` covers |
| **SOG** (PlayCanvas, open sourced) | ~15-20x smaller | Web delivery. Morton ordering plus WebP textures plus codebook quantisation |
| **SPZ** (Niantic, open source) | ~10x smaller | Web delivery, broadest cross-viewer support |
| **Streamed SOG / `.RAD`** | — | Only if a scene gets big enough to need progressive loading |

All the small ones are **lossy**, which is why the PLY stays authoritative. The web viewer
showing a quantised scene is fine for inspection; every *measurement* must come from the
sealed PLY, and the UI should say which one it is looking at. `FTW-60` adds a web format as an
extra asset next to the PLY, not instead of it.

> Our own SH truncation already gets 3.65x for free with bit-identical geometry (measured
> 2026-09-02, `docs/RESULTS.md`), so it composes with a delivery format rather than competing
> with one. Do SH truncation first; it is the lever that cannot hurt accuracy.

## Object subsections: how you actually get "the table" as a thing

A raw splat is one undifferentiated cloud. To click a table, hide it, or hang a POI off it, its
Gaussians have to be identifiable as a group. There are two families, and the hardware picks
for us.

### The ML family, and why it is out

Methods like **SAGA** (Segment Any 3D Gaussians), **Gaussian Grouping**, **Feature-3DGS** and
**LangSplat** attach a learned per-Gaussian identity or affinity feature, supervised by SAM or
a 2D tracker, so you can prompt in 2D and get a 3D mask. They are genuinely the state of the
art, and a 2026 TPAMI survey covers the space.

They also do not fit. SAGA's paper reports all training and inference on a **single RTX 3090,
24 GB**. A published comparison puts **Gaussian Grouping at ~61 GB peak VRAM**. RECON's
machines are an 8 GB 4060 and a 6 GB 4050, and we are already fighting VRAM on plain splatfacto
training. Each method also means retraining every scene with a modified pipeline, on top of the
COLMAP and splatfacto runs we already have to fit into a semester.

Say this in the report as a scoped-out alternative with the reason. "We evaluated learned
per-Gaussian segmentation and excluded it on a stated VRAM bound" is a better sentence than
silence, and it is true.

### The geometric family, which is what we ship

**SuperSplat** (`playcanvas/supersplat`, **MIT**, TypeScript on the PlayCanvas engine, runs in
the browser) already does the whole job, and one of its tools is named for exactly our use case:

- 2D selection: Picker, Lasso, Polygon, Brush, Flood, Eyedropper, with Set / Add / Remove /
  Intersect modifiers.
- 3D selection: **Sphere** and **Box**, with numeric position and size.
- **`Separate`: creates a new splat from the selection and removes it from the original.**
  `Duplicate` keeps both. The Scene Manager holds several splat objects at once.
- Deletion is non-destructive during the session, with undo and a full reset.

So the FYP-2 path is: open the sealed PLY in SuperSplat, box or lasso the table, `Separate`,
export the parts, and register them as **scene parts** against the scene. Cost: an afternoon
per scene and zero GPU. No training, no third-party service, no new dependency in our own code
if we use it as a *tool* rather than a fork.

**Do not fork SuperSplat into the dashboard.** The temptation is obvious and the cost is a
maintained fork of somebody else's editor for a feature used a handful of times per case. Use
it offline, ingest its output. If interactive in-browser segmentation later becomes a real
requirement, revisit — the MIT licence means the option stays open.

What this needs from the contract: a **parts manifest** — each part with an id, a human label,
its own file and sha256, and a parent scene. `FTW-63`, and it is a contract change because
`metadata.json` and the Scene model both grow. Parts are additive: the original PLY and its
hash are untouched, which is the whole reason segmentation is safe and cleanup is not.

## POIs, local edits, and syncing without breaking the hash chain

This is the FYP-2 feature the whole thing is for, and it has one genuinely hard corner.

### The data model

A POI is **not** a splat and **not** an anchor. It is a row: id, scene id, optional part id,
position and rotation in **scene coordinates** (metres once `unitScale` is applied), a type,
a label, notes, author, timestamps. Trajectories and spatter are the same shape with more
fields. Because they live in scene space:

- One cloud anchor still places all of them at once. Per `docs/ANCHORING.md`, never anchor a
  POI individually.
- They are verifiable: a POI is a number in a frame we control, not opaque state on a server we
  do not.
- They survive re-export of the splat. A better reconstruction of the same room, aligned to the
  same marker origin, keeps every POI where it was.

### The offline problem, stated precisely

The custody design is an **append-only chain where each row hashes its own contents plus the
previous row's hash**. That is inherently *sequential* and *server-ordered*. A phone editing
POIs on a plane cannot extend that chain: it does not know what the previous row will be by the
time it reconnects, and two offline devices would both claim the same position.

If you let clients compute chain hashes to "support offline", you get a chain that cannot be
verified, which is worse than having no chain — it is a tamper-evidence claim that does not
hold.

### The design that works

**Two layers, and the client never touches the chain.**

1. **Local draft journal.** The device stores an ordered list of *intents* — `createPoi`,
   `movePoi`, `editPoi`, `deletePoi` — each with a client-generated UUID as the POI id, a
   client timestamp, and a monotonic local sequence number. IndexedDB on web,
   `persistentDataPath` (JSON or SQLite) in Unity. Everything unsynced renders as visibly
   **draft** and cannot be exported into a report.
2. **Server replay on sync.** The client POSTs the journal. The server validates, applies the
   intents **in receipt order**, stamps *its own* clock, and appends the audit rows itself,
   computing `prevHash` and `rowHash` server side as it already does. It returns authoritative
   state. The client's timestamp is kept as a *claimed* field, clearly labelled, never as the
   custody time.

Client-generated UUIDs are what make this work without a "temporary id" dance: the POI has its
final identity from the moment it is created offline, so a later `movePoi` on the same device
refers to something the server will recognise.

**Conflicts.** POI work is overwhelmingly additive, so do not reach for CRDTs. Give each POI a
version integer, have updates send the version they read, and return **409** on a stale write
so the UI can show both values and let a human choose. Deletes are **tombstones**, never row
removal, because "this POI was removed, by whom, when" is exactly what custody is for.

**Idempotency.** A sync that times out after the server committed must not double-apply on
retry. Intents carry a UUID; the server records applied intent ids and ignores repeats.

**And the honest limitation:** offline mode means a window where the tamper-evident log does
not yet cover the edits. That is not a flaw to hide, it is the reason drafts are labelled and
non-exportable until synced. Handbook Section 18 already lists "offline mode with a cached
scene" as a T3 item; this is what makes it defensible rather than a liability.

## Integration plan

Only three of these belong in FYP-1, and they are there because the pipeline and the contract
would otherwise have to change later.

### In FYP-1, because deferring costs more than doing

- **`FTW-59` viewer engine spike.** Decide Spark versus PlayCanvas versus GaussianSplats3D on
  our real 178 k splat scene, in the actual Next.js dashboard, measured on a phone browser and
  a laptop. This is the same shape as the Unity renderer question and deserves the same
  treatment: measure, then commit, then record in `docs/STACK.md`.
- **`FTW-60` web delivery format.** Export SOG or SPZ as an *additional* asset. Cheap now,
  and it decides what the viewer loads.
- **`FTW-63` parts manifest in the contract.** Even with zero segmented scenes, agreeing the
  shape now means FYP-2 does not reopen `docs/API.md` and `metadata.json`.

### FYP-2, and this is the Chapter 10 plan

`FTW-61` remote 3D viewer page. `FTW-64` POI model and endpoints. `FTW-65` offline journal and
sync. `FTW-66` cleanup custody rules. `FTW-62` headset VR, behind a hardware check. `FTW-67`
turns this section into the mandatory FYP-2 work plan that D14 (11 Nov) requires in Chapter 10,
which is a graded deliverable and currently has nothing written against it.

### Ticket index

| Ticket | Track | When | What |
|---|---|---|---|
| FTW-59 | web | FYP-1 | Viewer engine spike, Spark versus PlayCanvas versus GaussianSplats3D |
| FTW-60 | gpu | FYP-1 | SOG or SPZ delivery asset next to the archival PLY |
| FTW-63 | web + gpu | FYP-1 | Scene parts manifest, contract change |
| FTW-61 | web | FYP-2 | Remote 3D viewer page |
| FTW-64 | web | FYP-2 | POI model and endpoints |
| FTW-65 | web + unity | FYP-2 | Offline draft journal and sync |
| FTW-66 | web | FYP-2 | Cleanup custody rules |
| FTW-62 | web | stretch | Headset VR, blocked on owning a headset |
| FTW-67 | report | 11 Nov | Chapter 10's mandatory FYP-2 work plan |

## Open questions

| Question | Who decides | Blocks |
|---|---|---|
| Does anyone have a VR headset, or is "VR" the remote 3D viewer? | All three | `FTW-62`, and the wording in the report |
| SOG or SPZ for delivery? Follows the viewer choice | Wahaj, after `FTW-59` | `FTW-60` |
| Are segmented parts a graded FYP-1 claim, or purely FYP-2 scope? | All three | how much of `FTW-63` ships now |

## Sign off

- [ ] All three agree "remote 3D viewer" and "VR" are different things, and which one is promised
- [ ] Agreed that annotation has no code path to splat geometry
- [ ] Parts manifest shape agreed before it enters `docs/API.md`
- [ ] Tayyab initials: ____
- [ ] Wahaj initials: ____
- [ ] Faizan initials: ____
