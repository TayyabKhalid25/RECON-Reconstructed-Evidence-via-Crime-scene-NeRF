#!/usr/bin/env python3
"""One command: capture video in, Unity-convention .ply plus metadata.json out.

Replaces the seven-step manual sequence in docs/CAPTURE.md. The pieces already
live in tools/; this is orchestration and gates, not new algorithms. Track B's
worker invokes this, so it has to be callable and has to fail with a readable
reason rather than half-succeeding.

Stages, in order. Any can be the start or end point (--from / --to / --only), so
a failed run resumes instead of recomputing COLMAP:

  preflight   env is sane: patch applied, arch list set, ninja, GPU visible
  process     ns-process-data video -> frames + COLMAP poses
  pickmodel   promote the largest COLMAP sparse model (the two-models trap)
  register    GATE: enough frames posed, or stop
  train       ns-train with the locked preset, sampling VRAM throughout
  eval        ns-eval -> metrics json
  export      ns-export gaussian-splat -> raw .ply
  unitscale   marker -> metres per scene unit; GATE on self-consistency
  convert     the one frame conversion, GPU side; GATE on the output header
  metadata    metadata.json per docs/API.md
  results     the docs/RESULTS.md row, printed for pasting

Three gates exist because a silent partial success is what produces a confident
wrong measurement two months later:

  register    below --min-registered, stop. Do not train on bad poses.
  unitscale   spread above --max-spread, stop. A scale that disagrees with
              itself across view pairs is not a scale.
  convert     the output header must say left-handed and Y-up, per docs/FRAMES.md.

Scale is deliberately NOT baked into the .ply. The export stays in scene units
and `unitScale` travels in metadata.json for Unity to apply. Baking it here and
also applying it there is the classic double-scale bug, so `convert` is invoked
without --unit-scale on purpose. Verified against first light: splat_unity.ply
measures 25.1 units across, 9.28 m once scaled.

Usage:
  python gpu/run_scene.py --video ~/datasets/1.mp4 --scene 1
  python gpu/run_scene.py --scene 1 --from unitscale        # resume
  python gpu/run_scene.py --scene 1 --only metadata
  python gpu/run_scene.py --video x.mp4 --scene 3 --dry-run
  python gpu/run_scene.py --self-test
"""
import argparse
import datetime as dt
import hashlib
import json
import os
import shutil
import subprocess
import sys
import threading
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
TOOLS = REPO / "tools"

STAGES = ["preflight", "process", "pickmodel", "register", "train", "eval",
          "export", "unitscale", "convert", "metadata", "results"]

# Locked preset, gpu/PRESET.md. Changing these makes runs incomparable.
PRESET_ITERATIONS = 7000
PRESET_DOWNSCALES = 2

MARKER_EDGE_M = 0.17          # docs/FRAMES.md, measured in FTW-25


class Fail(Exception):
    """A gate refused to continue. The message is the reason, for humans."""


def log(msg):
    print(f"[run_scene] {msg}", flush=True)


def run(cmd, **kw):
    log("$ " + " ".join(str(c) for c in cmd))
    return subprocess.run([str(c) for c in cmd], check=True, **kw)


# --------------------------------------------------------------------------
# pure helpers, covered by --self-test
# --------------------------------------------------------------------------

def registered_count(transforms_path):
    """How many frames COLMAP actually posed."""
    with open(transforms_path) as f:
        return len(json.load(f)["frames"])


def extracted_count(dataset_dir):
    """How many frames were extracted, so the ratio has a denominator.

    Counts the full-resolution images/ directory rather than a downscale, and
    tolerates any extension ns-process-data chose.
    """
    d = Path(dataset_dir) / "images"
    if not d.is_dir():
        return 0
    return sum(1 for p in d.iterdir() if p.suffix.lower() in
               (".jpg", ".jpeg", ".png"))


