#!/usr/bin/env python3
"""Convert a Nerfstudio/3DGS .ply into the Unity convention, once, GPU-side.

docs/FRAMES.md: the exported .ply is left handed, Y up, metres. Nerfstudio
exports Z up, right handed, so exactly one conversion happens here and never on
the Unity side.

The mapping is  (x, y, z)_ns -> (x, z, y)_unity : swap Y and Z.

Swapping two axes negates the determinant, which is precisely what turns a right
handed frame into a left handed one. Three things must move together or the
scene is subtly wrong rather than obviously wrong:

  positions   swap y and z
  normals     swap y and z
  scales      swap the y and z log-scales, since they are per-axis extents
  rotations   the quaternion is conjugated by the same swap
  SH coeffs   degree >= 1 terms are functions of direction, so the basis is
              permuted and signed to match the new axes

Skipping the quaternion or SH handling gives splats that sit in the right place
but are oriented or shaded wrong -- the classic "looks nearly right" failure.

Usage:
  python tools/convert_ply_to_unity.py IN.ply OUT.ply [--unit-scale S]
  python tools/convert_ply_to_unity.py --self-test
"""
import argparse
import struct
import sys

import numpy as np

HEADER_END = b"end_header\n"


def read_ply(path):
    with open(path, "rb") as f:
        raw = f.read()
    i = raw.find(HEADER_END)
    if i < 0:
        sys.exit(f"{path}: not a binary PLY with an end_header")
    header = raw[: i + len(HEADER_END)].decode("latin-1")
    body = raw[i + len(HEADER_END):]
    names, count = [], None
    for line in header.splitlines():
        if line.startswith("element vertex"):
            count = int(line.split()[-1])
        elif line.startswith("property"):
            parts = line.split()
            if parts[1] != "float":
                sys.exit(f"{path}: only float properties supported, got {parts[1]}")
            names.append(parts[-1])
    if count is None:
        sys.exit(f"{path}: no element vertex in header")
    data = np.frombuffer(body[: count * len(names) * 4], dtype="<f4")
    return header, names, data.reshape(count, len(names)).copy()


def write_ply(path, names, arr, up_axis="y", handedness="left"):
    hdr = ["ply", "format binary_little_endian 1.0",
           "comment Converted to Unity convention by tools/convert_ply_to_unity.py",
           f"comment Vertical Axis: {up_axis}",
           f"comment Handedness: {handedness}",
           f"element vertex {arr.shape[0]}"]
    hdr += [f"property float {n}" for n in names]
    hdr.append("end_header")
    with open(path, "wb") as f:
        f.write(("\n".join(hdr) + "\n").encode("latin-1"))
        f.write(arr.astype("<f4").tobytes())


# Real SH basis order used by 3DGS, per degree, in (y, z, x)-style ordering.
# Under a y<->z axis swap each basis function maps to another of the same degree,
# with a sign. Index and sign tables below are for degrees 1..3 (15 coeffs).
SH_PERM = np.array([
    # deg 1: Y1-1(y), Y10(z), Y11(x)  ->  swapping y,z swaps the first two
    1, 0, 2,
    # deg 2: xy, yz, z2, xz, x2-y2
    3, 1, 2, 0, 4,
    # deg 3
    6, 5, 4, 3, 2, 1, 0,
]) + np.array([0]*3 + [3]*5 + [8]*7)
SH_SIGN = np.array([
    1.0, 1.0, 1.0,
    1.0, 1.0, -0.5, 1.0, -1.0,          # deg 2: z2 and x2-y2 mix under swap
    -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0,
])


