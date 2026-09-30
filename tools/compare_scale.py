"""Measure the table in both PLY files by scanning Z slabs for a 2:1 aspect ratio.

The table is 4ft × 2ft (2:1 ratio). We scan every Z height, extract
points in a thin slab, compute their trimmed XY extent, and find the
slab whose aspect ratio is closest to 2:1. That's the table surface.
"""
import numpy as np
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
TABLE_L_M = 4 * 0.3048  # 1.2192 m
TABLE_W_M = 2 * 0.3048  # 0.6096 m
TARGET_RATIO = TABLE_L_M / TABLE_W_M  # 2.0
UNIT_SCALE_MARKER = 0.369573


def parse_ply_xyz(path):
    with open(path, "rb") as f:
        header = b""
        while True:
            line = f.readline()
            header += line
            if line.strip() == b"end_header":
                break
        ht = header.decode()
        vcount = 0
        n_props = 0
        for ln in ht.splitlines():
            ln = ln.strip()
            if ln.startswith("element vertex"):
                vcount = int(ln.split()[2])
            if ln.startswith("property"):
                n_props += 1
        data = np.frombuffer(f.read(vcount * n_props * 4), dtype="<f4").reshape(vcount, n_props)
        return data[:, 0], data[:, 1], data[:, 2]


def find_table_slab(x, y, z, min_points=500):
    """Scan Z slabs and find the one with aspect ratio closest to 2:1."""
    z_lo, z_hi = np.percentile(z, [5, 95])
    n_slabs = 100
    slab_h = (z_hi - z_lo) / n_slabs * 2  # slab thickness = 2% of range

    best_score = float("inf")
    best = None

    for i in range(n_slabs):
        z_center = z_lo + (z_hi - z_lo) * i / n_slabs
        mask = np.abs(z - z_center) < slab_h
        count = np.sum(mask)
        if count < min_points:
            continue

        sx, sy = x[mask], y[mask]
        dx = np.percentile(sx, 95) - np.percentile(sx, 5)
        dy = np.percentile(sy, 95) - np.percentile(sy, 5)
        if min(dx, dy) < 0.1:
            continue

        ratio = max(dx, dy) / min(dx, dy)
        score = abs(ratio - TARGET_RATIO)

        if score < best_score:
            best_score = score
            length = max(dx, dy)
            width = min(dx, dy)
            best = {
                "z": z_center, "count": count,
                "length": length, "width": width,
                "ratio": ratio, "score": score
            }

    return best


print("=" * 64)
print("  RECON – Table Measurement (4ft × 2ft = 2:1 ratio search)")
print("=" * 64)

for label, ply_path, has_marker in [
    ("SCENE 1 (WITH MARKER)", REPO / "exports/1/splat.ply", True),
    ("TABLE-NOMARKER (NO MARKER)", REPO / "exports/table-nomarker/splat.ply", False),
]:
    print(f"\n{'─'*64}")
    print(f"  {label}")
    print(f"{'─'*64}")
    x, y, z = parse_ply_xyz(ply_path)
    print(f"  {len(x):,} splats loaded")

    result = find_table_slab(x, y, z)
    if result is None:
        print("  ✗ Could not find a slab with ~2:1 aspect ratio")
        continue

    r = result
    print(f"  Best table slab at Z = {r['z']:.4f}  ({r['count']:,} points)")
    print(f"  Extent: {r['length']:.4f} × {r['width']:.4f} su  (ratio {r['ratio']:.2f}:1)")

    if has_marker:
        l_m = r["length"] * UNIT_SCALE_MARKER
        w_m = r["width"] * UNIT_SCALE_MARKER
        l_err = abs(l_m - TABLE_L_M) / TABLE_L_M * 100
        w_err = abs(w_m - TABLE_W_M) / TABLE_W_M * 100
        print(f"\n  With marker scale ({UNIT_SCALE_MARKER}):")
        print(f"    Measured:  {l_m*100:.1f} cm × {w_m*100:.1f} cm")
        print(f"    Real:      {TABLE_L_M*100:.1f} cm × {TABLE_W_M*100:.1f} cm")
        print(f"    Error:     L={l_err:.1f}% ({abs(l_m-TABLE_L_M)*100:.1f} cm)  W={w_err:.1f}% ({abs(w_m-TABLE_W_M)*100:.1f} cm)")
        scene1_l_m = l_m
        scene1_w_m = w_m
    else:
        # Derive scale from known table
        scale_l = TABLE_L_M / r["length"]
        scale_w = TABLE_W_M / r["width"]
        print(f"\n  Implied unitScale (from length): {scale_l:.6f}")
        print(f"  Implied unitScale (from width):  {scale_w:.6f}")
        avg_scale = (scale_l + scale_w) / 2
        print(f"  Implied unitScale (average):     {avg_scale:.6f}")

        diff_pct = abs(UNIT_SCALE_MARKER - avg_scale) / UNIT_SCALE_MARKER * 100
        print(f"\n  vs marker scale ({UNIT_SCALE_MARKER}): {diff_pct:.1f}% different")
        print(f"  (This difference = COLMAP's arbitrary scale factor)")

print(f"\n{'═'*64}")
print(f"  CONCLUSION FOR REPORT")
print(f"{'═'*64}")
print(f"""
  The ArUco marker provides automatic, reproducible metric scale.
  Without it, SfM scale is arbitrary — different every reconstruction.
  For forensic distance measurements, the marker is essential.
""")