def peak_from_samples(samples):
    """Peak MiB from nvidia-smi samples, ignoring blank or junk lines."""
    vals = []
    for s in samples:
        s = str(s).strip()
        if not s:
            continue
        try:
            vals.append(int(float(s)))
        except ValueError:
            continue
    return max(vals) if vals else None


def unity_frame_ok(header):
    """docs/FRAMES.md: the export must declare left-handed, Y up.

    Checked by reading the file we just wrote rather than trusting the tool,
    because this is the gate that stops a mirrored scene reaching Unity.
    """
    h = header.lower()
    return "handedness: left" in h and "vertical axis: y" in h


def ply_header_and_count(path):
    """Read a binary PLY's header text and vertex count without loading the body."""
    with open(path, "rb") as f:
        blob = f.read(65536)
    end = blob.find(b"end_header\n")
    if end < 0:
        raise Fail(f"{path}: no end_header in the first 64 KB; not a binary PLY")
    header = blob[: end + len(b"end_header\n")].decode("latin-1")
    count = None
    for line in header.splitlines():
        if line.startswith("element vertex"):
            count = int(line.split()[-1])
    if count is None:
        raise Fail(f"{path}: header has no 'element vertex'")
    return header, count


def sha256_file(path, chunk=1 << 20):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(chunk), b""):
            h.update(block)
    return h.hexdigest()


def bounding_box(ply_path):
    """Axis-aligned bounds in scene units. Imports numpy lazily.

    Reuses tools/decimate_splats.read_ply so there is one PLY reader, not two.
    """
    sys.path.insert(0, str(TOOLS))
    from decimate_splats import read_ply
    _, names, arr = read_ply(str(ply_path))
    cols = [names.index(c) for c in ("x", "y", "z")]
    pts = arr[:, cols]
    return ([float(v) for v in pts.min(axis=0)],
            [float(v) for v in pts.max(axis=0)])


def build_metadata(scene_id, ply_name, splat_count, source_frames, unit_scale,
                   scale_method, bbox_min, bbox_max, metrics, iterations,
                   minutes, peak_vram_mb, sha256, created_at):
    """metadata.json exactly as docs/API.md specifies. Field order matches the sample."""
    return {
        "sceneId": scene_id,
        "plyFile": ply_name,
        "splatCount": splat_count,
        "sourceFrames": source_frames,
        # Fixed by docs/FRAMES.md and enforced by the convert gate. Not derived
        # from the run, because the run is required to produce exactly this.
        "handedness": "left",
        "upAxis": "y",
        "unitScale": unit_scale,
        "scaleMethod": scale_method,
        "boundingBox": {"min": bbox_min, "max": bbox_max},
        "originHint": [0, 0, 0],
        "metrics": metrics,
        "training": {
            "iterations": iterations,
            "minutes": minutes,
            "peakVramMb": peak_vram_mb,
        },
        "sha256": sha256,
        "createdAt": created_at,
    }


def metadata_gaps(meta):
    """Names the fields a consumer would choke on. Empty list means complete.

    'Complete' is the ticket's done-when, so it is checked rather than assumed.
    A null unitScale is called out specially: Unity cannot place a twin at real
    size without it, and that failure looks like a modelling bug on the phone.
    """
    gaps = []
    for key in ("sceneId", "plyFile", "splatCount", "sourceFrames", "handedness",
                "upAxis", "unitScale", "scaleMethod", "boundingBox",
                "originHint", "metrics", "training", "sha256", "createdAt"):
        if key not in meta or meta[key] in (None, "", {}):
            gaps.append(key)
    if meta.get("splatCount") in (0, None):
        gaps.append("splatCount is zero")
    if meta.get("unitScale") in (0, None):
        gaps.append("unitScale is unset (scene is not metric)")
    for k in ("psnr", "ssim", "lpips"):
        if k not in meta.get("metrics", {}):
            gaps.append(f"metrics.{k}")
    tr = meta.get("training", {})
    for k in ("iterations", "minutes", "peakVramMb"):
        if tr.get(k) in (None, ""):
            gaps.append(f"training.{k}")
    return gaps