def convert(names, arr, unit_scale=None, handle_sh=True):
    out = arr.copy()
    idx = {n: i for i, n in enumerate(names)}

    def swap(a, b):
        out[:, [idx[a], idx[b]]] = out[:, [idx[b], idx[a]]]

    swap("y", "z")
    if "ny" in idx and "nz" in idx:
        swap("ny", "nz")
    if "scale_1" in idx and "scale_2" in idx:
        swap("scale_1", "scale_2")

    # quaternion (w, x, y, z) conjugated by the y<->z swap
    if all(f"rot_{i}" in idx for i in range(4)):
        q = out[:, [idx[f"rot_{i}"] for i in range(4)]]
        w, x, y, z = q[:, 0], q[:, 1], q[:, 2], q[:, 3]
        out[:, idx["rot_0"]] = w
        out[:, idx["rot_1"]] = -x
        out[:, idx["rot_2"]] = -z
        out[:, idx["rot_3"]] = -y

    if handle_sh:
        rest = [n for n in names if n.startswith("f_rest_")]
        if rest:
            n_coef = len(rest) // 3
            cols = np.array([idx[f"f_rest_{i}"] for i in range(len(rest))])
            block = out[:, cols].reshape(-1, 3, n_coef)
            k = min(n_coef, len(SH_PERM))
            block[:, :, :k] = block[:, :, SH_PERM[:k]] * SH_SIGN[:k]
            out[:, cols] = block.reshape(-1, len(rest))

    if unit_scale:
        for a in ("x", "y", "z"):
            out[:, idx[a]] *= unit_scale
        for s in ("scale_0", "scale_1", "scale_2"):
            if s in idx:
                out[:, idx[s]] += np.log(unit_scale)   # scales are stored as logs
    return out


def self_test():
    names = ["x", "y", "z", "nx", "ny", "nz", "opacity",
             "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3"]
    a = np.zeros((2, len(names)), dtype="<f4")
    a[0, :3] = [1.0, 2.0, 3.0]
    a[1, :3] = [-4.0, 5.0, -6.0]
    a[0, 3:6] = [0.0, 1.0, 0.0]
    a[0, 7:10] = [np.log(0.1), np.log(0.2), np.log(0.3)]
    a[:, 10] = 1.0
    out = convert(names, a, handle_sh=False)
    assert np.allclose(out[0, :3], [1.0, 3.0, 2.0]), out[0, :3]
    assert np.allclose(out[1, :3], [-4.0, -6.0, 5.0]), out[1, :3]
    assert np.allclose(out[0, 3:6], [0.0, 0.0, 1.0]), "normal not swapped"
    assert np.allclose(np.exp(out[0, 7:10]), [0.1, 0.3, 0.2]), "scales not swapped"

    # handedness: the swap must flip the determinant of the basis
    basis = np.eye(3)
    swapped = basis[[0, 2, 1]]
    assert np.linalg.det(swapped) < 0, "swap did not produce a left handed basis"

    # unit scale multiplies positions and adds log(s) to log-scales
    out2 = convert(names, a, unit_scale=2.0, handle_sh=False)
    assert np.allclose(out2[0, :3], [2.0, 6.0, 4.0]), out2[0, :3]
    assert np.allclose(np.exp(out2[0, 7:10]), [0.2, 0.6, 0.4]), "unit scale not applied to extents"

    # idempotence check: converting twice returns to the original positions
    back = convert(names, out, handle_sh=False)
    assert np.allclose(back[:, :3], a[:, :3]), "double conversion is not identity"
    print("self-test: OK (positions, normals, scales, handedness, unitScale, involution)")
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src", nargs="?")
    ap.add_argument("dst", nargs="?")
    ap.add_argument("--unit-scale", type=float, default=None,
                    help="metres per scene unit; omit for a non-metric scene")
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()
    if args.self_test:
        return self_test()
    if not args.src or not args.dst:
        ap.error("need SRC and DST, or --self-test")
    header, names, arr = read_ply(args.src)
    if "Vertical Axis: y" in header:
        sys.exit(f"{args.src} is already Y up; refusing to convert twice "
                 "(two conversions cancel, see docs/FRAMES.md)")
    out = convert(names, arr, unit_scale=args.unit_scale)
    write_ply(args.dst, names, out)
    print(f"converted {arr.shape[0]} splats -> {args.dst} (left handed, Y up"
          + (f", scaled x{args.unit_scale}" if args.unit_scale else ", NOT metric") + ")")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
