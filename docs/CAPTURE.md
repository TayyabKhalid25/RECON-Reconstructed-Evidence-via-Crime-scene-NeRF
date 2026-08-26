# Capture protocol

Every scene after first light uses this protocol and the preset in `gpu/`, so results are
comparable. Deviating from it is fine, but record the deviation in `docs/RESULTS.md` or the
numbers stop meaning anything next to each other.

The expensive mistake is not a bad training run, it is a bad capture: 40 minutes of GPU time on
bad poses produces a melted scene, and no setting fixes it afterwards.

## Camera settings

Set these **before** shooting. Several cannot be fixed afterwards, and a capture that looks fine
to the eye can still fail registration.

| Setting | Value | Why |
|---|---|---|
| Resolution | 1080p | High-bitrate 1080p beats low-bitrate 4K. `ns-process-data` downscales anyway and 4K only slows COLMAP |
| Frame rate | 60 fps | Less motion blur per frame than 30 |
| Orientation | Landscape | |
| HDR | **Off** | 10-bit HDR shifts colour during ffmpeg frame extraction |
| Lens | **Main only. Never zoom or switch mid-take** | A lens change alters the intrinsics, and COLMAP solves for one camera model. Ultra-wide is too distorted |
| Stabilisation | **EIS off** if the phone allows | EIS crops and warps each frame independently, breaking the fixed-pinhole assumption SfM relies on. Optical IS is fine |
| Exposure + focus | **Locked** (long-press) | Autofocus hunting changes focal length between frames; exposure drift changes appearance for the photometric loss |

The last three are the ones that quietly ruin a capture.

## The marker

The marker supplies metric scale **and** the AR alignment origin, so it appears in every capture
from the first one. Print from `tools/make_marker.py` unchanged (seed `20260822`) — the interior
pattern is seeded pseudo-random, so a differently generated sheet will not match a reference image
library built from the old one.

- Tape it **flat** at the scene origin, all four edges, on a matte surface.
- **Fill a decent fraction of the frame with it at least once.** This is the lesson from scene 1:
  the marker was small in every view, which bounded corner precision and left `unitScale` with a
  2.1 percent spread. Scale accuracy is set by how large the marker gets, not by the training.
- Keep it in frame at the start and periodically after, not only at the beginning.
- Never glossy paper. Specular highlights break both COLMAP matching and ARCore tracking.

## The shoot

A table is the easiest subject: bounded, and you can walk around it.

**Set the scene up first.** A bare glossy tabletop has nothing to match on.

- Put 5-8 **textured** objects on it: books, a mug, a cable, printed packaging.
- Avoid glass, mirrors, chrome, anything transparent or reflective. Their appearance changes with
  viewing angle, which breaks the assumption that a point looks the same from every view.
- Matte surfaces win.

**Then two orbits, not one.**

1. **Pass 1, object height.** Stand 1-1.5 m back, phone at roughly object height, looking slightly
   down. Slow full circle, about 40 s, whole subject plus marker in frame throughout.
2. **Pass 2, raised.** Same circle with the phone higher, looking down at roughly 45 degrees,
   about 40 s. This second elevation is what gives the reconstruction vertical structure. A
   single-height orbit looks fine along the capture path and collapses anywhere else.
3. Optional third closer pass for detail, and at least one pass where the marker is large.

End back at the starting view so the loop closes. Total 60-120 s.

**Pace:** 10-15 cm per second. Consecutive frames should overlap 70-80 percent. If it feels
absurdly slow, it is about right.

**Things that cost you the run**

- The scene must not change. Do not move an object between passes; no people or pets walking through.
- Do not cast your own shadow across the subject as you circle — an appearance change tied to your
  position is exactly what confuses the photometric loss.
- Do not fill the frame with one object and lose the wider geometry that ties views together.
- Be deliberate about what is in shot. Anything visible may end up in a report screenshot.

## Processing

Put the video under the **Linux** home, never `/mnt/c/...`, or frame extraction and training crawl.
Copying from Windows is fine; running from `/mnt/c` is not.

```bash
# 1. laptop plugged in, on a hard surface. Thermal throttling is the first suspect
#    if training time doubles between runs.
cp /mnt/c/Users/<you>/Downloads/<capture>.mp4 ~/datasets/
conda activate recon              # sets TORCH_CUDA_ARCH_LIST=8.9

# 2. frames + SfM. Requires the COLMAP 3.13 patch, see docs/STACK.md
python tools/patch_nerfstudio_colmap313.py --check
ns-process-data video --data ~/datasets/<capture>.mp4 --output-dir ~/datasets/<scene>

# 3. FIRST: COLMAP can emit several disconnected models and nerfstudio reads
#    sparse/0 unconditionally. If a stray small model sorts first you get a
#    bogus "only found poses for 1.27%" while a complete reconstruction sits in
#    sparse/1. This cost scene 2 an hour of misdiagnosis.
python tools/pick_colmap_model.py ~/datasets/<scene>

# 4. CHECKPOINT: at least ~80 percent of frames registered. Below that, recapture.
#    Do not train on bad poses.
python - <<'PY'
import json; d=json.load(open('<scene>/transforms.json')); print(len(d['frames']), 'frames posed')
PY

# 5. train with the locked preset
ns-train splatfacto --data ~/datasets/<scene> --max-num-iterations 7000

# 6. scale, frame conversion, metadata
python tools/compute_unitscale.py --data ~/datasets/<scene>
python tools/convert_ply_to_unity.py <raw>.ply <scene>_unity.ply

# 7. numbers into docs/RESULTS.md the same day, with date and machine
```

## Out of memory

Turn knobs in this order, and record what you settled on:

1. downscale images
2. cap splat count
3. raise the densification threshold
4. prune harder
5. tighten scene bounds

## Measured: marker size in frame drives scale accuracy

Two captures, same protocol, different marker size in frame:

| | Scene 1 | Scene 2 |
|---|---|---|
| Marker detected in | 102 frames | **151 frames** |
| `unitScale` spread | 2.1 % | **1.86 %** |

Getting the marker larger in frame is the cheapest available improvement to scale accuracy. It costs
nothing at capture time and cannot be recovered afterwards.

## Known limitation to state, not hide

Scale accuracy is bounded by how large the marker appears, and the reconstruction is metric only
up to that measurement. `unitScale` of 0.0 with `scaleMethod: "none"` means the scene is not
metric and must not be used for any accuracy claim — see `docs/FRAMES.md`.