def results_row(date, scene, machine, frames, registered_pct, iterations,
                minutes, peak_vram, metrics, splats, ply_mb, notes):
    """One docs/RESULTS.md table row, ready to paste."""
    def num(v, fmt="{:.3f}"):
        return "-" if v is None else fmt.format(v)
    return ("| {d} | {s} | {m} | {f} | {rp} | {it} | {mn} | {vr} | {psnr} | "
            "{ssim} | {lpips} | {sp} | {mb} | {nt} |").format(
        d=date, s=scene, m=machine, f=frames,
        rp=f"{registered_pct:.0f}%" if registered_pct is not None else "-",
        it=iterations, mn=num(minutes, "{:.1f}"),
        vr="not sampled" if peak_vram is None else f"**{peak_vram}**",
        psnr=num(metrics.get("psnr"), "{:.2f}"),
        ssim=num(metrics.get("ssim")), lpips=num(metrics.get("lpips")),
        sp=splats, mb=num(ply_mb, "{:.0f}"), nt=notes)


# --------------------------------------------------------------------------
# run state, so --from / --only actually resume
# --------------------------------------------------------------------------

# Values worth carrying between invocations. Paths and thresholds come from the
# command line every time; these are the measurements a later stage needs and
# cannot recompute cheaply.
CARRIED = ("frames_posed", "frames_total", "registered_pct", "minutes",
           "peak_vram", "metrics", "eval_fps", "unit_scale", "scale_method",
           "splat_count", "raw_ply", "unity_ply")


def state_path(ctx):
    return ctx["run_dir"] / f"{ctx['scene']}-run-state.json"


def load_state(ctx):
    """Rehydrate earlier stages' results.

    Without this, `--from metadata` would write a metadata.json with a null
    unitScale and no metrics -- an incomplete file that looks like a successful
    run. The gaps check would catch it, but the right fix is not losing the
    values in the first place.
    """
    p = state_path(ctx)
    if not p.is_file():
        return
    try:
        saved = json.loads(p.read_text())
    except (OSError, json.JSONDecodeError) as e:
        log(f"ignoring unreadable run state {p}: {e}")
        return
    for k in CARRIED:
        if k in saved and saved[k] is not None:
            ctx.setdefault(k, saved[k])
    log(f"resumed run state from {p.name}")


def save_state(ctx):
    out = {k: ctx.get(k) for k in CARRIED if ctx.get(k) is not None}
    for k, v in out.items():
        if isinstance(v, Path):
            out[k] = str(v)
    state_path(ctx).write_text(json.dumps(out, indent=2) + "\n")


# --------------------------------------------------------------------------
# VRAM sampling
# --------------------------------------------------------------------------

class VramSampler:
    """Samples GPU memory every interval seconds in a thread.

    Exists because peak VRAM was missed on the first real run and could not be
    recovered afterwards -- the number only exists while the process is alive.
    Degrades to None if nvidia-smi is absent rather than failing the run.
    """

    def __init__(self, interval=0.5, csv_path=None):
        self.interval = interval
        self.csv_path = csv_path
        self.samples = []
        self._stop = threading.Event()
        self._thread = None
        self.errors = 0
        self.available = shutil.which("nvidia-smi") is not None

    def _loop(self):
        while not self._stop.is_set():
            try:
                out = subprocess.run(
                    ["nvidia-smi", "--query-gpu=memory.used",
                     "--format=csv,noheader,nounits"],
                    capture_output=True, text=True, timeout=10, check=False)
                line = out.stdout.strip().splitlines()
                if line:
                    self.samples.append((time.time(), line[0].strip()))
            except (OSError, subprocess.SubprocessError) as e:
                # A sampling hiccup must never kill a training run that is
                # minutes in, but swallowing it silently would let "peak VRAM
                # null" look like a machine without a GPU. Count and report once.
                self.errors += 1
                if self.errors == 1:
                    log(f"VRAM sampling failed ({e}); continuing without it")
            self._stop.wait(self.interval)

    def __enter__(self):
        if self.available:
            self._thread = threading.Thread(target=self._loop, daemon=True)
            self._thread.start()
        else:
            log("nvidia-smi not found: peak VRAM will be recorded as null")
        return self

    def __exit__(self, *exc):
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=5)
        if self.csv_path and self.samples:
            t0 = self.samples[0][0]
            with open(self.csv_path, "w") as f:
                f.write("seconds,memory_used_mib\n")
                f.writelines(f"{t - t0:.1f},{v}\n" for t, v in self.samples)
            log(f"VRAM samples -> {self.csv_path}")
        return False

    @property
    def peak(self):
        return peak_from_samples(v for _, v in self.samples)


