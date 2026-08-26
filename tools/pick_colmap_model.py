#!/usr/bin/env python3
"""Make sure transforms.json is built from COLMAP's LARGEST sparse model.

The trap this exists for
------------------------
COLMAP's incremental mapper can emit several disconnected sub-reconstructions as
sparse/0, sparse/1, ... Nerfstudio reads sparse/0 unconditionally. If a small
stray model sorts first, ns-process-data reports something like

    Colmap matched 4 images
    COLMAP only found poses for 1.27% of the images. This is low.

while a complete reconstruction sits in sparse/1. It looks exactly like a failed
capture, and the honest-looking response - reshoot - wastes a good take. This
happened to scene 2: sparse/0 had 4 images, sparse/1 had all 316.

What this does
--------------
Counts registered images in every sparse/N, promotes the largest to sparse/0
(preserving the others), and regenerates transforms.json from it.

Usage
  python tools/pick_colmap_model.py ~/datasets/<scene>          # fix if needed
  python tools/pick_colmap_model.py ~/datasets/<scene> --check  # report only, exit 1 if wrong
  python tools/pick_colmap_model.py --self-test
"""
from __future__ import annotations

import argparse
import pathlib
import struct
import sys


def count_images(model_dir: pathlib.Path) -> int:
    """Registered image count from images.bin (or images.txt) without pycolmap."""
    b = model_dir / "images.bin"
    if b.exists():
        with open(b, "rb") as f:
            return struct.unpack("<Q", f.read(8))[0]
    t = model_dir / "images.txt"
    if t.exists():
        n = 0
        for line in t.read_text().splitlines():
            line = line.strip()
            if line and not line.startswith("#"):
                n += 1
        return n // 2          # two lines per image
    return -1


def models(sparse: pathlib.Path):
    out = []
    for d in sorted(sparse.iterdir()):
        if d.is_dir() and (d / "images.bin").exists() or (d / "images.txt").exists():
            out.append((d, count_images(d)))
    return out


def self_test() -> int:
    import tempfile
    with tempfile.TemporaryDirectory() as td:
        sp = pathlib.Path(td) / "sparse"
        for name, n in (("0", 4), ("1", 316), ("2", 12)):
            d = sp / name
            d.mkdir(parents=True)
            (d / "images.bin").write_bytes(struct.pack("<Q", n) + b"\x00" * 16)
        got = models(sp)
        assert [n for _, n in got] == [4, 316, 12], got
        best = max(got, key=lambda kv: kv[1])
        assert best[1] == 316 and best[0].name == "1", best
        assert count_images(sp / "0") == 4
        print("self-test: counted [4, 316, 12]; largest is sparse/1 -> would be promoted")
    print("self-test: OK")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("scene", nargs="?", type=pathlib.Path)
    ap.add_argument("--check", action="store_true",
                    help="report only; exit 1 if sparse/0 is not the largest model")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if not a.scene:
        ap.error("give a scene directory, or --self-test")

    scene = a.scene.expanduser()
    sparse = scene / "colmap" / "sparse"
    if not sparse.is_dir():
        sys.exit(f"no {sparse}")
    found = models(sparse)
    if not found:
        sys.exit(f"no sparse models under {sparse}")
    for d, n in found:
        print(f"  {d.name}: {n} registered images")
    best_dir, best_n = max(found, key=lambda kv: kv[1])
    total = len(list((scene / "images").glob("*"))) if (scene / "images").is_dir() else 0
    print(f"largest model: {best_dir.name} with {best_n} images"
          + (f" of {total} extracted ({100*best_n/total:.1f} %)" if total else ""))

    if len(found) == 1:
        print("single model, nothing to do")
        return 0
    if best_dir.name == "0":
        print("sparse/0 is already the largest, nothing to do")
        return 0

    print(f"\nsparse/0 has {count_images(sparse/'0')} images but sparse/{best_dir.name} "
          f"has {best_n} -- nerfstudio would read the wrong one")
    if a.check:
        return 1

    tmp = sparse / "_promote_tmp"
    (sparse / "0").rename(tmp)
    best_dir.rename(sparse / "0")
    tmp.rename(best_dir)
    print(f"promoted the {best_n}-image model to sparse/0 "
          f"(previous sparse/0 kept as sparse/{best_dir.name})")

    from nerfstudio.process_data.colmap_utils import colmap_to_json
    n = colmap_to_json(recon_dir=sparse / "0", output_dir=scene)
    print(f"regenerated transforms.json with {n} frames")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
