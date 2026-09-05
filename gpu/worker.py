import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent


@dataclass(frozen=True)
class ReconstructionJob:
    job_id: str
    scene_id: str
    video_path: str


# config
REDIS_URL = os.getenv("REDIS_URL", "redis://localhost:6379")
WEB_API_URL = os.getenv("WEB_API_URL", "http://localhost:3000")
WORKER_NAME = os.getenv("WORKER_NAME", "legion")
WORKER_TOKEN = os.getenv("WORKER_TOKEN", "dev-only-worker-token")
JOBS_DIR = Path(os.getenv("JOBS_DIR", Path.home() / "jobs"))

JOBS_DIR.mkdir(parents=True, exist_ok=True)


def report_status(
    job_id: str,
    status: str,
    progress: int | None = None,
    error: str | None = None,
) -> bool:
    url = f"{WEB_API_URL}/api/jobs/{job_id}/status"
    payload = {"status": status}
    if progress is not None:
        payload["progress"] = progress
    if error is not None:
        payload["error"] = error

    data = json.dumps(payload).encode()
    req = urllib.request.Request(
        url,
        data=data,
        headers={"x-worker-token": WORKER_TOKEN, "Content-Type": "application/json"},
        method="PATCH",
    )

    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status == 200
    except (urllib.error.URLError, TimeoutError, OSError) as e:
        print(f"[worker] failed to report status: {e}")
        return False


def claim_job() -> ReconstructionJob | None:
    url = f"{WEB_API_URL}/api/jobs/next?worker={WORKER_NAME}"
    req = urllib.request.Request(url, headers={"x-worker-token": WORKER_TOKEN})
    try:
        with urllib.request.urlopen(req) as resp:
            if resp.status == 200:
                data = json.loads(resp.read().decode())
                job_data = data.get("job")
                if not job_data:
                    return None

                # Resolve the video path (relative to repo or storage)
                source_key = job_data.get("sourceKey")
                video_path = Path(REPO / "web" / "storage" / source_key) if source_key else ""

                return ReconstructionJob(
                    job_id=job_data["jobId"],
                    scene_id=job_data["sceneId"],
                    video_path=str(video_path),
                )
    except (urllib.error.URLError, TimeoutError, OSError, json.JSONDecodeError) as e:
        print(f"[worker] Error claiming job: {e}")
        return None


def upload_result(job_id: str, ply_path: Path, metadata_path: Path) -> bool:
    """Uploads splat_unity.ply and metadata.json to POST /api/jobs/:id/result."""
    url = f"{WEB_API_URL}/api/jobs/{job_id}/result"
    boundary = "----WebKitFormBoundaryReconWorker7MA4YWxk"

    body = []
    # 1. splat_unity.ply
    body.append(f"--{boundary}\r\n".encode())
    body.append(f'Content-Disposition: form-data; name="ply"; filename="{ply_path.name}"\r\n'.encode())
    body.append(b"Content-Type: application/octet-stream\r\n\r\n")
    body.append(ply_path.read_bytes())
    body.append(b"\r\n")

    # 2. metadata.json
    body.append(f"--{boundary}\r\n".encode())
    body.append(f'Content-Disposition: form-data; name="metadata"; filename="{metadata_path.name}"\r\n'.encode())
    body.append(b"Content-Type: application/json\r\n\r\n")
    body.append(metadata_path.read_bytes())
    body.append(f"\r\n--{boundary}--\r\n".encode())

    payload = b"".join(body)
    req = urllib.request.Request(
        url,
        data=payload,
        headers={
            "x-worker-token": WORKER_TOKEN,
            "Content-Type": f"multipart/form-data; boundary={boundary}",
        },
        method="POST",
    )

    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status == 200
    except (urllib.error.URLError, TimeoutError, OSError) as e:
        print(f"[worker] Failed to upload result: {e}")
        return False