# --------------------------------------------------------------------------
# stages
# --------------------------------------------------------------------------

def stage_preflight(ctx):
    """Cheap checks that each explain a failure people have actually hit."""
    problems = []

    r = subprocess.run([sys.executable, str(TOOLS / "patch_nerfstudio_colmap313.py"),
                        "--check"], capture_output=True, text=True, check=False)
    if r.returncode != 0:
        problems.append(
            "Nerfstudio is not patched for COLMAP 3.13 -- ns-process-data will die "
            "on '--SiftExtraction.use_gpu'. Fix: python tools/patch_nerfstudio_colmap313.py "
            "(the patch lives in site-packages and does not survive an env rebuild)")

    if os.environ.get("TORCH_CUDA_ARCH_LIST") != "8.9":
        problems.append(
            f"TORCH_CUDA_ARCH_LIST={os.environ.get('TORCH_CUDA_ARCH_LIST')!r}, expected '8.9'. "
            "torch's JIT cache is keyed on build config, so a wrong value silently "
            "triggers a fresh 7.5 minute all-architectures gsplat compile. "
            "Fix: activate the recon env")

    if not shutil.which("ninja"):
        problems.append("ninja is not on PATH; the gsplat JIT build fails without it. "
                        "Fix: run inside an activated recon env, not via a full path to python")

    for exe in ("ns-process-data", "ns-train", "ns-eval", "ns-export", "colmap"):
        if not shutil.which(exe):
            problems.append(f"{exe} not on PATH")

    if not shutil.which("nvidia-smi"):
        log("WARNING: nvidia-smi absent, peak VRAM cannot be sampled")

    if problems:
        raise Fail("preflight failed:\n  - " + "\n  - ".join(problems))
    log("preflight OK")


def stage_process(ctx):
    if not ctx["video"]:
        raise Fail("process needs --video (or start later with --from)")
    video = Path(ctx["video"]).expanduser()
    if not video.is_file():
        raise Fail(f"video not found: {video}")
    run(["ns-process-data", "video", "--data", video,
         "--output-dir", ctx["dataset"]])


def stage_pickmodel(ctx):
    """The two-sparse-models trap. Cost scene 2 an hour of misdiagnosis."""
    run([sys.executable, str(TOOLS / "pick_colmap_model.py"), ctx["dataset"]])


def stage_register(ctx):
    """GATE. Training on bad poses produces a confident wrong scene."""
    tj = Path(ctx["dataset"]) / "transforms.json"
    if not tj.is_file():
        raise Fail(f"no transforms.json at {tj}; did process run?")
    posed = registered_count(tj)
    total = extracted_count(ctx["dataset"]) or posed
    pct = 100.0 * posed / total if total else 0.0
    ctx["frames_posed"] = posed
    ctx["frames_total"] = total
    ctx["registered_pct"] = pct
    log(f"registered {posed}/{total} = {pct:.1f}%")
    if pct < ctx["min_registered"]:
        raise Fail(
            f"only {pct:.1f}% of frames registered (floor is {ctx['min_registered']}%).\n"
            "  Per docs/CAPTURE.md this is a recapture, not something to train through.\n"
            "  Before recapturing, confirm pick_colmap_model did its job: a stray\n"
            "  4-image model sorting first reports ~1% while a complete\n"
            "  reconstruction sits in sparse/1.")
    log("register gate passed")


