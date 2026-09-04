#!/usr/bin/env python3
"""Shrink a 3DGS .ply toward a mobile budget, and say what it cost.

Two independent levers, because they trade against different things:

  --sh-degree D   Truncate spherical harmonics to degree D (0..3).
                  Costs view-dependent colour. Costs NO geometry and NO splats:
                  every splat survives at its exact position, scale and rotation,
                  so measured distances and the unitScale in docs/FRAMES.md are
                  untouched. This is the lever to reach for first.

  --max-splats N  Drop splats until at most N remain, lowest contribution first.
                  Costs geometry, so it is the lever that can move a measurement.
                  Per FTW-38 the value of N must come from a real device frame
                  rate (FTW-16/FTW-30), not from taste. There is deliberately no
                  default: this tool will not invent a budget.

Why SH first, measured on exports/1/splat_unity.ply (178333 splats, scene 1):

  the 45 f_rest_* floats are 72.6 % of the file but carry a small share of the
  radiance. Truncating all of them shifts colour by a mean of 0.015 in sRGB
  (about 3.9 levels of 255) across 400 random view directions, while cutting
  248 bytes/splat to 68 -- a 3.65x file reduction for no geometric change.

  keep deg2 (drop 3):  62 -> 41 props, 1.51x,  mean dC 2.4/255,  p99 12.8/255
  keep deg1:           62 -> 26 props, 2.38x,  mean dC 3.2/255,  p99 16.4/255
  deg0 only:           62 -> 17 props, 3.65x,  mean dC 3.9/255,  p99 19.2/255

  The p99 and max columns are the honest caveat: a minority of splats are
  strongly view-dependent (max excursion ~0.34, i.e. 87 levels) and those are
  typically specular highlights on glass or gloss. Mean error is what a viewer
  perceives across a room; p99 is what a shiny surface does. Numbers regenerate
  with --sh-report.

Ordering for --max-splats, when it is eventually calibrated. Contribution is
opacity * projected area, approximated view-independently as

    score = sigmoid(opacity) * (largest two of exp(scale)) product

i.e. opacity weighted by the splat's own cross-section. A large transparent
splat and a small opaque one can matter equally, which a pure opacity sort gets
wrong. Note splatfacto has already pruned by opacity at cull-alpha-thresh 0.1,
so on our exports an opacity-only cull removes almost nothing (1.3 % below 0.1)
-- that is why this is an area-weighted score and not a threshold.

Usage:
  python tools/decimate_splats.py IN.ply OUT.ply --sh-degree 0
  python tools/decimate_splats.py IN.ply OUT.ply --sh-degree 1 --max-splats 120000
  python tools/decimate_splats.py IN.ply --sh-report
  python tools/decimate_splats.py --self-test
"""
import argparse
import sys

import numpy as np

HEADER_END = b"end_header\n"

# Number of f_rest coefficients per colour channel, by SH degree.
COEFS_PER_CHANNEL = {0: 0, 1: 3, 2: 8, 3: 15}


def read_ply(path):
    """Read a binary little-endian all-float PLY. Mirrors convert_ply_to_unity."""
    with open(path, "rb") as f:
        raw = f.read()
    i = raw.find(HEADER_END)
    if i < 0:
        sys.exit(f"{path}: not a binary PLY with an end_header")
    header = raw[: i + len(HEADER_END)].decode("latin-1")
    body = raw[i + len(HEADER_END):]
    format_lines = [line for line in header.splitlines() if line.startswith("format ")]
    if not format_lines or format_lines[0] != "format binary_little_endian 1.0":
        sys.exit(f"{path}: only binary_little_endian 1.0 supported, got {format_lines[0] if format_lines else 'none'}")
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


