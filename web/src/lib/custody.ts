import { createHash } from 'node:crypto'
import type { PrismaClient } from '@prisma/client'
import { prisma } from './prisma'

/**
 * Append-only audit log with a hash chain, per docs/API.md.
 *
 * Each row hashes its own contents plus the previous row's hash, so altering
 * any earlier row invalidates every hash after it. Two rules make this hold:
 *
 *  1. Rows are only ever appended. Never update or delete one.
 *  2. Appends are serialised, so two concurrent writes cannot both build on
 *     the same predecessor and fork the chain.
 */

/** First row's predecessor. 64 zeros so every prevHash is a fixed-width digest. */
export const GENESIS_HASH = '0'.repeat(64)

export type AuditInput = {
  userId?: string | null
  action: string
  targetType: string
  targetId: string
}

/**
 * The exact preimage that gets hashed. Field order and separator are part of
 * the format: change either and every existing row fails verification, so this
 * is effectively frozen once real evidence is logged.
 *
 * Timestamp is ISO-8601 with millisecond precision. Postgres `timestamp(3)`
 * matches that, so a value read back hashes identically to the one written.
 */
export function auditPreimage(row: {
  seq: number
  userId: string | null
  action: string
  targetType: string
  targetId: string
  timestamp: Date
  prevHash: string
}): string {
  return [
    row.seq,
    row.userId ?? '',
    row.action,
    row.targetType,
    row.targetId,
    row.timestamp.toISOString(),
    row.prevHash,
  ].join('|')
}

export function hashAuditRow(row: Parameters<typeof auditPreimage>[0]): string {
  return createHash('sha256').update(auditPreimage(row), 'utf8').digest('hex')
}

/** SHA-256 of an uploaded file. Custody starts at upload, not in November. */
export function sha256Buffer(buf: Buffer): string {
  return createHash('sha256').update(buf).digest('hex')
}

type TxClient = Omit<
  PrismaClient,
  '$connect' | '$disconnect' | '$on' | '$transaction' | '$use' | '$extends'
>

/**
 * Appends one row, inside a transaction that serialises against other appends.
 *
 * `seq` is an autoincrement column, but the chain needs the value *before*
 * hashing, so it is claimed explicitly rather than left to the default. The
 * advisory lock is what prevents two requests reading the same tail row and
 * writing sibling rows that both claim the same prevHash.
 */
export async function appendAudit(
  input: AuditInput,
  client: TxClient | PrismaClient = prisma
): Promise<{ seq: number; rowHash: string }> {
  const run = async (tx: TxClient) => {
    // Serialises appends across connections. Released when the transaction ends.
    await tx.$executeRaw`SELECT pg_advisory_xact_lock(hashtext('recon_audit_chain'))`

    const tail = await tx.auditLog.findFirst({
      orderBy: { seq: 'desc' },
      select: { seq: true, rowHash: true },
    })

    const seq = (tail?.seq ?? 0) + 1
    const prevHash = tail?.rowHash ?? GENESIS_HASH
    const timestamp = new Date()

    const rowHash = hashAuditRow({
      seq,
      userId: input.userId ?? null,
      action: input.action,
      targetType: input.targetType,
      targetId: input.targetId,
      timestamp,
      prevHash,
    })

    await tx.auditLog.create({
      data: {
        seq,
        userId: input.userId ?? null,
        action: input.action,
        targetType: input.targetType,
        targetId: input.targetId,
        timestamp,
        prevHash,
        rowHash,
      },
    })

    return { seq, rowHash }
  }

  // Already inside a transaction: reuse it, since opening a nested one would
  // deadlock on the advisory lock the outer transaction may already hold.
  if ('$transaction' in client && typeof client.$transaction === 'function') {
    return (client as PrismaClient).$transaction((tx) => run(tx as TxClient))
  }
  return run(client as TxClient)
}

export type VerifyResult =
  | { ok: true; rows: number }
  | {
      ok: false
      rows: number
      /** First row that fails, which is where tampering starts. */
      failedAtSeq: number
      reason: 'hash-mismatch' | 'chain-break' | 'sequence-gap'
    }

/**
 * Walks the chain from the genesis row and recomputes every hash.
 *
 * Distinguishes three failures because they mean different things: a row edited
 * in place (hash-mismatch), a row deleted or reordered (chain-break), and a
 * missing seq (sequence-gap).
 */
export async function verifyCustodyChain(
  client: PrismaClient | TxClient = prisma
): Promise<VerifyResult> {
  const rows = await client.auditLog.findMany({
    orderBy: { seq: 'asc' },
    select: {
      seq: true,
      userId: true,
      action: true,
      targetType: true,
      targetId: true,
      timestamp: true,
      prevHash: true,
      rowHash: true,
    },
  })

  let prevHash = GENESIS_HASH
  let expectedSeq = 1

  for (const row of rows) {
    if (row.seq !== expectedSeq) {
      return { ok: false, rows: rows.length, failedAtSeq: row.seq, reason: 'sequence-gap' }
    }
    if (row.prevHash !== prevHash) {
      return { ok: false, rows: rows.length, failedAtSeq: row.seq, reason: 'chain-break' }
    }
    if (hashAuditRow(row) !== row.rowHash) {
      return { ok: false, rows: rows.length, failedAtSeq: row.seq, reason: 'hash-mismatch' }
    }
    prevHash = row.rowHash
    expectedSeq += 1
  }

  return { ok: true, rows: rows.length }
}