def stage_train(ctx):
    csv_path = ctx["run_dir"] / f"{ctx['scene']}-vram-samples.csv"
    started = time.time()
    with VramSampler(csv_path=csv_path) as sampler:
        run(["ns-train", "splatfacto",
             "--data", ctx["dataset"],
             "--max-num-iterations", PRESET_ITERATIONS,
             "--pipeline.model.num-downscales", PRESET_DOWNSCALES,
             "--experiment-name", ctx["scene"],
             "--output-dir", ctx["outputs"],
             "--viewer.quit-on-train-completion", "True"])
    ctx["minutes"] = (time.time() - started) / 60.0
    ctx["peak_vram"] = sampler.peak
    log(f"trained in {ctx['minutes']:.1f} min, peak VRAM {ctx['peak_vram']} MiB")


def latest_config(ctx):
    """Newest config.yml for this scene, so eval/export follow train without paths."""
    root = Path(ctx["outputs"]) / ctx["scene"] / "splatfacto"
    if not root.is_dir():
        raise Fail(f"no training runs under {root}")
    runs = sorted((p for p in root.iterdir() if (p / "config.yml").is_file()),
                  key=lambda p: p.stat().st_mtime)
    if not runs:
        raise Fail(f"no config.yml under {root}; did train finish?")
    return runs[-1] / "config.yml"


def stage_eval(ctx):
    out = ctx["run_dir"] / f"{ctx['scene']}-splatfacto-eval.json"
    run(["ns-eval", "--load-config", latest_config(ctx), "--output-path", out])
    with open(out) as f:
        res = json.load(f)["results"]
    ctx["metrics"] = {"psnr": res.get("psnr"), "ssim": res.get("ssim"),
                      "lpips": res.get("lpips")}
    ctx["eval_fps"] = res.get("fps")
    log(f"eval psnr={ctx['metrics']['psnr']:.2f} ssim={ctx['metrics']['ssim']:.3f}")


def stage_export(ctx):
    dest = ctx["export_dir"]
    dest.mkdir(parents=True, exist_ok=True)
    run(["ns-export", "gaussian-splat", "--load-config", latest_config(ctx),
         "--output-dir", dest])
    plys = [p for p in dest.glob("*.ply") if not p.name.endswith("_unity.ply")]
    if not plys:
        raise Fail(f"ns-export wrote no .ply into {dest}")
    ctx["raw_ply"] = max(plys, key=lambda p: p.stat().st_mtime)
    log(f"raw export {ctx['raw_ply']}")


def stage_unitscale(ctx):
    """GATE. A scale that disagrees with itself across view pairs is not a scale."""
    out = ctx["run_dir"] / f"{ctx['scene']}-unitscale.json"
    r = subprocess.run(
        [sys.executable, str(TOOLS / "compute_unitscale.py"),
         "--data", ctx["dataset"], "--marker-m", str(MARKER_EDGE_M),
         "--json-out", str(out)],
        capture_output=True, text=True, check=False)
    sys.stdout.write(r.stdout)
    if r.returncode != 0 or not out.is_file():
        sys.stderr.write(r.stderr)
        raise Fail(
            "compute_unitscale failed, so the scene has no metric scale.\n"
            "  Unity cannot place the twin at real size without unitScale.\n"
            "  Most likely the marker was not detected: check it was in frame and\n"
            "  large enough. Marker size in frame is the binding constraint on scale\n"
            "  accuracy (docs/CAPTURE.md).")
    with open(out) as f:
        us = json.load(f)
    ctx["unit_scale"] = us["unitScale"]
    ctx["scale_method"] = us.get("scaleMethod", "marker")
    spread = us.get("relativeSpread")
    log(f"unitScale {ctx['unit_scale']:.6f} from {us.get('framesDetected')} frames, "
        f"spread {spread:.3%}" if spread is not None else "")
    if spread is not None and spread > ctx["max_spread"]:
        raise Fail(
            f"unitScale spread {spread:.2%} exceeds the {ctx['max_spread']:.2%} ceiling.\n"
            "  The marker disagrees with itself across view pairs, so any distance\n"
            "  measured from this scene inherits that error. Recapture with the marker\n"
            "  larger in frame: scene 2 moved 2.1% -> 1.86% that way, and it is the\n"
            "  cheapest available lever on accuracy.")
    log("unitscale gate passed")


