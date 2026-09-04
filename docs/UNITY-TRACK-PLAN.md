# Track C execution plan — 2 Sep to 11 Dec 2026

The handbook (`Forensic-NeRF-FYP-Handbook.md`) is the source of truth for *what* and *by when*.
This is the Unity track's plan for *how*, written on 2 Sep 2026 by Faizan (Track C second, Tayyab
leads) so the other two can see what is being built, in what order, and why. Update it when a
decision changes; do not let it drift from the handbook.

## Where the project stood on 2 Sep

- **Web (main):** first light merged. Every Unity-facing endpoint exists: `POST /api/auth/login`
  → `{ token, user }`; `GET /api/scenes?status=READY` → `{ scenes }`; `GET /api/scenes/:id` →
  `{ scene: { id, name, unitScale, job, assets, assetUrl } }`; `GET /api/scenes/:id/asset` streams
  the `.ply` with an `X-Asset-SHA256` header. Auth is `Authorization: Bearer <jwt>` (12 h). Errors
  are always `{ error: { code, message, details? } }`. Anchor endpoints are on PR #18 with a
  **decomposed** transform (position, unit quaternion, scale, `frame`), never a 4x4 matrix. No DB
  migration had run yet.
- **GPU:** `gpu/run_scene.py` exports `splat_unity.ply` (Unity frame, **scene units**) plus
  `metadata.json` (`unitScale` ≈ 0.31–0.37, `handedness: left`, `upAxis: y`). Scene 1 is 178,333
  splats / 43 MB; a degree-0 SH export is 12 MB with identical geometry (PR #15).
  `tools/splat_to_mesh.py` (PR #20) produces a collider mesh `.ply` in scene units.
- **Unity (main):** stock Mobile AR Template, Unity 6000.3.22f1, URP 17.3.0, AR Foundation 6.5.0.
  No RECON code. Tayyab's PR #14 found that `aras-p/UnityGaussianSplatting` cannot compile on
  Android GPUs (wave intrinsics in `SplatUtilities.compute`), so nothing renders on a phone.
- **The empty cell everyone is blocked on:** `docs/RESULTS.md` "Mobile rendering" has no FPS
  number (FTW-30, due 6 Sep). It sets the splat budget for the whole pipeline.

## Decisions taken 2 Sep (Track C, to be confirmed with Tayyab)

1. **Renderer:** try `arloopa/UnitySplats` first (MIT, Unity 6, URP, CPU-sort fallback, runtime
   `.ply` loader with `SourceCoordinates.RUF` = "Unity, no conversion"), `wuyize25/gsplat-unity`
   second, FTW-44 (hand-patch aras-p) last — the sequence `MOBILE-SPLAT-OPTIONS.md` suggests.
   Final call by **9 Oct** per the risk register.
2. **Android only for FYP-1.** iOS is documented as designed, not built; FTW-57 closes with that
   reason; no MacBook build day.
3. **Faizan takes the FTW-30 measurement** on his phone in parallel with Tayyab fixing PR #14.
4. **Architecture:** all RECON code in `Assets/Recon/` behind its own assemblies; the renderer
   package behind one interface; pure C# cores separated from MonoBehaviours so the handbook's
   Track C unit tests run without a device.

## Architecture

```
Unity/Recon/Assets/Recon/
  Recon.Runtime.asmdef      references ARFoundation, ARSubsystems, XR.CoreUtils, InputSystem, Mathematics
  Contract/Core/            DTOs mirroring docs/API.md and metadata.json. Pure C#.
  Api/                      ReconApiClient (UnityWebRequest), ApiException from the error envelope,
                            ISceneSource: ApiSceneSource (live) and FileSceneSource (folder on disk)
  Scenes/                   Core/SceneMetadataValidator (handedness==left, upAxis==y, unitScale>0),
                            Sha256File, SceneLoader (fetch → download → verify sha → validate →
                            renderer.Load → parent under SceneRoot → localScale = unitScale)
  Rendering/                ISplatRenderer { LoadAsync(plyPath, parent), Unload(), SplatCount, Bounds }
                            Adapters/UnitySplats  (own asmdef, compiled only when the package is present)
                            Adapters/GsplatUnity, Adapters/Arasp (same pattern, when needed)
  Alignment/                SceneRoot (the one origin), AlignmentSource {None, Marker, CloudAnchor,
                            Manual}, MarkerAligner (ARTrackedImageManager, physicalSize 0.170),
                            AlignmentStatus; later CloudAnchorAligner (FTW-52/53), ManualPlacement
  Colliders/                Core/PlyMeshReader, ColliderMeshLoader (MeshCollider under SceneRoot),
                            CollisionLayers (ArPlanes baseline vs SplatMesh)
  Ballistics/               Core/TrajectoryIntegrator (semi-implicit Euler, quadratic drag, swept
                            raycast every substep through an IRaycaster seam), ShotController
  Spatter/                  Core/SpatterMath (minor/major = sin α), SpatterEmitter, EllipseDecal
  UI/                       ScenePicker, AlignmentIndicator, FpsOverlay, ShotParametersPanel
  Diagnostics/              FpsCounter, PerfLog (per-second samples → JSON in persistentDataPath;
                            RESULTS.md rows come from the file, not from eyeballing)
  Config/                   ReconSettings ScriptableObject (apiBaseUrl, marker size, layers)
  Scenes/ReconAR.unity      the app scene (Faizan). SampleScene.unity stays Tayyab's.
  Editor/                   BuildScript (batch-mode Android build), SceneBootstrap (creates ReconAR),
                            RendererFeatureUtil
  Tests/EditMode/           NUnit; Core/ tests also run under `dotnet test Unity/Recon.Core.Tests`
```

Rules baked in (`docs/FRAMES.md`, the two Unity skills, `docs/ANCHORING.md`):

- **Never flip or scale the renderer to fix appearance.** `SceneRoot.localScale = unitScale` is
  the only scale applied; anchor `scale` (normally 1) applies to the anchor→scene offset, never
  to the splat. The validator refuses `unitScale == 0` loudly.
- **One origin.** Splat, colliders, impacts, spatter, POIs are all children of `SceneRoot`.
- **Marker `physicalSize` = 0.170 m**, the measured value in `docs/FRAMES.md`.
- **Sweep every substep** in ballistics; drag-off must match `y = x·tanθ − g·x²/(2·v²·cos²θ)`.
- **One person owns a scene file at a time.** Shared things are prefabs.

## Phases

**Phase 0 — 2–4 Sep: environment, review queue, hygiene** (FTW-68, FTW-73, FTW-7/29)
Unity 6000.3.22f1 with Android support; review #22, #18, #15, #20, #14; Smart Merge attributes and
`Unity/README.md`; Tailscale on the Victus and phone; session log every day.

**Phase 1 — by 6 Sep (I1): the FPS number** (FTW-69, FTW-30)
UnitySplats pinned to a commit; the 12 MB degree-0 `splat_unity.ply` loaded at runtime from
`persistentDataPath` behind `ISplatRenderer`; Vulkan-only Android, MSAA/HDR off; `FpsCounter` +
`PerfLog`; scripted APK build; median and p5 FPS over 60 s standing and walking into
`docs/RESULTS.md` with phone model, renderer commit and multithreaded-rendering on/off. If
UnitySplats fails on device: gsplat-unity (needs Gamma colour space — an app-wide change, decide
deliberately); if both fail, FTW-44 as a **file split** of the non-sorting kernels, not a bitonic
sort rewrite.

**Phase 2 — by 20 Sep (I2): scene via the API, aligned to the marker** (FTW-70, FTW-72)
Contract/Api/Scenes; `FileSceneSource` so Unity is never blocked on the web DB; validator first;
`ReconAR.unity` with `ARTrackedImageManager` (raster marker PNG from the same generator and seed,
`arcoreimg eval-img` ≥ 75), `ARPlaneManager`, `SceneRoot`, minimal UI. Acceptance: swap the `.ply`
server side, restart the app, the new scene appears; a real doorway lines up; text is not
mirrored; a tape-measured edge matches within a couple of cm.

**Phase 3 — by 11 Oct (I3/I4, midterm demo)** (FTW-71)
AR-plane colliders first, then the `splat_to_mesh` PLY as a `MeshCollider` under `SceneRoot`;
`TrajectoryIntegrator` with the parabola test; `ShotController`, impact markers, parameter UI.
Rehearse I4 three times on three days on the real network. Renderer decision final by 9 Oct.

**Phase 4 — by 1 Nov (D12): persistence and two devices** (Tayyab leads FTW-50..53)
Faizan: `CloudAnchorAligner` slot, the marker → anchor → manual fallback chain UI with the
non-metric flag, `AnchorRecord` ↔ PR #18 wire format, the drift measurement harness (FTW-56),
and `Spatter/`.

**Phase 5 — by 8 Nov (D13): research apparatus**
`ShotBatch`: identical shots into `ArPlanes` and `SplatMesh` at several decimation levels,
impact divergence in cm → `docs/RESULTS.md` (Challenge 1). Point-to-point measure tool. After
12 Nov: repeats, bug fixes, report chapters and TC-XX test cases from these numbers.

## Verification

- EditMode tests headless: `Unity/run-tests.ps1`; pure cores: `dotnet test Unity/Recon.Core.Tests`.
- Device: `Unity/build-android.ps1`; `adb logcat -s Unity` shows validate/load/align lines;
  `adb pull` the PerfLog JSON.
- Each gate rehearsed three times on the demo network before the date.

## Open for the team

- Cloud anchor TTL per scene (30 vs 365 days) — `docs/ANCHORING.md`.
- Colour space if gsplat-unity ends up required (Gamma vs current Linear).
- Primary test phone: on the ARCore list, ideally Snapdragon/Adreno (`MOBILE-SPLAT-OPTIONS.md`).
