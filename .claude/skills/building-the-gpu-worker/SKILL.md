---
name: building-the-gpu-worker
description: Use when writing the Python worker that runs COLMAP and splatfacto, wiring it to the queue, capturing training metrics, or when a worker hangs, double-claims, or reports jobs FAILED with no useful reason.
---

# Building the GPU worker

## Overview

A Python process on each CUDA machine: claim a job, download the video, run the reconstruction as subprocesses, upload `.ply` plus `metadata.json`, report status. It must survive flaky networks, dying mid-job, and three machines running it at once.

## Queue integration, pick one and note it in STACK.md

1. **`bullmq` Python package** (official) so the worker consumes the same queue the web enqueues to. Confirm feature parity for stalled-job handling before relying on it. `verify`
2. **A thin Node shim** that owns the BullMQ worker and shells out to the Python pipeline. More moving parts, but queue behaviour is guaranteed identical to the docs.

Either way: concurrency 1 (one training run per GPU), and the claim is only real after the guarded Postgres transition (see building-the-job-pipeline).

## The pipeline stage runner

Every external tool goes through one wrapper: logged, time-limited, error-classified.

```python
class StageError(Exception):
    def __init__(self, code: str, detail: str): ...   # code is machine-readable

def run_stage(name: str, cmd: list[str], timeout_s: int, log_dir: Path) -> None:
    log = log_dir / f"{name}.log"
    with log.open("w") as f:
        try:
            subprocess.run(cmd, stdout=f, stderr=subprocess.STDOUT,
                           timeout=timeout_s, check=True)
        except subprocess.TimeoutExpired:
            raise StageError(f"{name.upper()}_TIMEOUT", f"exceeded {timeout_s}s, see {log}")
        except subprocess.CalledProcessError as e:
            raise StageError(f"{name.upper()}_FAILED", tail(log, 30))  # last lines, not "exit 1"
```

Stages: `process` (ns-process-data video), `train` (ns-train splatfacto), `eval`, `export`. Between `process` and `train`, **parse the COLMAP output and abort with `SFM_REGISTRATION_LOW` if under ~80 percent of frames registered**. That one check saves more GPU-hours than anything else in the worker.

## Error taxonomy

`FAILED` always carries a code plus human-readable detail: `SFM_REGISTRATION_LOW`, `CUDA_OOM` (grep the tail for "out of memory"), `TRAIN_TIMEOUT`, `UPLOAD_FAILED`, `MARKER_NOT_FOUND`. The dashboard shows these to a human who did not run the job; "exit code 1" is a bug in the worker, not an error message.

## Metrics capture

- Peak VRAM: sample `nvidia-smi --query-gpu=memory.used --format=csv,noheader` every few seconds in a background thread during `train`; record the max.
- PSNR/SSIM/LPIPS: `ns-eval` writes JSON; parse it, never scrape stdout.
- Wall-clock per stage. All of it goes into `metadata.json` (`training` block) and `docs/RESULTS.md`.

## Hard-won rules

| Rule | Why |
|---|---|
| Wipe the job's output dir at claim time | Retries must be idempotent, half-written outputs poison the next run |
| Work under the Linux filesystem, `~/jobs/<jobId>/` | `/mnt/c` I/O makes everything crawl |
| All HTTP to the web API retries with backoff (`tenacity`) | Three machines, three flaky home networks |
| Upload result, then flip status, in that order | READY with a missing asset is worse than a late READY |
| Log the exact command line for every stage | "It worked on my machine" becomes diffable |