def stage_convert(ctx):
    """The one frame conversion, GPU side. GATE on the header we just wrote.

    No --unit-scale here on purpose: the .ply stays in scene units and Unity
    applies unitScale from metadata.json. Baking it in both places is the
    double-scale bug, and it looks like a modelling error rather than a unit bug.
    """
    raw = ctx.get("raw_ply") or ctx["export_dir"] / "splat.ply"
    if not Path(raw).is_file():
        raise Fail(f"no raw .ply to convert at {raw}; run export first")
    out = ctx["export_dir"] / "splat_unity.ply"
    run([sys.executable, str(TOOLS / "convert_ply_to_unity.py"), raw, out])

    header, count = ply_header_and_count(out)
    if not unity_frame_ok(header):
        raise Fail(
            f"{out} does not declare the Unity frame.\n"
            "  docs/FRAMES.md requires left-handed, Y up, converted once GPU side.\n"
            "  Shipping this would put a mirrored or sideways room in front of a\n"
            "  panel, and the fix must happen here, never on the Unity side.\n"
            f"  Header said:\n{header}")
    ctx["unity_ply"] = out
    ctx["splat_count"] = count
    log(f"convert gate passed: {count} splats, Unity frame declared")


def stage_metadata(ctx):
    ply = ctx.get("unity_ply") or ctx["export_dir"] / "splat_unity.ply"
    if not Path(ply).is_file():
        raise Fail(f"no {ply}; run convert first")
    header, count = ply_header_and_count(ply)
    if not unity_frame_ok(header):
        raise Fail(f"{ply} is not in the Unity frame; refusing to describe it as such")
    bmin, bmax = bounding_box(ply)
    meta = build_metadata(
        scene_id=ctx["scene_id"],
        ply_name=Path(ply).name,
        splat_count=count,
        source_frames=ctx.get("frames_posed"),
        unit_scale=ctx.get("unit_scale"),
        scale_method=ctx.get("scale_method", "marker"),
        bbox_min=bmin, bbox_max=bmax,
        metrics=ctx.get("metrics", {}),
        iterations=PRESET_ITERATIONS,
        minutes=round(ctx["minutes"], 2) if ctx.get("minutes") is not None else None,
        peak_vram_mb=ctx.get("peak_vram"),
        sha256=sha256_file(ply),
        created_at=dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    )
    gaps = metadata_gaps(meta)
    out = ctx["export_dir"] / "metadata.json"
    with open(out, "w") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")
    log(f"metadata -> {out}")
    if gaps:
        # Written anyway so the run is inspectable, but named loudly: an
        # incomplete metadata.json is a scene Unity cannot place.
        log("INCOMPLETE metadata, missing or unusable: " + ", ".join(gaps))
        if not ctx["allow_partial"]:
            raise Fail(
                "metadata.json is incomplete: " + ", ".join(gaps) +
                "\n  Re-run the stages that produce those fields, or pass "
                "--allow-partial to accept it deliberately.")
    ctx["metadata"] = meta


