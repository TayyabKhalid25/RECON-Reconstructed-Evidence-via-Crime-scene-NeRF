# Frame convention

SfM tooling and Unity do not share conventions. COLMAP output is right handed, Y down (computer
vision camera convention). Unity is left handed, Y up. Exactly one conversion happens in the
whole pipeline, and this file says where.

## The rule

**Convert once, on the GPU side, before export.** The exported `.ply` is already in Unity's
convention: left handed, Y up, metres. `metadata.json` records `handedness` and `upAxis` so the
Unity client asserts them instead of guessing. If the client ever sees anything other than
`left` / `y`, it refuses the scene loudly.

Never apply a flip on the Unity side. Two conversions cancel out and cost a day to find.

## Scale

Monocular SfM has no absolute scale. Every capture includes the printed marker of known
physical size at the scene origin. After reconstruction we detect it, compute metres per scene
unit, and write it to `unitScale` with `scaleMethod: "marker"`. A `unitScale` of 0.0 means the
scene is not metric and must not be used for any accuracy claim.

### The printed marker, as measured

| Property | Value |
|---|---|
| Measured outer edge | **170.0 mm x 170.0 mm** |
| Measured on | 2026-08-23, steel ruler, printed at 100 percent scale |
| Paper | A4, matte |
| Source | `tools/make_marker.py`, seed `20260822`, `marker-a4-170mm.pdf` |

The measured figure is the outer edge of the solid black border ring, not the paper size.

**Unity / AR Foundation:** set the reference image `physicalSize` to **0.170 m**. If it is left
unset, ARCore estimates the marker size and the alignment scale drifts.

**A reprint must come from the same generator and seed.** The interior pattern is
pseudo-random; a freshly generated marker is a different image and will not match a reference
image library built from this one. Reprint from `tools/make_marker.py` unchanged, and
re-measure, because a different printer may scale differently.

## Sign off

- [ ] Diagram added showing both frames and the conversion
- [ ] Tayyab initials: ____
- [ ] Wahaj initials: ____
- [ ] Faizan initials: ____