def write_ply(path, names, arr, comments=()):
    hdr = ["ply", "format binary_little_endian 1.0"]
    hdr += [f"comment {c}" for c in comments]
    hdr.append(f"element vertex {arr.shape[0]}")
    hdr += [f"property float {n}" for n in names]
    hdr.append("end_header")
    with open(path, "wb") as f:
        f.write(("\n".join(hdr) + "\n").encode("latin-1"))
        f.write(arr.astype("<f4").tobytes())


def preserved_comments(header):
    """Keep the frame-convention comments; they are load bearing for Unity.

    docs/FRAMES.md fixes the convention on the GPU side, and Unity reads these
    lines to confirm it. Dropping them during decimation would silently strip
    the only in-file record that the file is already left handed and Y up.
    """
    keep = []
    for line in header.splitlines():
        if line.startswith("comment") and (
            "Vertical Axis" in line or "Handedness" in line or "Unity convention" in line
        ):
            keep.append(line[len("comment "):].strip())
    return keep


def truncate_sh(names, arr, degree):
    """Drop f_rest_* columns above `degree`. Returns (names, arr) with fewer cols.

    3DGS stores f_rest channel-major: all 15 coefficients for R, then G, then B.
    So keeping the first k coefficients per channel is a strided selection, not a
    prefix of the flat 45. Getting this wrong silently recolours the scene, which
    is the same class of bug the SH permutation comment in convert_ply_to_unity
    warns about.
    """
    if degree not in COEFS_PER_CHANNEL:
        sys.exit(f"--sh-degree must be one of {sorted(COEFS_PER_CHANNEL)}")
    rest = [n for n in names if n.startswith("f_rest_")]
    if not rest:
        return names, arr
    if len(rest) % 3:
        sys.exit(f"f_rest count {len(rest)} is not divisible by 3 channels")
    n_coef = len(rest) // 3
    keep_n = COEFS_PER_CHANNEL[degree]
    if keep_n >= n_coef:
        return names, arr           # already at or below the requested degree

    idx = {n: i for i, n in enumerate(names)}
    keep_cols, keep_names = [], []
    for c in range(3):
        for k in range(keep_n):
            flat = c * n_coef + k
            keep_cols.append(idx[f"f_rest_{flat}"])
            keep_names.append(flat)
    # Renumber sequentially so the file stays a valid 3DGS ply at the new degree.
    other = [(i, n) for i, n in enumerate(names) if not n.startswith("f_rest_")]
    new_names = [n for _, n in other] + [f"f_rest_{j}" for j in range(len(keep_cols))]
    new_arr = np.concatenate(
        [arr[:, [i for i, _ in other]], arr[:, keep_cols]], axis=1
    )
    return new_names, new_arr


def contribution(names, arr):
    """Per-splat score: opacity * cross-section, both in linear units."""
    idx = {n: i for i, n in enumerate(names)}
    alpha = 1.0 / (1.0 + np.exp(-arr[:, idx["opacity"]]))
    scales = np.exp(arr[:, [idx["scale_0"], idx["scale_1"], idx["scale_2"]]])
    # A 3D Gaussian's silhouette is widest across its two largest axes; the
    # smallest axis is the thickness we are looking through, not the area.
    two_largest = np.sort(scales, axis=1)[:, 1:]
    return alpha * two_largest.prod(axis=1)


def cap_splats(names, arr, max_splats):
    if max_splats is None or arr.shape[0] <= max_splats:
        return arr, 0
    score = contribution(names, arr)
    # argpartition is O(n) and we only need the top-k set, not its order.
    keep = np.argpartition(-score, max_splats - 1)[:max_splats]
    keep.sort()                      # preserve original file order for diffability
    return arr[keep], arr.shape[0] - max_splats


