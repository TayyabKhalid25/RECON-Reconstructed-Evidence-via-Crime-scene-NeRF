/**
 * Custody chain checks that need no database.
 *
 * Verifies the two properties the report's tamper-evidence claim rests on:
 * an intact chain verifies, and any edit to an earlier row is detected at the
 * row where it happened.
 *
 *   npx tsx scripts/check-custody.ts
 */
import { GENESIS_HASH, auditPreimage, hashAuditRow } from '../src/lib/custody'

type Row = Parameters<typeof auditPreimage>[0] & { rowHash: string }

let failures = 0
function check(name: string, condition: boolean) {
  console.log(`${condition ? 'PASS' : 'FAIL'}  ${name}`)
  if (!condition) failures += 1
}

/** Builds a valid chain the same way appendAudit does. */
function buildChain(n: number): Row[] {
  const rows: Row[] = []
  let prevHash = GENESIS_HASH
  for (let seq = 1; seq <= n; seq += 1) {
    const base = {
      seq,
      userId: `user-${seq}`,
      action: 'job.create',
      targetType: 'Job',
      targetId: `job-${seq}`,
      // Fixed timestamps so the test is deterministic.
      timestamp: new Date(Date.UTC(2026, 8, 1, 12, 0, seq)),
      prevHash,
    }
    const rowHash = hashAuditRow(base)
    rows.push({ ...base, rowHash })
    prevHash = rowHash
  }
  return rows
}

/** Same walk as verifyCustodyChain, against in-memory rows. */
function verify(rows: Row[]): { ok: boolean; failedAtSeq?: number; reason?: string } {
  let prevHash = GENESIS_HASH
  let expectedSeq = 1
  for (const row of rows) {
    if (row.seq !== expectedSeq) return { ok: false, failedAtSeq: row.seq, reason: 'sequence-gap' }
    if (row.prevHash !== prevHash) return { ok: false, failedAtSeq: row.seq, reason: 'chain-break' }
    if (hashAuditRow(row) !== row.rowHash) {
      return { ok: false, failedAtSeq: row.seq, reason: 'hash-mismatch' }
    }
    prevHash = row.rowHash
    expectedSeq += 1
  }
  return { ok: true }
}

// 1. An intact chain verifies.
check('intact chain of 5 verifies', verify(buildChain(5)).ok)
check('empty chain verifies', verify([]).ok)

// 2. Editing a field in place is caught at that row. This is D12's acceptance
//    test: tamper one row in the database, verification must fail.
{
  const rows = buildChain(5)
  rows[2].action = 'job.delete'
  const r = verify(rows)
  check('edited action is detected', !r.ok && r.reason === 'hash-mismatch')
  check('edit is reported at the row it happened (seq 3)', r.failedAtSeq === 3)
}

// 3. Rewriting the row hash to match the edit still breaks, because the next
//    row's prevHash no longer matches. This is the property that makes the
//    chain worth having over per-row hashes.
{
  const rows = buildChain(5)
  rows[2].targetId = 'job-tampered'
  rows[2].rowHash = hashAuditRow(rows[2])
  const r = verify(rows)
  check('recomputing the edited row still breaks the chain', !r.ok && r.reason === 'chain-break')
  check('break surfaces at the following row (seq 4)', r.failedAtSeq === 4)
}

// 4. Deleting a row is caught, so history cannot be quietly shortened.
{
  const rows = buildChain(5)
  rows.splice(2, 1)
  const r = verify(rows)
  check('deleted row is detected', !r.ok)
  check('deletion surfaces as a sequence gap', r.reason === 'sequence-gap')
}

// 5. Timestamp round-trip. The hash covers an ISO-8601 string and JS Date only
//    carries milliseconds, which is why the column is Timestamp(3): at wider
//    precision a value read back would not rehash identically.
{
  const rows = buildChain(1)
  const roundTripped = { ...rows[0], timestamp: new Date(rows[0].timestamp.toISOString()) }
  check('millisecond timestamps round-trip through ISO-8601', hashAuditRow(roundTripped) === rows[0].rowHash)

  const microsecond = { ...rows[0], timestamp: new Date(rows[0].timestamp.getTime() + 0.4) }
  check(
    'sub-millisecond drift does not silently change the hash',
    hashAuditRow(microsecond) === rows[0].rowHash
  )
}

// 6. Genesis is fixed width, so every prevHash is a 64-char digest.
check('genesis hash is 64 hex chars', /^[0]{64}$/.test(GENESIS_HASH))
check('row hash is 64 hex chars', /^[0-9a-f]{64}$/.test(buildChain(1)[0].rowHash))

console.log(failures === 0 ? '\nall custody checks passed' : `\n${failures} check(s) FAILED`)
process.exit(failures === 0 ? 0 : 1)
