#!/usr/bin/env python3
"""Turn a Gaussian splat .ply into a mesh PhysX can collide with.

Handbook Section 09: "Splats are not surfaces, so PhysX cannot collide with them
directly." Section 07 wants a "Poisson mesh export path working, for colliders",
and the handbook names the comparison of this mesh against the AR-plane baseline
as the project's research contribution -- it is what makes Challenge 1 ("how much
mesh approximation error is tolerable before trajectories diverge") answerable.

Two reasons to build this regardless of how the mobile splat question lands:
ballistics needs collision geometry either way, and it is also fallback option 3
in Section 15 (ship a mesh on mobile, keep splats for the dashboard).

FRAME AND SCALE, both easy to get silently wrong:

  Input must already be in the Unity convention, i.e. the output of
  convert_ply_to_unity.py. This tool does not convert anything and refuses a
  file that does not declare the convention, so a mesh can never be the thing
  that reintroduces a handedness flip.

  Output stays in SCENE UNITS, exactly like splat_unity.ply. Unity applies
  unitScale from metadata.json to both. Baking metres in here while Unity also
  scales is the double-scale bug, which presents as a modelling error rather
  than a unit error.

HOW THE SURFACE IS FOUND

Splat centres alone are a point cloud with no orientation, and Poisson needs
normals. The useful trick, from SuGaR and used by 3DGS-to-PC, is that a
well-converged 3D Gaussian tends to lie flat against the surface it represents,
so its shortest axis approximates the surface normal. We recover that axis from
the splat's rotation quaternion and per-axis scales, which is why this reads the
full .ply rather than just xyz.

Two methods, because they fail differently:

  poisson   (default) Open3D's screened Poisson. Watertight and smooth, which is
            what a mesh collider wants. Fabricates surface in unobserved regions,
            so it can close a doorway that was never scanned.
  voxel     Occupancy grid plus marching cubes, the approach PlayCanvas's
            splat-transform and gsplat-unity's mesh generator take for collision.
            Blockier, but it only ever produces surface where splats actually
            were, so it does not invent geometry.

Usage:
  python tools/splat_to_mesh.py exports/1/splat_unity.ply out.ply
  python tools/splat_to_mesh.py in.ply out.ply --method voxel --voxel 0.02
  python tools/splat_to_mesh.py in.ply out.ply --target-tris 40000 --report r.json
  python tools/splat_to_mesh.py --self-test
"""
import argparse
import json
import sys

import numpy as np

HEADER_END = b"end_header\n"


# --------------------------------------------------------------------------
# io, shared with the other tools' conventions
# --------------------------------------------------------------------------

def read_ply(path):
    """Binary little-endian all-float PLY. Same reader shape as the other tools."""
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


def require_unity_frame(header, path):
    """Refuse input that does not declare the Unity convention.

    docs/FRAMES.md fixes the conversion at exactly one place, GPU side, before
    export. A mesh built from an unconverted .ply would be mirrored, and a
    mirrored collider is worse than no collider: physics still runs, so nothing
    looks broken until impact points are quietly wrong.
    """
    h = header.lower()
    if "handedness: left" in h and "vertical axis: y" in h:
        return
    sys.exit(
        f"{path} does not declare the Unity frame (left handed, Y up).\n"
        "  Run tools/convert_ply_to_unity.py first. This tool deliberately does\n"
        "  not convert: docs/FRAMES.md fixes that at one place and this is not it."
    )


# --------------------------------------------------------------------------
# geometry, covered by --self-test
# --------------------------------------------------------------------------

def quat_to_matrix(q):
    """(w, x, y, z) rows -> (N, 3, 3) rotation matrices. Normalises defensively."""
    q = np.asarray(q, dtype=np.float64)
    n = np.linalg.norm(q, axis=1, keepdims=True)
    # A zero quaternion would divide by zero; treat it as identity rather than
    # producing NaNs that propagate into every normal downstream.
    n = np.where(n < 1e-12, 1.0, n)
    w, x, y, z = (q / n).T
    return np.stack([
        np.stack([1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)], -1),
        np.stack([2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)], -1),
        np.stack([2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)], -1),
    ], axis=1)


