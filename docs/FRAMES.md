# Frame convention

SfM tooling and Unity do not share conventions. COLMAP output is right handed, Y down (computer
vision camera convention). Unity is left handed, Y up. Exactly one conversion happens in the
whole pipeline, and this file says where.

## The rule

**Convert once, on the GPU side, before export.** The exported `.ply` is already in Unity's
convention: left handed, Y up, in **scene units** (not metres). `metadata.json` records
`handedness` and `upAxis` so the Unity client asserts them instead of guessing, and `unitScale`
(metres per scene unit) so the client scales the scene root once. If the client ever sees
anything other than `left` / `y`, or a `unitScale` of 0, it refuses the scene loudly.

Scale is deliberately **not** baked into the `.ply`: `gpu/run_scene.py` runs the conversion
without `--unit-scale`, the export and the collider mesh from `tools/splat_to_mesh.py` stay in
scene units, `metadata.json.boundingBox` is in scene units too, and Unity applies `unitScale`
exactly once on `SceneRoot`. Baking metres into the file while Unity also scales is the
double-scale bug, which presents as a modelling error rather than a unit error.

Never apply a flip on the Unity side. Two conversions cancel out and cost a day to find.

### The converter

`tools/convert_ply_to_unity.py` is that one conversion. Nerfstudio exports Z up, right handed;
the mapping is `(x, y, z) -> (x, z, y)`, a Y/Z swap, which negates the basis determinant and so
turns right handed into left handed.

Five things move together, and a converter that only moves positions is wrong in a way that
looks nearly right:

| Field | Treatment |
|---|---|
| positions | swap y, z |
| normals | swap y, z |
| `scale_1` / `scale_2` | swapped; they are per-axis extents |
| `rot_*` quaternion | conjugated by the same swap |
| `f_rest_*` SH coefficients | degree >= 1 terms are direction dependent, so the basis is permuted and signed |

The script writes `comment Vertical Axis: y` and `comment Handedness: left` into the header, and
**refuses to run on a file already marked Y up** so a double conversion cannot happen by
accident. `--unit-scale S` multiplies positions and adds `log(S)` to the log-scales; omit it for
a non-metric scene. `--self-test` checks the swap, the handedness flip, the scale handling and
that converting twice is the identity.

## Scale

Monocular SfM has no absolute scale. Every capture includes the printed marker of known
physical size at the scene origin. After reconstruction we detect it, compute metres per scene
unit, and write it to `unitScale` with `scaleMethod: "marker"`. A `unitScale` of 0.0 means the
scene is not metric and must not be used for any accuracy claim.

### Computing unitScale

`tools/compute_unitscale.py` implements the `scaleMethod: "marker"` path. It detects the marker's
solid black border in each frame, triangulates the four corners through the COLMAP poses in
`transforms.json`, and divides the known 0.170 m edge by the measured edge in scene units.

The discriminator is the border itself: a candidate 4-gon is only accepted if a band just inside
its outline is dark while the interior is brighter. That rejects the paper sheet, the tabletop,
floor tiles and phones, and it means the tool measures the **170 mm black edge** rather than the
**A4 sheet** — confusing those two is a silent ~20 percent scale error.

**It refuses rather than guesses.** The triangulated quad must have four mutually agreeing edges
and both diagonals at sqrt(2) x edge, within `--tol` (default 12 percent). If no view pair passes,
it exits non-zero and emits nothing. A wrong `unitScale` is worse than a missing one: 0.0 makes
the client refuse the scene loudly, whereas a wrong value produces confident, incorrect
measurements. `--self-test` checks triangulation against synthetic geometry, that a skewed quad is
rejected, and that corner ordering is rotation invariant.

Report the **spread across view pairs** as the uncertainty. Recovering 170 mm from the marker that
defined the scale is circular; the spread, and a plausibility check on the scene bounding box, are
the figures that mean something.

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
- [x] Faizan initials: FT, 2026-09-02
