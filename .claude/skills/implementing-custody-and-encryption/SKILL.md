---
name: implementing-custody-and-encryption
description: Use when implementing the audit log, hash chain, custody verification endpoint, SHA-256 of uploads, or AES-256-GCM encryption at rest, or when the verify endpoint gives false tamper alarms.
---

# Implementing custody and encryption

## Overview

Chain of custody is the forensic credibility of the whole system, and it is cheap to build: an append-only audit table where each row's hash covers the previous row's hash. Tamper with any row and every later hash stops verifying. The measurable (for Chapter 7): overhead per write, and verify time vs log length.

## The hash chain

The killer bug is non-deterministic serialization: hash a **canonical** string, never `JSON.stringify` of an object whose key order can change.

```ts
function rowHash(r: { userId: string; action: string; targetType: string;
                      targetId: string; timestamp: string; prevHash: string }) {
  // fixed field order, fixed timestamp format (ISO 8601 UTC), '|' separator
  const canonical = [r.userId, r.action, r.targetType, r.targetId, r.timestamp, r.prevHash].join("|");
  return createHash("sha256").update(canonical, "utf8").digest("hex");
}

// append: inside the SAME transaction as the action it records
const prev = await tx.auditLog.findFirst({ orderBy: { id: "desc" } });
const prevHash = prev?.rowHash ?? "0".repeat(64);            // genesis
await tx.auditLog.create({ data: { ...row, prevHash, rowHash: rowHash({ ...row, prevHash }) } });
```

- **Serialize appends.** Two concurrent writers reading the same `prev` fork the chain; the transaction plus a unique index on a monotonic sequence makes the second writer fail and retry.
- **Verify endpoint**: walk in insert order, recompute each hash, compare. Return the first bad row index. The unit test that matters: verify passes, then `UPDATE` one old row directly in the DB, verify now fails at exactly that row.
- Timestamps are stored as the exact string that was hashed. Reformatting on read is the classic false-alarm source.

## Upload integrity

SHA-256 the video **while streaming it to disk** at upload (hash the same bytes you store), record it on the Asset row and in the first audit entry for the case. The GPU worker re-hashes after download and refuses a mismatch. Same for the produced `.ply` on the way back.

## AES-256-GCM at rest

```ts
const iv = randomBytes(12);                                   // 96-bit IV, unique per encryption
const cipher = createCipheriv("aes-256-gcm", key, iv);        // key: 32 bytes from env, not in git
const enc = Buffer.concat([cipher.update(plain), cipher.final()]);
const stored = Buffer.concat([iv, cipher.getAuthTag(), enc]); // iv ‖ tag ‖ ciphertext
```

- **Never reuse an IV under the same key**; random 12-byte IV per file/field is fine at this scale.
- GCM's auth tag means decryption fails loudly on tampering; treat that failure as a custody event, log it.
- Key management at FYP scope: one key per environment in `.env`, rotation documented as future work. Say exactly that in the report rather than pretending at HSMs.
- Encrypt the stored asset and sensitive columns; hash first, then encrypt (the custody hash is of the plaintext bytes the investigator uploaded).

## Order of operations for one upload

1. Stream to disk, hashing as bytes pass.
2. Write Asset row (sha256, size) + audit entry, one transaction.
3. Encrypt at rest (if enabled for the deployment).
4. Enqueue job. The audit trail starts before any processing touches the file.
