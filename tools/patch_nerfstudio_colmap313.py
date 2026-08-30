#!/usr/bin/env python3
"""Make Nerfstudio 1.1.5 drive COLMAP 3.13's renamed CLI options.

COLMAP 3.13 renamed the SIFT option groups that Nerfstudio 1.1.5 still emits:

    --SiftExtraction.use_gpu  ->  --FeatureExtraction.use_gpu
    --SiftMatching.use_gpu    ->  --FeatureMatching.use_gpu

Unpatched, `ns-process-data` dies with:
    Failed to parse options - unrecognised option '--SiftExtraction.use_gpu'

The fix edits the installed package, so it does NOT survive rebuilding the env or
reinstalling nerfstudio. Re-run this script after either. Idempotent, and it
reports whether it changed anything.

Usage:  python tools/patch_nerfstudio_colmap313.py [--check]
"""
import argparse
import importlib.util
import pathlib
import sys

RENAMES = {
    "--SiftExtraction.use_gpu": "--FeatureExtraction.use_gpu",
    "--SiftMatching.use_gpu": "--FeatureMatching.use_gpu",
}


def target_file() -> pathlib.Path:
    spec = importlib.util.find_spec("nerfstudio.process_data.colmap_utils")
    if spec is None or spec.origin is None:
        sys.exit("nerfstudio is not importable; activate the recon env first")
    return pathlib.Path(spec.origin)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="report status only, change nothing (exit 1 if unpatched)")
    args = ap.parse_args()

    path = target_file()
    text = path.read_text()
    stale = {old: text.count(old) for old in RENAMES if old in text}
    already = all(new in text for new in RENAMES.values())

    if not stale:
        print(f"OK: {path} already uses the COLMAP 3.13 option names"
              if already else f"OK: no stale option names in {path}")
        return 0

    if args.check:
        print(f"UNPATCHED: {path} still emits {sorted(stale)}")
        return 1

    for old, new in RENAMES.items():
        text = text.replace(old, new)
    path.write_text(text)
    print(f"patched {path}: " + ", ".join(f"{o} -> {RENAMES[o]} ({n}x)"
                                          for o, n in stale.items()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
