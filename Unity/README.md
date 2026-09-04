# Track C · Unity AR client

Owner: Tayyab (lead). Faizan is the second pair of hands (since 30 Aug 2026).

The phone side of the pipeline: fetch a reconstructed scene from the web API, put it back on
top of the real room by the printed marker, render the Gaussian splats, and run the ballistics
and spatter overlay inside it. Handbook Section 09 is the build order; `../docs/FRAMES.md` is
the coordinate contract; `../docs/ANCHORING.md` is how the twin survives a session ending;
`../docs/UNITY-TRACK-PLAN.md` is the track's execution plan under the handbook.

## Versions

Unity **6000.3.22f1** (`Recon/ProjectSettings/ProjectVersion.txt`), URP 17.3.0, AR Foundation
6.x with the ARCore and ARKit providers, XR Interaction Toolkit 3.5.1. Exact installed versions
live in `../docs/STACK.md`; a version change goes there in the same commit, never silently.

## Opening the project

1. Unity Hub → Installs → add **6000.3.22f1** with *Android Build Support* (SDK & NDK tools,
   OpenJDK). Install the exact patch: a different one rewrites project files and floods diffs.
2. Hub → Projects → Open → select `Unity/Recon` (the folder that directly contains `Assets/`,
   `Packages/`, `ProjectSettings/`). Selecting `Unity/` or the repo root shows "no project found".
3. File → Build Settings → switch platform to **Android**.

## Smart Merge, once per machine

Scenes, prefabs and `.asset` files are YAML that git cannot line-merge. `Recon/.gitattributes`
routes them to UnityYAMLMerge; git still needs to know where the tool is:

```
git config merge.unityyamlmerge.name "Unity SmartMerge (UnityYAMLMerge)"
git config merge.unityyamlmerge.driver "\"C:/Program Files/Unity/Hub/Editor/6000.3.22f1/Editor/Data/Tools/UnityYAMLMerge.exe\" merge -p %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
```

macOS: the tool is at `/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app/Contents/Tools/UnityYAMLMerge`.

Even with the merge driver: **one person owns a scene file at a time**, say so in chat, and put
anything shared in a prefab. `Assets/Scenes/SampleScene.unity` is Tayyab's (the stock AR template
plus his splat experiments); RECON's own scene is `Assets/Recon/Scenes/ReconAR.unity` (Faizan).

## Where things live

```
Recon/Assets/
  MobileARTemplateAssets/   stock Unity AR template, do not build on it
  Samples/                  XR Interaction Toolkit samples, same
  Recon/                    everything RECON, in its own assemblies (Recon.Runtime, Recon.Editor,
                            Recon.Tests). Contract/ Api/ Scenes/ Rendering/ Alignment/ Colliders/
                            Ballistics/ Spatter/ UI/ Diagnostics/ Config/ Editor/ Tests/
```

Inside each area, `Core/` holds pure C# with no `UnityEngine` reference (validation, integration
maths, parsers). Those files compile under plain `dotnet` too, which is how they are tested on a
machine without the Editor.

Rules the code enforces, from `../docs/FRAMES.md` and the project skills:

- The `.ply` arrives already in Unity convention (left handed, Y up) and in **scene units**.
  `SceneRoot.localScale = metadata.unitScale` is the only scale applied anywhere. Nothing flips
  an axis on the client; a scene with `unitScale 0` or the wrong `handedness`/`upAxis` is refused
  with the reason on screen.
- Everything the user sees (splat, colliders, impacts, spatter, POIs) is a child of `SceneRoot`.
- The splat renderer package sits behind `Rendering/ISplatRenderer`; app code never references
  a renderer package directly. Decision record: `../docs/MOBILE-SPLAT-OPTIONS.md`.
- Ballistics sweep a raycast on **every** substep; the drag-free shot must match the analytic
  parabola in the unit test.

## Tests and builds

Pure C# cores (validation, trajectory integration, spatter maths, PLY parsing) are testable
without the Editor: `dotnet test Unity/Recon.Core.Tests`. Unity-side EditMode tests run from the
Test Runner window or headless with `Unity/run-tests.ps1`. `Unity/build-android.ps1` builds
`Builds/Recon.apk` in batch mode and installs it with `adb`; `Unity/push-dev-scene.ps1` pushes a
`.ply` + `metadata.json` to the phone for the FTW-30 measurement. These arrive with FTW-69; until
that merges the commands above do not exist yet.

## Gotchas learned the hard way

- **An interrupted Android build leaves the tree dirty.** AR Foundation moves the XR Simulation
  assets to `Assets/XR/Temp` and the performance-test package writes `Assets/Resources/*.json`
  during a build, restoring them afterwards. Kill the build mid-way and they stay moved. Always
  `git status` before committing after a build; `git checkout --` the touched settings and delete
  `Assets/XR/Temp` and `Assets/Resources` if the build did not finish.
- **ARCore refuses a reference image without a texture at build time**
  (`ArCoreImg.MissingTextureException`). `SceneBootstrap` therefore only creates the marker entry
  when `Assets/Recon/Markers/recon-marker-170mm.png` exists. To score a marker image the way the
  build does: `Library/PackageCache/com.unity.xr.arcore@*/Tools~/Windows/arcoreimg.exe eval-img
  --input_image_path=<png>` (ours scores 100; ARCore wants 75 or better).
- **`Assets/Gsplat/Settings/Resources/GsplatSettings.asset` is required at runtime** by the
  UnitySplats renderer and is created by the package's own editor bootstrap on first import. It
  is committed; do not delete it.
- **Deep worktree paths break on Windows.** The template's asset paths exceed MAX_PATH under a
  long root. `git config --global core.longpaths true` and a short root such as `%TEMP%\rw\<name>`.

## Never commit

`Library/`, `Builds/`, `.ply`/`.splat` files, splat `.bytes` data, videos. `.gitignore` enforces
the repo-wide rules; big files move over Tailscale (`tailscale file cp scene.ply victus:`).
