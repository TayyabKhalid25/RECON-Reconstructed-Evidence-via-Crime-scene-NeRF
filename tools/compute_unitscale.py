#!/usr/bin/env python3
"""Compute unitScale by finding the printed marker in the reconstruction.

docs/FRAMES.md, Scale: every capture includes the printed marker of known
physical size at the scene origin; after reconstruction we detect it, compute
metres per scene unit, and write unitScale with scaleMethod "marker".

Method
------
1. Detect the marker's solid black border in each frame (OpenCV): threshold,
   find contours, keep the largest 4-gon that is convex, roughly square, and
   contains a high proportion of dark pixels in a ring just inside its edge.
2. Take the detected corners in the two views where the marker is largest and
   most square, and triangulate each corner with the COLMAP poses and
   intrinsics from transforms.json.
3. Measure the four edges and two diagonals of the triangulated quad in scene
   units, and derive unitScale = 0.170 m / edge.

Refusing to guess
-----------------
A wrong scale is worse than no scale: 0.0 makes the Unity client refuse the
scene loudly, while a wrong value yields confident, incorrect measurements.
So the tool fails unless the geometry is self-consistent: the four edges must
agree with each other, and each diagonal must be sqrt(2) times the edge, both
within --tol. It reports the spread as a stated uncertainty.

Usage
  python tools/compute_unitscale.py --data ~/datasets/1 [--marker-m 0.170]
                                    [--tol 0.12] [--debug-dir DIR]
  python tools/compute_unitscale.py --self-test
"""
from __future__ import annotations

import argparse
import itertools
import json
import pathlib
import sys

import numpy as np

try:
    import cv2
except ImportError:
    cv2 = None


# ---------------------------------------------------------------- detection

def order_corners(pts: np.ndarray) -> np.ndarray:
    """Return the 4 points ordered tl, tr, br, bl."""
    c = pts.mean(0)
    ang = np.arctan2(pts[:, 1] - c[1], pts[:, 0] - c[0])
    p = pts[np.argsort(ang)]
    start = np.argmin(p.sum(1))          # top-left has the smallest x+y
    return np.roll(p, -start, axis=0)


def quad_scores(quad: np.ndarray):
    """Side lengths, squareness in [0,1], and area of an ordered quad."""
    s = np.array([np.linalg.norm(quad[(i + 1) % 4] - quad[i]) for i in range(4)])
    if s.min() <= 0:
        return s, 0.0, 0.0
    squareness = s.min() / s.max()
    area = 0.5 * abs(sum(quad[i][0] * quad[(i + 1) % 4][1]
                         - quad[(i + 1) % 4][0] * quad[i][1] for i in range(4)))
    return s, squareness, area