def splat_normals(scales_log, quats):
    """Surface normal per splat: the rotated shortest principal axis.

    A converged 3D Gaussian tends to lie flat against the surface it represents,
    so its thinnest direction approximates the surface normal (the SuGaR
    heuristic). Sign is arbitrary here and is resolved later by Open3D's
    consistent-tangent-plane orientation.
    """
    scales = np.exp(np.asarray(scales_log, dtype=np.float64))
    rot = quat_to_matrix(quats)
    shortest = np.argmin(scales, axis=1)
    # Column k of R is the world direction of local axis k.
    return rot[np.arange(rot.shape[0]), :, shortest]


def sigmoid(x):
    return 1.0 / (1.0 + np.exp(-np.asarray(x, dtype=np.float64)))


def select_splats(arr, names, min_opacity, max_extent_units):
    """Keep splats that plausibly sit on a surface.

    Two filters, both for the same reason: Poisson trusts every point it is
    given, so a floating low-opacity haze splat or a huge sky-sized blob drags
    surface toward it and produces a lumpy collider.
    """
    idx = {n: i for i, n in enumerate(names)}
    alpha = sigmoid(arr[:, idx["opacity"]])
    scales = np.exp(arr[:, [idx["scale_0"], idx["scale_1"], idx["scale_2"]]])
    keep = alpha >= min_opacity
    if max_extent_units is not None:
        keep &= scales.max(axis=1) <= max_extent_units
    return keep


def bbox(points):
    return points.min(axis=0), points.max(axis=0)


def hausdorff_ish(mesh_pts, splat_pts, sample=20000, seed=0):
    """One-sided nearest-neighbour distances from mesh vertices to splat centres.

    Not a true Hausdorff distance and not called one: it answers "did the surface
    stay where the splats are", which is the question that matters before a mesh
    becomes a collider. Reported as a distribution, because a mean alone hides
    the fabricated-surface failure Poisson is prone to.
    """
    from scipy.spatial import cKDTree
    rng = np.random.default_rng(seed)
    if mesh_pts.shape[0] > sample:
        mesh_pts = mesh_pts[rng.choice(mesh_pts.shape[0], sample, replace=False)]
    d, _ = cKDTree(splat_pts).query(mesh_pts, k=1)
    return {
        "mean": float(d.mean()),
        "median": float(np.median(d)),
        "p95": float(np.percentile(d, 95)),
        "max": float(d.max()),
    }


# --------------------------------------------------------------------------
# reconstruction
# --------------------------------------------------------------------------

def build_cloud(points, normals):
    import open3d as o3d
    pcd = o3d.geometry.PointCloud()
    pcd.points = o3d.utility.Vector3dVector(points)
    pcd.normals = o3d.utility.Vector3dVector(normals)
    # Splat normals have arbitrary sign; without this, Poisson sees a surface
    # whose inside and outside flip at random and returns noise.
    pcd.orient_normals_consistent_tangent_plane(k=15)
    return pcd


def reconstruct_poisson(pcd, depth, density_quantile):
    import open3d as o3d
    # n_threads=1 ensures deterministic, bit-reproducible Poisson accumulation.
    mesh, density = o3d.geometry.TriangleMesh.create_from_point_cloud_poisson(
        pcd, depth=depth, n_threads=1
    )
    if density_quantile > 0:
        # Poisson fabricates surface in unobserved regions and marks it with low
        # sample density. Trimming the lowest quantile removes the invented
        # geometry that would otherwise close a doorway nobody scanned.
        d = np.asarray(density)
        mesh.remove_vertices_by_mask(d < np.quantile(d, density_quantile))
    return mesh