def sh_basis(dirs):
    """Real SH basis up to degree 3, in the coefficient order 3DGS uses."""
    x, y, z = dirs[:, 0], dirs[:, 1], dirs[:, 2]
    xx, yy, zz = x * x, y * y, z * z
    c1 = 0.4886025119029199
    c2 = [1.0925484305920792, -1.0925484305920792, 0.31539156525252005,
          -1.0925484305920792, 0.5462742152960396]
    c3 = [-0.5900435899266435, 2.890611442640554, -0.4570457994644658,
          0.3731763325901154, -0.4570457994644658, 1.445305721320277,
          -0.5900435899266435]
    b = np.zeros((dirs.shape[0], 15))
    b[:, 0], b[:, 1], b[:, 2] = -c1 * y, c1 * z, -c1 * x
    b[:, 3] = c2[0] * x * y
    b[:, 4] = c2[1] * y * z
    b[:, 5] = c2[2] * (2 * zz - xx - yy)
    b[:, 6] = c2[3] * x * z
    b[:, 7] = c2[4] * (xx - yy)
    b[:, 8] = c3[0] * y * (3 * xx - yy)
    b[:, 9] = c3[1] * x * y * z
    b[:, 10] = c3[2] * y * (4 * zz - xx - yy)
    b[:, 11] = c3[3] * z * (2 * zz - 3 * xx - 3 * yy)
    b[:, 12] = c3[4] * x * (4 * zz - xx - yy)
    b[:, 13] = c3[5] * z * (xx - yy)
    b[:, 14] = c3[6] * x * (xx - 3 * yy)
    return b


def sh_report(names, arr, n_views=400, sample=20000, seed=0):
    """Measure the colour cost of each SH truncation, in sRGB levels.

    Evaluates real radiance over random view directions rather than comparing
    coefficients, because coefficient magnitude alone does not tell you what a
    viewer sees: the basis functions have different peak amplitudes per degree.
    """
    idx = {n: i for i, n in enumerate(names)}
    rest = [n for n in names if n.startswith("f_rest_")]
    if len(rest) != 45:
        return None
    n = arr.shape[0]
    rng = np.random.default_rng(seed)
    dirs = rng.normal(size=(n_views, 3))
    dirs /= np.linalg.norm(dirs, axis=1, keepdims=True)
    basis = sh_basis(dirs)

    sub = rng.choice(n, min(sample, n), replace=False)
    dc = arr[np.ix_(sub, [idx[f"f_dc_{j}"] for j in range(3)])]
    coef = arr[np.ix_(sub, [idx[f"f_rest_{j}"] for j in range(45)])].reshape(-1, 3, 15)

    # 3DGS convention: stored colour is an offset around mid grey, Y0 = 0.2821.
    base = (0.5 + dc * 0.28209479)[:, None, :]
    full = np.clip(base + np.einsum("vk,nck->nvc", basis, coef), 0.0, 1.0)

    rows = []
    for degree in (2, 1, 0):
        trimmed = coef.copy()
        trimmed[:, :, COEFS_PER_CHANNEL[degree]:] = 0.0
        approx = np.clip(base + np.einsum("vk,nck->nvc", basis, trimmed), 0.0, 1.0)
        err = np.abs(approx - full)
        props = len(names) - 45 + 3 * COEFS_PER_CHANNEL[degree]
        rows.append({
            "degree": degree,
            "props": props,
            "ratio": len(names) / props,
            "mean": float(err.mean()),
            "p99": float(np.percentile(err, 99)),
            "max": float(err.max()),
        })
    return rows


def print_sh_report(rows, n_splats, n_props):
    print(f"SH truncation cost, {n_splats} splats, {n_props} properties "
          f"({n_splats * n_props * 4 / 1e6:.2f} MB)")
    print("Radiance error vs full degree 3, over 400 random view directions.")
    print(f"{'keep':>6} {'props':>6} {'MB':>7} {'shrink':>7} "
          f"{'mean dC':>12} {'p99 dC':>12} {'max dC':>12}")
    for r in rows:
        mb = n_splats * r["props"] * 4 / 1e6
        print(f"{('deg ' + str(r['degree'])):>6} {r['props']:>6} {mb:>7.2f} "
              f"{r['ratio']:>6.2f}x "
              f"{r['mean']:>7.5f} ({r['mean'] * 255:4.1f}) "
              f"{r['p99']:>7.5f} ({r['p99'] * 255:4.1f}) "
              f"{r['max']:>7.4f} ({r['max'] * 255:4.0f})")
    print("dC is sRGB 0-1; the value in brackets is 8-bit levels out of 255.")