def detect_marker(img_bgr, min_squareness=0.72, min_area_frac=0.0015):
    """Find the marker's black border. Returns ordered corners or None.

    The marker is a solid black ring around a busy interior, printed on white
    paper. The discriminator is that ring: a candidate 4-gon must be dark in a
    band just inside its outline, which rejects the paper sheet itself, the
    tabletop, floor tiles and the phone.
    """
    if cv2 is None:
        raise RuntimeError("OpenCV required for detection")
    h, w = img_bgr.shape[:2]
    grey = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2GRAY)
    grey = cv2.GaussianBlur(grey, (5, 5), 0)
    best = None
    # several thresholds: lighting varies a lot across an orbit
    for thr in (60, 80, 100, 120, 140):
        _, bw = cv2.threshold(grey, thr, 255, cv2.THRESH_BINARY_INV)
        bw = cv2.morphologyEx(bw, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))
        cnts, _ = cv2.findContours(bw, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
        for c in cnts:
            peri = cv2.arcLength(c, True)
            ap = cv2.approxPolyDP(c, 0.02 * peri, True)
            if len(ap) != 4 or not cv2.isContourConvex(ap):
                continue
            quad = order_corners(ap.reshape(4, 2).astype(np.float64))
            sides, sq, area = quad_scores(quad)
            if sq < min_squareness or area < min_area_frac * w * h:
                continue
            # the ring test: sample a band just inside the outline
            ctr = quad.mean(0)
            inner = ctr + (quad - ctr) * 0.90
            outer = ctr + (quad - ctr) * 0.99
            mo = np.zeros((h, w), np.uint8)
            mi = np.zeros((h, w), np.uint8)
            cv2.fillPoly(mo, [outer.astype(np.int32)], 255)
            cv2.fillPoly(mi, [inner.astype(np.int32)], 255)
            ring = cv2.bitwise_and(mo, cv2.bitwise_not(mi))
            if ring.sum() == 0:
                continue
            ring_mean = float(grey[ring > 0].mean())
            core = cv2.erode(mi, np.ones((9, 9), np.uint8))
            if core.sum() == 0:
                continue
            core_mean = float(grey[core > 0].mean())
            # dark ring, brighter core -> a border around content
            if ring_mean > 110 or core_mean - ring_mean < 25:
                continue
            score = area * sq
            if best is None or score > best["score"]:
                best = {"corners": quad, "score": score, "area": area,
                        "squareness": sq, "ring": ring_mean, "core": core_mean,
                        "thr": thr}
    return best


# ------------------------------------------------------------ triangulation

def projection_matrices(tj, name_a, name_b):
    """Build 3x4 projection matrices for two named frames from transforms.json."""
    K = np.array([[tj["fl_x"], 0, tj["cx"]],
                  [0, tj["fl_y"], tj["cy"]],
                  [0, 0, 1]], dtype=np.float64)
    out = {}
    for fr in tj["frames"]:
        nm = pathlib.Path(fr["file_path"]).name
        if nm not in (name_a, name_b):
            continue
        c2w = np.array(fr["transform_matrix"], dtype=np.float64)
        # transforms.json is OpenGL style (x right, y up, z back); flip to CV
        c2w = c2w.copy()
        c2w[:, 1] *= -1
        c2w[:, 2] *= -1
        w2c = np.linalg.inv(c2w)
        out[nm] = K @ w2c[:3, :]
    return out


def triangulate(P1, x1, P2, x2):
    """Linear triangulation (DLT) of one point seen in two views."""
    A = np.vstack([
        x1[0] * P1[2] - P1[0],
        x1[1] * P1[2] - P1[1],
        x2[0] * P2[2] - P2[0],
        x2[1] * P2[2] - P2[1],
    ])
    _, _, Vt = np.linalg.svd(A)
    X = Vt[-1]
    return X[:3] / X[3]


# ------------------------------------------------------------------- checks

def consistency(quad3d, tol):
    """Edge/diagonal agreement for a triangulated square. Returns (ok, report)."""
    e = np.array([np.linalg.norm(quad3d[(i + 1) % 4] - quad3d[i]) for i in range(4)])
    d = np.array([np.linalg.norm(quad3d[2] - quad3d[0]),
                  np.linalg.norm(quad3d[3] - quad3d[1])])
    edge = float(e.mean())
    rep = {
        "edges": [round(float(v), 5) for v in e],
        "edge_mean": round(edge, 5),
        "edge_spread": round(float(e.std() / edge), 4) if edge else None,
        "diagonals": [round(float(v), 5) for v in d],
        "diag_over_edge": [round(float(v / edge), 4) for v in d] if edge else None,
    }
    if edge <= 0:
        return False, rep | {"why": "degenerate quad"}
    if e.std() / edge > tol:
        return False, rep | {"why": f"edges disagree by {e.std()/edge:.1%} > tol {tol:.0%}"}
    bad = [r for r in (d / edge) if abs(r - np.sqrt(2)) / np.sqrt(2) > tol]
    if bad:
        return False, rep | {"why": f"diagonal/edge ratio {bad} not sqrt(2) within {tol:.0%}"}
    return True, rep


# ---------------------------------------------------------------- self-test

def self_test():
    # a synthetic 0.5-unit square, two cameras, known geometry
    sq = np.array([[0, 0, 0], [0.5, 0, 0], [0.5, 0.5, 0], [0, 0.5, 0]], float)
    K = np.array([[800, 0, 320], [0, 800, 240], [0, 0, 1]], float)

    def cam(eye, tgt=np.zeros(3)):
        f = tgt - eye; f /= np.linalg.norm(f)
        r = np.cross([0, 0, 1.0], f); r /= np.linalg.norm(r)
        u = np.cross(f, r)
        R = np.vstack([r, u, f])
        return K @ np.hstack([R, (-R @ eye).reshape(3, 1)])

    P1, P2 = cam(np.array([1.2, -1.4, 1.0])), cam(np.array([-1.1, -1.5, 1.2]))

    def proj(P, X):
        x = P @ np.append(X, 1.0)
        return x[:2] / x[2]

    tri = np.array([triangulate(P1, proj(P1, X), P2, proj(P2, X)) for X in sq])
    assert np.allclose(tri, sq, atol=1e-6), f"triangulation wrong:\n{tri}"
    ok, rep = consistency(tri, 0.12)
    assert ok, rep
    assert abs(rep["edge_mean"] - 0.5) < 1e-6, rep
    us = 0.170 / rep["edge_mean"]
    assert abs(us - 0.34) < 1e-6, us
    print(f"triangulation exact; edge {rep['edge_mean']:.6f}; unitScale {us:.6f}")

    # a deliberately skewed quad must be rejected, not scaled
    bad = tri.copy(); bad[2] += [0.4, 0.0, 0.0]
    ok2, rep2 = consistency(bad, 0.12)
    assert not ok2, "skewed quad wrongly accepted"
    print("skewed quad rejected:", rep2["why"])

    # corner ordering is rotation invariant
    for k in range(4):
        assert np.allclose(order_corners(np.roll(sq[:, :2], k, axis=0)),
                           order_corners(sq[:, :2])), "ordering unstable"
    print("corner ordering stable under rotation")
    print("self-test: OK")
    return 0


# -------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", type=pathlib.Path)
    ap.add_argument("--marker-m", type=float, default=0.170,
                    help="marker outer black border edge, metres (docs/FRAMES.md)")
    ap.add_argument("--tol", type=float, default=0.12)
    ap.add_argument("--max-frames", type=int, default=304)
    ap.add_argument("--debug-dir", type=pathlib.Path)
    ap.add_argument("--json-out", type=pathlib.Path)
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if not a.data:
        ap.error("--data is required")
    if cv2 is None:
        sys.exit("OpenCV is required")

    tj = json.loads((a.data / "transforms.json").read_text())
    img_dir = a.data / "images"
    names = sorted(p.name for p in img_dir.glob("*.png"))[: a.max_frames]
    if a.debug_dir:
        a.debug_dir.mkdir(parents=True, exist_ok=True)

    dets = {}
    for i, nm in enumerate(names):
        img = cv2.imread(str(img_dir / nm))
        if img is None:
            continue
        d = detect_marker(img)
        if d:
            dets[nm] = d
            if a.debug_dir and len(dets) <= 12:
                vis = img.copy()
                cv2.polylines(vis, [d["corners"].astype(np.int32)], True, (0, 0, 255), 6)
                for j, c in enumerate(d["corners"]):
                    cv2.circle(vis, tuple(c.astype(int)), 12, (0, 255, 0), -1)
                    cv2.putText(vis, str(j), tuple(c.astype(int) + 16),
                                cv2.FONT_HERSHEY_SIMPLEX, 2, (255, 0, 0), 4)
                cv2.imwrite(str(a.debug_dir / f"det_{nm}"), vis)
        if (i + 1) % 50 == 0:
            print(f"  scanned {i+1}/{len(names)}, {len(dets)} detections", flush=True)

    print(f"detected the marker in {len(dets)}/{len(names)} frames")
    if len(dets) < 2:
        sys.exit("need the marker in at least 2 frames to triangulate; "
                 "detection is too weak on this capture")

    # rank by area*squareness, then try view pairs until one is self-consistent
    ranked = sorted(dets.items(), key=lambda kv: -kv[1]["score"])
    cand = ranked[: min(8, len(ranked))]
    results = []
    for (na, da), (nb, db) in itertools.combinations(cand, 2):
        Ps = projection_matrices(tj, na, nb)
        if na not in Ps or nb not in Ps:
            continue
        quad = np.array([triangulate(Ps[na], da["corners"][k],
                                    Ps[nb], db["corners"][k]) for k in range(4)])
        ok, rep = consistency(quad, a.tol)
        results.append({"views": [na, nb], "ok": ok, **rep})
        if ok:
            print(f"consistent pair {na} + {nb}: edge {rep['edge_mean']:.5f} units")

    good = [r for r in results if r["ok"]]
    if not good:
        print("\nno self-consistent view pair. Nearest attempts:")
        for r in sorted(results, key=lambda r: r.get("edge_spread") or 9)[:5]:
            print(f"  {r['views']}: {r.get('why')}")
        sys.exit("refusing to emit a unitScale that the geometry does not support")

    edges = np.array([r["edge_mean"] for r in good])
    edge = float(np.median(edges))
    unit = a.marker_m / edge
    spread = float(edges.std() / edge) if len(edges) > 1 else 0.0
    out = {
        "unitScale": round(unit, 6),
        "scaleMethod": "marker",
        "markerEdgeMetres": a.marker_m,
        "markerEdgeSceneUnits": round(edge, 6),
        "consistentPairs": len(good),
        "pairsTried": len(results),
        "framesDetected": len(dets),
        "framesScanned": len(names),
        "relativeSpread": round(spread, 4),
        "tolerance": a.tol,
    }
    print("\n" + json.dumps(out, indent=2))
    print(f"\n1 scene unit = {unit:.4f} m   (marker {a.marker_m} m = {edge:.4f} units)")
    if spread > 0.05:
        print(f"NOTE: {spread:.1%} spread across pairs. Treat as approximate; "
              "a closer capture of the marker would tighten it.")
    if a.json_out:
        a.json_out.write_text(json.dumps(out, indent=2) + "\n")
        print(f"wrote {a.json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