def reconstruct_voxel(points, voxel):
    """Occupancy grid plus marching cubes. Never invents surface."""
    import open3d as o3d
    from skimage import measure

    lo, hi = bbox(points)
    dims = np.maximum(np.ceil((hi - lo) / voxel).astype(int) + 3, 4)
    if dims.prod() > 300_000_000:
        sys.exit(f"--voxel {voxel} needs a {dims.tolist()} grid, too large. "
                 "Use a bigger voxel size.")
    grid = np.zeros(dims, dtype=np.float32)
    ijk = np.floor((points - lo) / voxel).astype(int) + 1
    np.clip(ijk, 0, np.array(dims) - 1, out=ijk)
    np.add.at(grid, (ijk[:, 0], ijk[:, 1], ijk[:, 2]), 1.0)

    # Grid holds splat counts from np.add.at; isosurface at level=0.5 wraps populated
    # voxels. Small isolated voxel clusters are pruned by --min-cluster-tris downstream.
    verts, faces, _, _ = measure.marching_cubes(grid, level=0.5)
    verts = verts * voxel + lo - voxel      # undo the 1-voxel pad

    mesh = o3d.geometry.TriangleMesh()
    mesh.vertices = o3d.utility.Vector3dVector(verts)
    mesh.triangles = o3d.utility.Vector3iVector(faces)
    return mesh


def clean_and_decimate(mesh, target_tris, min_cluster_tris=100):
    """Remove reconstruction noise, then decimate. Returns (mesh, stats).

    The cluster filter is deliberately conservative and uses an ABSOLUTE
    threshold, not a fraction of the total. A relative threshold
    (`counts >= 0.001 * total`) looked reasonable and was badly wrong: a splat
    derived surface is genuinely fragmented -- walls, floor and furniture often
    do not connect -- so scaling the threshold with total size deleted real room
    surface and kept only the single largest blob. On scene 1 that silently cut
    the mesh from 24.7 x 15.5 x 23.5 scene units down to 8.0 x 4.7 x 7.7.

    A collider that covers a third of the room is worse than an obviously broken
    one: physics still runs and shots simply pass through the missing walls.
    """
    mesh.remove_degenerate_triangles()
    mesh.remove_duplicated_vertices()
    mesh.remove_duplicated_triangles()
    mesh.remove_non_manifold_edges()

    stats = {"clustersTotal": 0, "clustersRemoved": 0, "trianglesRemovedAsNoise": 0}
    labels, counts, _ = mesh.cluster_connected_triangles()
    labels = np.asarray(labels)
    counts = np.asarray(counts)
    stats["clustersTotal"] = int(counts.size)
    if counts.size > 1 and min_cluster_tris > 0:
        keep = counts >= min_cluster_tris
        stats["clustersRemoved"] = int((~keep).sum())
        stats["trianglesRemovedAsNoise"] = int(counts[~keep].sum())
        if keep.any():
            mesh.remove_triangles_by_mask(~keep[labels])
            mesh.remove_unreferenced_vertices()

    if target_tris and len(mesh.triangles) > target_tris:
        mesh = mesh.simplify_quadric_decimation(int(target_tris))
    mesh.compute_vertex_normals()
    return mesh, stats


def verify_mesh_ply(path):
    """Assert output mesh PLY header matches the expected binary layout."""
    with open(path, "rb") as f:
        raw = f.read(4096)
    idx = raw.find(HEADER_END)
    if idx < 0:
        sys.exit(f"{path}: missing end_header in generated PLY")
    header = raw[:idx + len(HEADER_END)].decode("latin-1")
    lines = header.splitlines()

    if not lines or lines[0] != "ply":
        sys.exit(f"{path}: header does not start with ply")
    if len(lines) < 2 or lines[1] != "format binary_little_endian 1.0":
        sys.exit(f"{path}: expected binary_little_endian 1.0, got {lines[1] if len(lines) > 1 else 'none'}")

    h_lower = header.lower()
    for req in ("vertical axis: y", "handedness: left", "units: scene units"):
        if req not in h_lower:
            sys.exit(f"{path}: header missing required comment: {req}")

    v_props = []
    face_prop = None
    in_vertex = False
    in_face = False
    for line in lines:
        if line.startswith("element vertex"):
            in_vertex = True
            in_face = False
        elif line.startswith("element face"):
            in_vertex = False
            in_face = True
        elif line.startswith("element "):
            in_vertex = False
            in_face = False
        elif line.startswith("property") and in_vertex:
            parts = line.split()
            v_props.append(parts[-1])
        elif line.startswith("property") and in_face:
            face_prop = line

    expected_v_props = ["x", "y", "z", "nx", "ny", "nz"]
    if v_props != expected_v_props:
        sys.exit(f"{path}: vertex properties {v_props} do not match expected {expected_v_props}")

    if not face_prop or "vertex_indices" not in face_prop:
        sys.exit(f"{path}: face property {face_prop} missing vertex_indices")