def self_test():
    """Checks the two things that fail silently: SH striding and the cap order."""
    names = (["x", "y", "z", "opacity", "scale_0", "scale_1", "scale_2"]
             + [f"f_dc_{i}" for i in range(3)]
             + [f"f_rest_{i}" for i in range(45)])
    n = 6
    a = np.zeros((n, len(names)), dtype="<f4")
    base = names.index("f_rest_0")
    # Encode each coefficient as channel*100 + k so a mis-stride is visible.
    for c in range(3):
        for k in range(15):
            a[:, base + c * 15 + k] = c * 100 + k

    # deg 1 keeps coefficients 0,1,2 of each channel: 0,1,2,100,101,102,200,201,202
    nm1, a1 = truncate_sh(names, a, 1)
    got = list(a1[0, -9:])
    want = [0, 1, 2, 100, 101, 102, 200, 201, 202]
    assert got == want, f"deg1 stride wrong: {got} != {want}"
    assert len([x for x in nm1 if x.startswith("f_rest_")]) == 9
    assert nm1[-1] == "f_rest_8", f"renumbering wrong: {nm1[-1]}"

    # deg 2 keeps 8 per channel.
    nm2, a2 = truncate_sh(names, a, 2)
    got2 = list(a2[0, -24:])
    want2 = ([float(k) for k in range(8)] + [100.0 + k for k in range(8)]
             + [200.0 + k for k in range(8)])
    assert got2 == want2, f"deg2 stride wrong: {got2} != {want2}"

    # deg 0 drops every f_rest and leaves the rest of the columns alone.
    nm0, a0 = truncate_sh(names, a, 0)
    assert not any(x.startswith("f_rest_") for x in nm0)
    assert a0.shape[1] == len(names) - 45

    # Requesting a higher degree than present is a no-op, not an error.
    nm_same, a_same = truncate_sh(nm1, a1, 3)
    assert nm_same == nm1 and a_same.shape == a1.shape

    # Cap keeps the highest opacity*area splats and preserves file order.
    b = np.zeros((5, len(names)), dtype="<f4")
    b[:, names.index("opacity")] = [9.0, -9.0, 9.0, -9.0, 9.0]   # 1,0,1,0,1
    b[:, names.index("scale_0")] = 0.0                            # exp -> 1
    b[:, names.index("scale_1")] = 0.0
    b[:, names.index("scale_2")] = 0.0
    b[:, names.index("x")] = [10, 11, 12, 13, 14]
    kept, dropped = cap_splats(names, b, 3)
    assert dropped == 2, dropped
    assert list(kept[:, names.index("x")]) == [10, 12, 14], kept[:, names.index("x")]

    # A cap at or above the count changes nothing.
    kept2, dropped2 = cap_splats(names, b, 99)
    assert dropped2 == 0 and kept2.shape[0] == 5

    # Area matters, not just opacity: a big translucent splat beats a tiny opaque one.
    c = np.zeros((2, len(names)), dtype="<f4")
    c[:, names.index("opacity")] = [-2.0, 2.0]            # 0.12 vs 0.88
    c[0, names.index("scale_0"):names.index("scale_2") + 1] = np.log(10.0)
    c[1, names.index("scale_0"):names.index("scale_2") + 1] = np.log(0.01)
    c[:, names.index("x")] = [100, 200]
    kept3, _ = cap_splats(names, c, 1)
    assert kept3[0, names.index("x")] == 100, "area weighting not applied"

    # sh_report checks: zero f_rest must have 0 error at deg 0, 1, 2.
    zero_arr = np.zeros((10, len(names)), dtype="<f4")
    r_zero = sh_report(names, zero_arr, n_views=50, sample=10)
    assert r_zero is not None and len(r_zero) == 3
    for row in r_zero:
        assert row["mean"] == 0.0 and row["max"] == 0.0, f"expected 0 error for zeroed f_rest: {row}"

    # Degree-1 only coefficients: degree >= 1 must have 0 error, degree 0 must have non-zero error.
    deg1_arr = np.zeros((10, len(names)), dtype="<f4")
    base = names.index("f_rest_0")
    for ch in range(3):
        for k in range(3):
            deg1_arr[:, base + ch * 15 + k] = 1.0
    r_deg1 = sh_report(names, deg1_arr, n_views=50, sample=10)
    assert r_deg1 is not None
    # r_deg1 rows are for degree 2, 1, 0
    row_map = {row["degree"]: row for row in r_deg1}
    assert row_map[2]["mean"] == 0.0 and row_map[2]["max"] == 0.0, "deg2 error should be 0 on deg1 data"
    assert row_map[1]["mean"] == 0.0 and row_map[1]["max"] == 0.0, "deg1 error should be 0 on deg1 data"
    assert row_map[0]["mean"] > 0.0 and row_map[0]["max"] > 0.0, "deg0 error should be >0 on deg1 data"

    print("self-test OK")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("input", nargs="?")
    ap.add_argument("output", nargs="?")
    ap.add_argument("--sh-degree", type=int, default=None,
                    help="truncate spherical harmonics to this degree (0-3)")
    ap.add_argument("--max-splats", type=int, default=None,
                    help="cap splat count; must come from a measured device budget")
    ap.add_argument("--sh-report", action="store_true",
                    help="measure the colour cost of each truncation and exit")
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()

    if args.self_test:
        self_test()
        return
    if not args.input:
        ap.error("input is required")

    header, names, arr = read_ply(args.input)
    n_in, p_in = arr.shape[0], arr.shape[1]

    if args.sh_report:
        rows = sh_report(names, arr)
        if rows is None:
            sys.exit(f"{args.input}: needs 45 f_rest_* properties for an SH report")
        print_sh_report(rows, n_in, p_in)
        return

    if args.sh_degree is None and args.max_splats is None:
        ap.error("nothing to do: pass --sh-degree and/or --max-splats "
                 "(or --sh-report to see the trade)")
    if not args.output:
        ap.error("output is required when writing a decimated file")

    comments = preserved_comments(header)
    if args.sh_degree is not None:
        names, arr = truncate_sh(names, arr, args.sh_degree)
        comments.append(f"SH truncated to degree {args.sh_degree} "
                        f"by tools/decimate_splats.py")
    dropped = 0
    if args.max_splats is not None:
        arr, dropped = cap_splats(names, arr, args.max_splats)
        comments.append(f"Splats capped at {args.max_splats} "
                        f"by tools/decimate_splats.py")

    write_ply(args.output, names, arr, comments)

    bytes_in = n_in * p_in * 4
    bytes_out = arr.shape[0] * arr.shape[1] * 4
    print(f"in : {n_in:>8} splats x {p_in:>2} props = {bytes_in / 1e6:8.2f} MB")
    print(f"out: {arr.shape[0]:>8} splats x {arr.shape[1]:>2} props = "
          f"{bytes_out / 1e6:8.2f} MB   ({bytes_in / bytes_out:.2f}x smaller)")
    if dropped:
        print(f"dropped {dropped} splats ({100 * dropped / n_in:.2f}%) by "
              f"opacity x cross-section")
    if args.sh_degree is not None and args.sh_degree < 3:
        print("colour cost of the SH truncation: rerun with --sh-report on the "
              "input to quantify it")


if __name__ == "__main__":
    main()