def process_job(job: ReconstructionJob):
    """Executes the full 3D reconstruction pipeline on the GPU."""
    print("\n[worker] ========================================")
    print(f"[worker] Claimed Job: {job.job_id}")
    print(f"[worker] Scene: {job.scene_id}")
    print(f"[worker] Video: {job.video_path}")
    print("[worker] ========================================\n")

    video = Path(job.video_path)
    if not video.exists():
        err_msg = f"Source video not found: {job.video_path}"
        print(f"[worker] ERROR: {err_msg}")
        report_status(job.job_id, status="FAILED", error=err_msg)
        return

    export_dir = REPO / "exports" / job.scene_id
    export_dir.mkdir(parents=True, exist_ok=True)

    report_status(job.job_id, status="PROCESSING", progress=10)

    # Command to run the 11-stage pipeline
    cmd = [
        sys.executable,
        str(REPO / "gpu" / "run_scene.py"),
        "--video",
        str(video),
        "--scene",
        job.scene_id,
        "--export-dir",
        str(export_dir),
    ]

    print("[worker] Executing reconstruction pipeline...")
    try:
        res = subprocess.run(cmd, text=True, check=False)
        if res.returncode != 0:
            err_msg = f"Reconstruction failed with exit code {res.returncode}"
            print(f"[worker] {err_msg}")
            report_status(job.job_id, status="FAILED", error=err_msg)
            return

        ply_file = export_dir / "splat_unity.ply"
        meta_file = export_dir / "metadata.json"

        if not ply_file.exists() or not meta_file.exists():
            err_msg = "Pipeline completed but output files are missing"
            print(f"[worker] {err_msg}")
            report_status(job.job_id, status="FAILED", error=err_msg)
            return

        print("[worker] Pipeline finished successfully. Uploading assets...")
        if upload_result(job.job_id, ply_file, meta_file):
            print(f"[worker] Job {job.job_id} is READY!")
        else:
            report_status(job.job_id, status="FAILED", error="Asset upload to API failed")

    except Exception as e:  # noqa: BLE001
        print(f"[worker] Unexpected error: {e}")
        report_status(job.job_id, status="FAILED", error=str(e))


def self_test():
    """Validates worker configuration and API connectivity without waiting for a training run."""
    print("[self-test] Validating ReconstructionJob dataclass...")
    job = ReconstructionJob(job_id="test-job", scene_id="test-scene", video_path="/tmp/fake.mp4")
    assert job.job_id == "test-job"
    assert job.scene_id == "test-scene"
    assert job.video_path == "/tmp/fake.mp4"

    print("[self-test] Validating repository paths...")
    assert REPO.exists(), f"Repo root does not exist: {REPO}"
    assert (REPO / "gpu" / "run_scene.py").exists(), "gpu/run_scene.py not found"

    print(f"[self-test] Testing Web API connectivity ({WEB_API_URL})...")
    try:
        url = f"{WEB_API_URL}/api/jobs/next?worker=selftest"
        req = urllib.request.Request(url, headers={"x-worker-token": WORKER_TOKEN})
        with urllib.request.urlopen(req) as resp:
            assert resp.status == 200, f"Expected 200 from /api/jobs/next, got {resp.status}"
            data = json.loads(resp.read().decode())
            print(f"[self-test] API response OK: {data}")
    except (urllib.error.URLError, TimeoutError, OSError) as e:
        print(f"[self-test] Web API check warning (make sure web server is running): {e}")

    print("[self-test] self-test OK")


def main():
    if "--self-test" in sys.argv:
        self_test()
        return

    print("[worker] RECON GPU Worker starting...")
    print(f"[worker] Worker Name: {WORKER_NAME}")
    print(f"[worker] Web API: {WEB_API_URL}")
    print("[worker] Polling for jobs (Ctrl+C to stop)...")

    while True:
        try:
            job = claim_job()
            if job:
                process_job(job)
            else:
                time.sleep(3)
        except KeyboardInterrupt:
            print("\n[worker] Stopping worker...")
            break
        except Exception as e:  # noqa: BLE001
            print(f"[worker] Loop error: {e}")
            time.sleep(5)


if __name__ == "__main__":
    main()