def write_mesh(path, mesh, comments):
    """Open3D writes no comments, so the header is rewritten to carry the frame.

    The frame convention travelling with the file is what stops someone
    importing this in six weeks and guessing.
    """
    import open3d as o3d
    path_str = str(path)
    if not path_str.endswith(".ply"):
        sys.exit(f"Output must be a .ply file, got {path_str}")

    o3d.io.write_triangle_mesh(path_str, mesh, write_ascii=False,
                               write_vertex_normals=True)
    with open(path_str, "rb") as f:
        raw = f.read()
    i = raw.find(b"format ")
    j = raw.find(b"\n", i)
    if i < 0 or j < 0:
        sys.exit(f"{path_str}: failed to find format line in Open3D PLY output")
    injected = b"".join(f"comment {c}\n".encode("latin-1") for c in comments)
    with open(path_str, "wb") as f:
        f.write(raw[: j + 1] + injected + raw[j + 1:])

    verify_mesh_ply(path_str)


def sha256_file(path):
    import hashlib
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()


# --------------------------------------------------------------------------

def self_test():
    import tempfile

    # Identity quaternion must give the identity matrix.
    r = quat_to_matrix([[1, 0, 0, 0]])[0]
    assert np.allclose(r, np.eye(3)), r

    # 180 degrees about Y flips x and z.
    r = quat_to_matrix([[0, 0, 1, 0]])[0]
    assert np.allclose(r @ [1, 0, 0], [-1, 0, 0], atol=1e-9)
    assert np.allclose(r @ [0, 1, 0], [0, 1, 0], atol=1e-9)

    # A zero quaternion is treated as identity rather than producing NaNs.
    assert np.isfinite(quat_to_matrix([[0, 0, 0, 0]])).all()

    # Rotations stay orthonormal, so normals keep unit length.
    q = np.array([[0.5, 0.5, 0.5, 0.5], [0.9, 0.1, 0.2, 0.3]])
    for m in quat_to_matrix(q):
        assert np.allclose(m @ m.T, np.eye(3), atol=1e-9)

    # Normal is the shortest axis. Thin in local Y, unrotated -> normal is +/-Y.
    n = splat_normals([[0.0, np.log(0.001), 0.0]], [[1, 0, 0, 0]])[0]
    assert np.allclose(np.abs(n), [0, 1, 0], atol=1e-9), n

    # Same splat rotated 90 degrees about X: local Y becomes world Z.
    s2 = np.sin(np.pi / 4)
    n = splat_normals([[0.0, np.log(0.001), 0.0]], [[s2, s2, 0, 0]])[0]
    assert np.allclose(np.abs(n), [0, 0, 1], atol=1e-6), n

    # Thin in local X instead -> normal follows X.
    n = splat_normals([[np.log(0.001), 0.0, 0.0]], [[1, 0, 0, 0]])[0]
    assert np.allclose(np.abs(n), [1, 0, 0], atol=1e-9), n

    # Normals are unit length, which Poisson depends on.
    ns = splat_normals(np.log([[0.1, 0.02, 0.3]] * 4),
                       [[1, 0, 0, 0], [0, 1, 0, 0], [0.5, 0.5, 0.5, 0.5], [0, 0, 0, 1]])
    assert np.allclose(np.linalg.norm(ns, axis=1), 1.0, atol=1e-9)

    # Opacity and size filters.
    names = ["x", "y", "z", "opacity", "scale_0", "scale_1", "scale_2"]
    a = np.zeros((4, len(names)), dtype="<f4")
    a[:, 3] = [9.0, -9.0, 9.0, 9.0]                  # ~1, ~0, ~1, ~1
    a[:, 4:7] = np.log([[0.01] * 3, [0.01] * 3, [0.01] * 3, [5.0] * 3])
    keep = select_splats(a, names, 0.5, 1.0)
    assert list(keep) == [True, False, True, False], keep
    # No size cap keeps the big one.
    assert list(select_splats(a, names, 0.5, None)) == [True, False, True, True]

    # The frame gate accepts only a converted file.
    ok = "comment Vertical Axis: y\ncomment Handedness: left\n"
    require_unity_frame(ok, "t")            # must not exit
    for bad in ("", "comment Handedness: right\ncomment Vertical Axis: y\n",
                "comment Vertical Axis: z\ncomment Handedness: left\n"):
        try:
            require_unity_frame(bad, "t")
        except SystemExit:
            pass
        else:
            raise AssertionError(f"frame gate accepted {bad!r}")

    # verify_mesh_ply tests
    with tempfile.NamedTemporaryFile(suffix=".ply", mode="w", delete=False) as f:
        f.write("ply\nformat binary_little_endian 1.0\ncomment Vertical Axis: y\ncomment Handedness: left\ncomment Units: scene units\nelement vertex 3\nproperty double x\nproperty double y\nproperty double z\nproperty double nx\nproperty double ny\nproperty double nz\nelement face 1\nproperty list uchar uint vertex_indices\nend_header\n")
        tmp_ply = f.name
    verify_mesh_ply(tmp_ply)

    print("self-test OK")


