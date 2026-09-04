import { z } from 'zod'

/**
 * Validation and shaping for cloud anchors, shared by the two endpoints in
 * docs/API.md and by scripts/check-anchor.ts so there is one definition of
 * "a valid transform" rather than three.
 *
 * The transform is anchor space -> scene space. Scene space is already the
 * Unity convention (left handed, Y up) because the .ply is converted GPU side
 * per docs/FRAMES.md, so nothing here flips an axis and nothing downstream
 * should either.
 */

/**
 * How far a quaternion's length may drift from 1 before we reject it.
 *
 * Clients accumulate float error, so demanding exactly 1 would reject honest
 * input; 1e-3 is loose enough for a phone's pose and tight enough to catch the
 * failures that matter -- an all-zero quaternion, an unnormalised one, or
 * Euler angles sent in the wrong fields.
 */
export const QUAT_TOLERANCE = 1e-3

/** Finite guard: JSON allows no NaN, but a client can still send huge floats. */
const coord = z.number().finite()

export const AnchorInput = z
  .object({
    anchorId: z.string().min(1).max(512),
    provider: z.string().min(1).max(64).optional(),
    position: z.object({ x: coord, y: coord, z: coord }).strict(),
    rotation: z.object({ x: coord, y: coord, z: coord, w: coord }).strict(),
    // Normally 1. Rejecting zero and negatives because a zero-scale twin is
    // invisible and a negative one is mirrored, and both read as "the anchor
    // is broken" rather than "the request was wrong".
    scale: z.number().finite().positive().optional(),
    expiresAt: z.iso.datetime().optional(),
    ttlDays: z.number().int().positive().optional(),
    deviceLabel: z.string().max(128).optional(),
  })
  .strict()
  .refine((v) => isUnitQuaternion(v.rotation), {
    message:
      `rotation must be a unit quaternion (length 1 +/- ${QUAT_TOLERANCE}); ` +
      'a non-normalised quaternion skews the twin rather than failing visibly',
    path: ['rotation'],
  })

export type AnchorInputType = z.infer<typeof AnchorInput>

export function quaternionLength(q: { x: number; y: number; z: number; w: number }): number {
  return Math.sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w)
}

export function isUnitQuaternion(q: {
  x: number
  y: number
  z: number
  w: number
}): boolean {
  const len = quaternionLength(q)
  if (!Number.isFinite(len)) return false
  return Math.abs(len - 1) <= QUAT_TOLERANCE
}

/** Flat columns for Prisma. Normalises quaternion defensively on store. */
export function toAnchorColumns(input: AnchorInputType) {
  const len = quaternionLength(input.rotation)
  const normFactor = len > 1e-12 ? 1.0 / len : 1.0
  let expiresAt: Date | null = null
  if (input.expiresAt !== undefined) {
    expiresAt = new Date(input.expiresAt)
  } else if (input.ttlDays !== undefined) {
    expiresAt = new Date(Date.now() + input.ttlDays * 86400 * 1000)
  }

  return {
    anchorId: input.anchorId,
    ...(input.provider === undefined ? {} : { provider: input.provider }),
    posX: input.position.x,
    posY: input.position.y,
    posZ: input.position.z,
    rotX: input.rotation.x * normFactor,
    rotY: input.rotation.y * normFactor,
    rotZ: input.rotation.z * normFactor,
    rotW: input.rotation.w * normFactor,
    ...(input.scale === undefined ? {} : { scale: input.scale }),
    expiresAt,
    deviceLabel: input.deviceLabel ?? null,
  }
}

type AnchorRow = {
  id: string
  anchorId: string
  provider: string
  posX: number
  posY: number
  posZ: number
  rotX: number
  rotY: number
  rotZ: number
  rotW: number
  scale: number
  expiresAt: Date | null
  deviceLabel: string | null
  createdAt: Date
}

/**
 * Response shape for both endpoints: nested, so the client reads
 * `position`/`rotation` rather than eleven flat floats it has to reassemble in
 * the right order.
 *
 * `expired` is computed rather than left to the client. A resolve that silently
 * returns a dead anchor looks like broken tracking on the phone, which is an
 * expensive thing to debug from the Unity end.
 */
export function serialiseAnchor(row: AnchorRow, now: Date = new Date()) {
  return {
    id: row.id,
    anchorId: row.anchorId,
    provider: row.provider,
    position: { x: row.posX, y: row.posY, z: row.posZ },
    rotation: { x: row.rotX, y: row.rotY, z: row.rotZ, w: row.rotW },
    scale: row.scale,
    expiresAt: row.expiresAt ? row.expiresAt.toISOString() : null,
    expired: row.expiresAt ? row.expiresAt.getTime() <= now.getTime() : false,
    deviceLabel: row.deviceLabel,
    createdAt: row.createdAt.toISOString(),
    // Stated in the payload so nobody has to remember it from a doc, and so a
    // client that starts flipping axes is contradicting the response it was
    // handed. docs/FRAMES.md.
    frame: { handedness: 'left', upAxis: 'y', space: 'anchor->scene', units: 'metres' },
  }
}
