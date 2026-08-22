---
name: debugging-frames-and-scale
description: Use when a scene renders mirrored, upside down, sideways, at the wrong size, misaligned with the real room in AR, or when measured accuracy is wildly off despite good PSNR.
---

# Debugging frames and scale

## Overview

There is exactly one coordinate conversion in the pipeline: GPU side, before export, into Unity convention (left handed, Y up, metres). Almost every "the scene looks wrong in Unity" bug is a violation of that rule or a scale problem. The convention is in `docs/FRAMES.md`.

## Diagnosis table

| Symptom | Cause | Fix |
|---|---|---|
| Text or asymmetric objects mirrored | Handedness flip missing, or applied twice | Count the flips across the whole pipeline. The answer must be exactly one, on the GPU side |
| Scene sideways or upside down | Up-axis mismatch | Check `upAxis` in metadata against what the exporter actually did, not what it should have done |
| Room is the wrong size in AR | `unitScale` is 0.0, defaulted, or marker not detected | A unitScale of 0.0 means non-metric. Reject the scene, do not eyeball a scale factor |
| Good PSNR but centimetre accuracy way off | Scale factor wrong, not reconstruction bad | Re-measure the marker in scene units. PSNR says nothing about scale |
| Doorway offset from real doorway in AR | Marker pose noisy or originHint wrong | Re-detect on a flat, well-lit marker. Check the anchor transform stored server side |

## Rules that prevent the whole class

- Unity asserts `handedness == "left"` and `upAxis == "y"` and refuses the scene loudly otherwise. Never "fix" a bad scene with a client-side flip: two conversions cancel and cost a day to find.
- Every capture includes the marker. Every export computes `unitScale` from it.
- The sanity check after any pipeline change: reconstruct a scene with an object of known size, measure it, confirm within a couple of centimetres; confirm text is not mirrored; place by marker and check a real doorway lines up.