def main():
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("input", nargs="?", help="a Unity-convention splat .ply")
    ap.add_argument("output", nargs="?", help=".ply or .obj mesh")
    ap.add_argument("--method", choices=["poisson", "voxel"], default="poisson")
    ap.add_argument("--depth", type=int, default=9,
                    help="Poisson octree depth; 9 is room scale, 10 is slower and finer")
    ap.add_argument("--density-quantile", type=float, default=0.05,
                    help="trim this lowest quantile of Poisson sample density, "
                         "which is where it invents surface (0 disables)")
    ap.add_argument("--voxel", type=float, default=0.02,
                    help="voxel edge in SCENE UNITS for --method voxel")
    ap.add_argument("--min-opacity", type=float, default=0.3,
                    help="drop splats below this sigmoid opacity")
    ap.add_argument("--max-extent", type=float, default=0.5,
                    help="drop splats whose largest axis exceeds this, in scene "
                         "units (0 disables)")
    ap.add_argument("--min-cluster-tris", type=int, default=100,
                    help="drop disconnected shells smaller than this many "
                         "triangles (0 disables). Absolute, never a fraction of "
                         "the total: a splat surface is legitimately fragmented")
    ap.add_argument("--target-tris", type=int, default=50000,
                    help="decimate to about this many triangles; handbook asks "
                         "for tens of thousands for a mesh collider")
    ap.add_argument("--report", help="write a json report of the run")
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()

    if args.self_test:
        self_test()
        return 0
    if not args.input or not args.output:
        ap.error("input and output are required")

    header, names, arr = read_ply(args.input)
    require_unity_frame(header, args.input)

    idx = {n: i for i, n in enumerate(names)}
    for need in ("opacity", "scale_0", "rot_0"):
        if need not in idx:
            sys.exit(f"{args.input}: missing '{need}'. This needs a full 3DGS .ply, "
                     "not a decimated one with properties stripped.")

    pts_all = arr[:, [idx["x"], idx["y"], idx["z"]]].astype(np.float64)
    keep = select_splats(arr, names, args.min_opacity,
                         args.max_extent if args.max_extent > 0 else None)
    n_in = arr.shape[0]
    n_kept = int(keep.sum())
    if n_kept < 1000:
        sys.exit(f"only {n_kept} splats survived filtering (of {n_in}). "
                 "Lower --min-opacity or raise --max-extent.")
    print(f"[splat_to_mesh] {n_in} splats -> {n_kept} kept "
          f"({100 * n_kept / n_in:.1f}%) after opacity/size filtering")

    pts = pts_all[keep]
    scales = arr[np.ix_(keep, [idx["scale_0"], idx["scale_1"], idx["scale_2"]])]
    quats = arr[np.ix_(keep, [idx[f"rot_{i}"] for i in range(4)])]
    normals = splat_normals(scales, quats)

    if args.method == "poisson":
        print(f"[splat_to_mesh] poisson depth={args.depth} "
              f"(orienting {n_kept} normals first, this is the slow part)")
        mesh = reconstruct_poisson(build_cloud(pts, normals), args.depth,
                                   args.density_quantile)
    else:
        print(f"[splat_to_mesh] voxel marching cubes, {args.voxel} scene units")
        mesh = reconstruct_voxel(pts, args.voxel)

    raw_tris = len(mesh.triangles)
    mesh, clean_stats = clean_and_decimate(mesh, args.target_tris,
                                           args.min_cluster_tris)
    tris = len(mesh.triangles)
    verts = np.asarray(mesh.vertices)
    if tris == 0:
        sys.exit("reconstruction produced no triangles; try --method voxel "
                 "or a lower --min-opacity")

    smin, smax = bbox(pts)
    mmin, mmax = bbox(verts)
    dev = hausdorff_ish(verts, pts)

    print(f"[splat_to_mesh] {raw_tris} -> {tris} triangles, "
          f"{len(verts)} vertices, watertight={mesh.is_watertight()}")
    print(f"[splat_to_mesh] splat bbox {np.round(smax - smin, 3).tolist()}")
    print(f"[splat_to_mesh] mesh  bbox {np.round(mmax - mmin, 3).tolist()}")
    print(f"[splat_to_mesh] vertex-to-splat distance, scene units: "
          f"median {dev['median']:.4f}  p95 {dev['p95']:.4f}  max {dev['max']:.4f}")
    print(f"[splat_to_mesh] clusters {clean_stats['clustersTotal']}, removed "
          f"{clean_stats['clustersRemoved']} as noise "
          f"({clean_stats['trianglesRemovedAsNoise']} triangles)")

    # A collider that quietly covers part of the room is the dangerous failure:
    # physics still runs and shots pass through whatever is missing. Say so.
    coverage = np.divide(mmax - mmin, np.where(smax - smin == 0, 1, smax - smin))
    if coverage.min() < 0.9:
        print(f"[splat_to_mesh] NOTE: mesh spans "
              f"{np.round(100 * coverage, 1).tolist()} percent of the splat "
              f"bounding box per axis.")
        if args.method == "poisson" and args.density_quantile > 0:
            # Expected here rather than alarming: the density trim exists to
            # delete surface Poisson invented at the sparse fringe, and the
            # fringe is exactly where the bounding box extremes are.
            print(f"  Some of this is intended: --density-quantile "
                  f"{args.density_quantile} trims the sparse fringe where "
                  "Poisson fabricates surface. Set it to 0 to see the untrimmed "
                  "extent.")
        print("  Whatever is missing has no collider, so shots pass through it. "
              "If the gap is real, lower --min-cluster-tris or --min-opacity.")

    write_mesh(args.output, mesh, [
        "Collider mesh from tools/splat_to_mesh.py",
        f"Method: {args.method}",
        "Vertical Axis: y",
        "Handedness: left",
        "Units: scene units, NOT metres. Apply unitScale from metadata.json",
    ])
    print(f"[splat_to_mesh] wrote {args.output}")

    if args.report:
        report = {
            "input": args.input, "output": args.output, "method": args.method,
            "sha256": sha256_file(args.output),
            "splatsIn": n_in, "splatsKept": n_kept,
            "minOpacity": args.min_opacity, "maxExtent": args.max_extent,
            "trianglesBeforeDecimation": raw_tris, "triangles": tris,
            "vertices": len(verts), "watertight": bool(mesh.is_watertight()),
            "triangleWinding": "unspecified (handled by MeshCollider for non-convex collision)",
            "splatBBox": {"min": smin.tolist(), "max": smax.tolist()},
            "meshBBox": {"min": mmin.tolist(), "max": mmax.tolist()},
            "vertexToSplatDistanceSceneUnits": dev,
            "coveragePerAxis": coverage.tolist(),
            "cleanup": clean_stats,
            "units": "scene units, apply unitScale from metadata.json",
            "frame": {"handedness": "left", "upAxis": "y"},
        }
        if args.method == "poisson":
            report["depth"] = args.depth
            report["densityQuantile"] = args.density_quantile
        else:
            report["voxel"] = args.voxel
        with open(args.report, "w") as f:
            json.dump(report, f, indent=2)
            f.write("\n")
        print(f"[splat_to_mesh] report -> {args.report}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
