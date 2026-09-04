# The contract

This file is what makes three people in three places actually parallel. A change here needs
all three of us to know before it merges. Source of truth: handbook Section 06.

## Job state machine

Five states, one direction. Anything unexpected ends at `FAILED` with readable error text,
because a job that silently vanishes is the worst thing to debug.

```
PENDING  ->  PROCESSING  ->  READY
   |              |
   +----------->  FAILED     (with error text)
                  CANCELLED  (user asked to stop)
```

## Data model

Minimum viable, custody built in from day one. Add fields as needed, never remove the audit table.

```
Case      : one investigation, owns many scenes
Scene     : one capture, owns one reconstruction job
Job       : reconstruction work, status per the machine above
Asset     : an uploaded or produced file, with sha256 and byte size
Anchor    : where a scene sits in the real world. Provider anchor id plus the
            anchor-space-to-scene-space transform. Many per scene: a re-host
            appends, and the resolve endpoint returns the newest
User      : id, email, passwordHash, role enum
AuditLog  : append only. userId, action, targetType, targetId,
            timestamp, prevHash, rowHash
```

Each audit row hashes its own contents plus the previous row's hash, so tampering with any
earlier row breaks every hash after it. Cheap to build, tamper evident, and the per-write
overhead is a number we measure for the report.

## Endpoints

### Web, consumed by GPU workers

```
POST   /api/jobs                  create, returns { jobId }
GET    /api/jobs/next             worker claims one PENDING job
PATCH  /api/jobs/:id/status       { status, error?, progress? }
POST   /api/jobs/:id/result       attach .ply and metadata.json
```

With BullMQ the worker takes jobs off Redis rather than polling `/next`. The endpoint stays
anyway: it makes manual testing and recovery from a stuck queue trivial.

### Web, consumed by the Unity client

```
POST   /api/auth/login            returns a token
GET    /api/scenes?status=READY   list, newest first
GET    /api/scenes/:id            metadata plus asset URLs
GET    /api/scenes/:id/asset      the .ply, or a redirect to it
POST   /api/scenes/:id/anchor     store cloud anchor id and transform (ADMIN, INVESTIGATOR)
GET    /api/scenes/:id/anchor     second device resolves the anchor (ADMIN, INVESTIGATOR, VIEWER)
```

The two anchor endpoints are how a second investigator sees the twin in the same physical
place. We store the anchor identifier plus the transform from anchor space to scene space, so
alignment survives across sessions and devices.

**The transform is decomposed, not a matrix.**

`POST /api/scenes/:id/anchor` takes:
```json
{
  "anchorId": "ca_abc123",
  "provider": "arcore-cloud-anchors",
  "position": { "x": 1.5, "y": -2.0, "z": 0.25 },
  "rotation": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
  "scale": 1.0,
  "expiresAt": "2027-09-02T10:00:00Z",
  "ttlDays": 30,
  "deviceLabel": "pixel-7a"
}
```

And returns `201 Created` with the envelope:
```json
{
  "anchor": {
    "id": "anc_1",
    "anchorId": "ca_abc123",
    "provider": "arcore-cloud-anchors",
    "position": { "x": 1.5, "y": -2.0, "z": 0.25 },
    "rotation": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
    "scale": 1.0,
    "expiresAt": "2027-09-02T10:00:00.000Z",
    "expired": false,
    "deviceLabel": "pixel-7a",
    "createdAt": "2026-09-02T12:00:00.000Z",
    "frame": { "handedness": "left", "upAxis": "y", "space": "anchor->scene", "units": "metres" }
  }
}
```

`GET /api/scenes/:id/anchor` returns `200 OK` with the newest anchor in `{ "anchor": { ... } }`, or `{ "anchor": null }` if no anchor has been hosted for this scene yet (allowing clean fallback to marker/manual alignment).

A 16-float 4x4 matrix in JSON carries a silent row-major versus column-major trap — Unity's
`Matrix4x4` is column-major — and a transposed matrix misplaces the twin subtly rather than
failing visibly. That is the failure class `docs/FRAMES.md` exists to prevent, so the wire format
does not offer the opportunity.

Three things the server enforces so the client cannot store a scene it will then render wrongly:
the quaternion must be unit length (tolerance 1e-3, which absorbs honest float drift but rejects
an all-zero or unnormalised rotation), `scale` must be finite and positive (zero is an invisible
twin, negative is a mirrored one, and both read as broken tracking rather than a bad request), and
every coordinate must be finite. Stored rotations are defensively normalised.

`expired` is computed server side. A resolve that silently returns a dead Cloud Anchor looks
exactly like broken tracking on the phone, which is expensive to debug from the Unity end.
`expiresAt: null` means no expiry was recorded, not that it never expires.

`position` and anchor space are in **metres** (ARCore world space). `scale` here is the anchor-to-scene
fit and is normally 1. It is **not** `Scene.unitScale`, which converts scene units to metres and comes
from the marker. Applying one where the other belongs is the double-scale bug.

## metadata.json, written by the GPU worker

Unity cannot place a scene correctly without this. Every field exists because something breaks
without it. Machine-readable example: [samples/metadata.example.json](samples/metadata.example.json).

## Sign off

- [ ] All three of us have read the state machine and agree
- [ ] Sample metadata.json committed (done, see samples/)
- [ ] A sample .ply shared out of band so Web and Unity can build before Track A produces real
      output (.ply is gitignored, it goes over Tailscale or a drive link, not into the repo)
