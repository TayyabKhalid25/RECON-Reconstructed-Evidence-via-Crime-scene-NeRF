---
name: rendering-splats-in-unity
description: Use when integrating UnityGaussianSplatting, loading scenes fetched from the API at runtime, aligning the twin to the marker in AR, or when splats render slowly on the phone or appear in the wrong place.
---

# Rendering splats in Unity

## Overview

Two hard problems hide here, and both should be attacked in week 1, not during integration: **runtime loading** (the renderer package may expect editor-time import) and **mobile frame rate** (splat renderers were built for desktop GPUs).

## The runtime loading problem, resolve before building anything on top

`aras-p/UnityGaussianSplatting` historically converts a `.ply` into its own `GaussianSplatAsset` via an **editor** tool, which does not exist on a phone. A pipeline that serves fresh `.ply` files to a built app needs one of: `verify` against the current package version

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
       └─ GaussianSplatRenderer (the twin)
```

- Set the marker's **physical size** in the reference image library; without it ARCore estimates and scale drifts.
- The splat scene parents under the marker-derived pose. Cloud Anchors then persist that pose for multi-device (host once, store anchor id via `POST /api/scenes/:id/anchor`, resolve on the second device).
- Never scale or flip the splat renderer to "fix" appearance; that is a metadata bug, see debugging-frames-and-scale.

**`docs/ANCHORING.md` is the full design and it is settled** — read it before writing anchor code. The four things most likely to cost you a day:

- **Which package is still open.** AR Foundation 6.5 has a built-in persistent anchor API backed by Cloud Anchors (no extra package), but Unity documents its `SerializableGuid` as non-transferable and lists ARCore as N/A for "shared anchors". `arcore-unity-extensions` v1.54.0 (`arf6`) is the documented two-device route but predates AR Foundation 6.5. FTW-50 decides; do not build on either first.
- **Keyless authorization is mandatory**, not preferred: an API key caps anchor lifetime at 24 hours against a 1-365 day range. And the SHA-1 is per keystore, so three debug keystores means `ErrorNotAuthorized` on two phones — which looks exactly like a broken anchor. One shared keystore.
- **Gate hosting on `FeatureMapQuality == Good`.** Hosting on `Insufficient` returns an id that never resolves, and the failure surfaces days later on a different phone.
- **One anchor per scene, at the marker origin, never one per POI or object part.** Then the stored anchor-to-scene transform is identity, and `scale: 1` there is *not* `metadata.unitScale` — confusing the two is the double-scale bug.

## Mobile frame rate

- **Measure first**: sample scene on the real phone, read FPS from a on-screen counter, week 1. This number sets the training splat budget (the phone, not the 8 GB GPU, is the ceiling).
- Knobs in order: decimate splat count on export, tighten scene bounds, lower render scale in URP, distance-based LOD.
- Fallback (decide by 9 Oct per the risk register): textured mesh on mobile, true splats on the web viewer. The mesh already exists for colliders, so it is reuse.

## Unity repo hygiene

- Enable UnityYAMLMerge (Smart Merge) or scene/prefab conflicts become unresolvable.
- One person owns a scene file at a time; coordinate in chat, prefabs for everything shared.
- `Library/`, builds, and `.ply` test files never enter git.
