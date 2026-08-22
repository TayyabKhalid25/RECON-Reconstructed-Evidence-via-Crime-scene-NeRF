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

## Sign off

- [ ] Diagram added showing both frames and the conversion
- [ ] Tayyab initials: ____
- [ ] Wahaj initials: ____
- [ ] Faizan initials: ____
