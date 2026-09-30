---
name: rendering-splats-in-unity
description: Use when integrating UnitySplats, loading scenes fetched from the API at runtime, aligning the twin to the marker in AR, or when splats render slowly on the phone or appear in the wrong place.
---

# Rendering splats in Unity

## Overview

Two hard problems hide here, and both should be attacked in week 1, not during integration: **runtime loading** (the renderer package may expect editor-time import) and **mobile frame rate** (splat renderers were built for desktop GPUs).

## The runtime loading problem, resolve before building anything on top

`arloopa/UnitySplats` replaced `aras-p/UnityGaussianSplatting` (which failed on mobile due to wave-op shader requirements). `UnitySplats` directly loads `.ply` files and features an async CPU sort fallback when GPU wave intrinsics are absent (`Runtime/GsplatSorter.cs`). A pipeline that serves fresh `.ply` files to a built app needs one of:

1. **Convert on the GPU side**: Track A exports the package's runtime-loadable format (if the current version has one) instead of, or alongside, raw `.ply`.
2. **Runtime creator**: build the splat asset at runtime from the downloaded `.ply` using the package's API from a small importer we write once.
3. **Worst case**: a conversion step in a desktop tool between download and device, which breaks the automated-pipeline story and should force option 1 or 2.

Whichever lands, record it in `docs/STACK.md` and `metadata.json` (`plyFile` naming reflects the real format).

## Alignment: marker first, anchor second

```
ARTrackedImageManager (marker library, physicalSize set from the printed measurement)
  └─ on tracked: place SceneRoot at image pose
       SceneRoot.localScale = metadata.unitScale (assert != 0)
       assert metadata.handedness == "left" && metadata.upAxis == "y"  // refuse loudly
       └─ GsplatRenderer (the twin)
```

- Set the marker's **physical size** in the reference image library; without it ARCore estimates and scale drifts.
- The splat scene parents under the marker-derived pose. Cloud Anchors then persist that pose for multi-device (host once, store anchor id via `POST /api/scenes/:id/anchor`, resolve on the second device).
- Never scale or flip the splat renderer to "fix" appearance; that is a metadata bug, see debugging-frames-and-scale.

## Mobile frame rate

- **Measure first**: sample scene on the real phone, read FPS from a on-screen counter, week 1. This number sets the training splat budget (the phone, not the 8 GB GPU, is the ceiling).
- Knobs in order: decimate splat count on export, tighten scene bounds, lower render scale in URP, distance-based LOD.
- Fallback (decide by 9 Oct per the risk register): textured mesh on mobile, true splats on the web viewer. The mesh already exists for colliders, so it is reuse.

## Unity repo hygiene

- Enable UnityYAMLMerge (Smart Merge) or scene/prefab conflicts become unresolvable.
- One person owns a scene file at a time; coordinate in chat, prefabs for everything shared.
- `Library/`, builds, and `.ply` test files never enter git.