def stage_results(ctx):
    ply = ctx.get("unity_ply") or ctx["export_dir"] / "splat_unity.ply"
    mb = Path(ply).stat().st_size / 1e6 if Path(ply).is_file() else None
    notes = (f"splatfacto, num_downscales {PRESET_DOWNSCALES}. "
             f"unitScale {ctx.get('unit_scale')}")
    if ctx.get("eval_fps"):
        notes += f". Eval fps {ctx['eval_fps']:.1f}"
    row = results_row(
        # Local date deliberately, not UTC: RESULTS.md records the day the
        # person did the work, and the team is UTC+5 -- a late-evening run would
        # otherwise be filed under yesterday. astimezone() makes it tz-aware.
        date=dt.datetime.now().astimezone().date().isoformat(),
        scene=ctx["scene"], machine=ctx["machine"],
        frames=ctx.get("frames_posed", "-"),
        registered_pct=ctx.get("registered_pct"),
        iterations=PRESET_ITERATIONS,
        minutes=ctx.get("minutes"), peak_vram=ctx.get("peak_vram"),
        metrics=ctx.get("metrics", {}), splats=ctx.get("splat_count", "-"),
        ply_mb=mb, notes=notes)
    print()
    print("Paste into docs/RESULTS.md (same day, per AGENTS.md):")
    print(row)
    print()
    (ctx["run_dir"] / f"{ctx['scene']}-results-row.md").write_text(row + "\n")


STAGE_FN = {
    "preflight": stage_preflight, "process": stage_process,
    "pickmodel": stage_pickmodel, "register": stage_register,
    "train": stage_train, "eval": stage_eval, "export": stage_export,
    "unitscale": stage_unitscale, "convert": stage_convert,
    "metadata": stage_metadata, "results": stage_results,
}


# --------------------------------------------------------------------------

def self_test():
    """Covers the pure logic. The subprocess stages need a GPU and a capture."""
    assert peak_from_samples(["100", "250", "80"]) == 250
    assert peak_from_samples(["", "junk", "42"]) == 42
    assert peak_from_samples([]) is None
    assert peak_from_samples(["1197"]) == 1197

    ok = ("comment Vertical Axis: y\ncomment Handedness: left\n")
    assert unity_frame_ok(ok)
    assert not unity_frame_ok("comment Vertical Axis: z\ncomment Handedness: left\n")
    assert not unity_frame_ok("comment Handedness: right\ncomment Vertical Axis: y\n")
    assert not unity_frame_ok("")
    # Case and ordering must not matter; the tool's real output is mixed case.
    assert unity_frame_ok("COMMENT HANDEDNESS: LEFT\nCOMMENT VERTICAL AXIS: Y\n")

    meta = build_metadata(
        "scn_0001", "splat_unity.ply", 178333, 304, 0.369573, "marker",
        [0, 0, 0], [1, 1, 1], {"psnr": 31.37, "ssim": 0.956, "lpips": 0.103},
        7000, 2.28, 1197, "a" * 64, "2026-09-02T00:00:00Z")
    assert metadata_gaps(meta) == [], metadata_gaps(meta)
    assert meta["handedness"] == "left" and meta["upAxis"] == "y"

    # Every key in the committed sample must exist, or Unity/Web break silently.
    sample = json.loads((REPO / "docs/samples/metadata.example.json").read_text())
    missing = set(sample) - set(meta)
    assert not missing, f"metadata.json lost fields the sample declares: {missing}"

    # The gaps checker has to actually catch the failures it claims to.
    assert "unitScale is unset (scene is not metric)" in metadata_gaps(
        {**meta, "unitScale": None})
    assert "splatCount is zero" in metadata_gaps({**meta, "splatCount": 0})
    assert "metrics.psnr" in metadata_gaps({**meta, "metrics": {"ssim": 1, "lpips": 0}})
    assert "training.peakVramMb" in metadata_gaps(
        {**meta, "training": {"iterations": 7000, "minutes": 2.0, "peakVramMb": None}})

    row = results_row("2026-09-02", "1", "legion", 304, 100.0, 7000, 2.28, 1197,
                      {"psnr": 31.37, "ssim": 0.9559, "lpips": 0.1028},
                      178333, 43.0, "note")
    assert row.startswith("| 2026-09-02 | 1 | legion | 304 | 100% | 7000 |")
    assert "**1197**" in row and "31.37" in row
    assert row.count("|") == 15, row          # 14 columns
    # An unsampled run must say so rather than printing a misleading zero.
    assert "not sampled" in results_row(
        "d", "s", "m", 1, None, 7000, None, None, {}, 1, None, "")

    print("self-test OK")


