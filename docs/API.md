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
POST   /api/scenes/:id/anchor     store cloud anchor id and transform
GET    /api/scenes/:id/anchor     second device resolves the same anchor
```

The two anchor endpoints are how a second investigator sees the twin in the same physical
place. We store the anchor identifier plus the transform from anchor space to scene space, so
alignment survives across sessions and devices.

## metadata.json, written by the GPU worker

Unity cannot place a scene correctly without this. Every field exists because something breaks
without it. Machine-readable example: [samples/metadata.example.json](samples/metadata.example.json).

## Sign off

- [ ] All three of us have read the state machine and agree
- [ ] Sample metadata.json committed (done, see samples/)
- [ ] A sample .ply shared out of band so Web and Unity can build before Track A produces real
      output (.ply is gitignored, it goes over Tailscale or a drive link, not into the repo)