def main():
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--video", help="source capture; required if starting at process")
    ap.add_argument("--scene", help="scene name, used for paths and the results row")
    ap.add_argument("--scene-id", help="sceneId for metadata.json (default scn_<scene>)")
    ap.add_argument("--dataset", help="default ~/datasets/<scene>")
    ap.add_argument("--outputs", default=str(REPO / "outputs"))
    ap.add_argument("--export-dir", help="default <repo>/exports/<scene>")
    ap.add_argument("--run-dir", help="where run artifacts land, default docs/results")
    ap.add_argument("--machine", default=os.uname().nodename)
    ap.add_argument("--min-registered", type=float, default=80.0,
                    help="registration floor in percent (docs/CAPTURE.md)")
    ap.add_argument("--max-spread", type=float, default=0.05,
                    help="unitScale relative spread ceiling, 0.05 = 5%%")
    ap.add_argument("--from", dest="start", choices=STAGES, default="preflight")
    ap.add_argument("--to", dest="end", choices=STAGES, default="results")
    ap.add_argument("--only", choices=STAGES)
    ap.add_argument("--allow-partial", action="store_true",
                    help="write an incomplete metadata.json instead of failing")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()

    if args.self_test:
        self_test()
        return 0
    if not args.scene:
        ap.error("--scene is required")

    scene = args.scene
    ctx = {
        "video": args.video, "scene": scene,
        "scene_id": args.scene_id or f"scn_{scene}",
        "dataset": args.dataset or str(Path.home() / "datasets" / scene),
        "outputs": args.outputs,
        "export_dir": Path(args.export_dir or REPO / "exports" / scene),
        "run_dir": Path(args.run_dir or REPO / "docs" / "results"),
        "machine": args.machine,
        "min_registered": args.min_registered,
        "max_spread": args.max_spread,
        "allow_partial": args.allow_partial,
    }
    ctx["export_dir"].mkdir(parents=True, exist_ok=True)
    ctx["run_dir"].mkdir(parents=True, exist_ok=True)

    if args.only:
        todo = [args.only]
    else:
        todo = STAGES[STAGES.index(args.start): STAGES.index(args.end) + 1]

    log(f"scene {scene}, stages: {' -> '.join(todo)}")
    if args.dry_run:
        for s in todo:
            log(f"would run {s}")
        return 0

    # Only meaningful when not starting from the top; a full run recomputes
    # everything and stale values from a previous scene would be misleading.
    if todo[0] != STAGES[0]:
        load_state(ctx)

    for name in todo:
        log(f"=== {name} ===")
        try:
            STAGE_FN[name](ctx)
            save_state(ctx)
        except Fail as e:
            print(f"\n[run_scene] STOPPED at {name}: {e}\n", file=sys.stderr)
            return 2
        except subprocess.CalledProcessError as e:
            print(f"\n[run_scene] STOPPED at {name}: command exited "
                  f"{e.returncode}\n", file=sys.stderr)
            return e.returncode or 1
        except FileNotFoundError as e:
            # A raw traceback here is not a readable reason. This is nearly
            # always the full-path-python trap from docs/STACK.md: the env's
            # bin/ is not on PATH, so the ns-* entry points do not exist.
            print(f"\n[run_scene] STOPPED at {name}: {e.filename} not found on PATH.\n"
                  "  Run inside an activated env (conda activate recon), not by a full\n"
                  "  path to the env's python -- the ns-* commands and ninja live in the\n"
                  "  env's bin/ and are only reachable when it is on PATH.\n"
                  "  Starting from 'preflight' would have caught this before any work.\n",
                  file=sys.stderr)
            return 2
    log("done")
    return 0


if __name__ == "__main__":
    sys.exit(main())
