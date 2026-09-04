/**
 * Anchor validation and serialisation checks that need no database.
 *
 * Covers the properties the multi-device story rests on: a valid transform is
 * accepted, the failures that would misplace the twin are rejected rather than
 * stored, and the response round-trips the transform without reordering it.
 *
 *   npx tsx scripts/check-anchor.ts
 */
import {
  AnchorInput,
  QUAT_TOLERANCE,
  isUnitQuaternion,
  quaternionLength,
  serialiseAnchor,
  toAnchorColumns,
} from '../src/lib/anchor'

let failures = 0
function check(name: string, condition: boolean) {
  console.log(`${condition ? 'PASS' : 'FAIL'}  ${name}`)
  if (!condition) failures += 1
}

const identity = { x: 0, y: 0, z: 0, w: 1 }

function body(over: Record<string, unknown> = {}) {
  return {
    anchorId: 'ca_abc123',
    position: { x: 1.5, y: -2, z: 0.25 },
    rotation: identity,
    ...over,
  }
}

// --- quaternion validity -------------------------------------------------

check('identity quaternion is unit', isUnitQuaternion(identity))
check(
  'a normalised arbitrary quaternion is unit',
  isUnitQuaternion({ x: 0.5, y: 0.5, z: 0.5, w: 0.5 })
)
check('all-zero quaternion is rejected', !isUnitQuaternion({ x: 0, y: 0, z: 0, w: 0 }))
check(
  'unnormalised quaternion is rejected',
  !isUnitQuaternion({ x: 1, y: 1, z: 1, w: 1 })
)
check(
  'drift inside tolerance is accepted',
  isUnitQuaternion({ x: 0, y: 0, z: 0, w: 1 + QUAT_TOLERANCE / 2 })
)
check(
  'drift outside tolerance is rejected',
  !isUnitQuaternion({ x: 0, y: 0, z: 0, w: 1 + QUAT_TOLERANCE * 10 })
)
check('quaternionLength is euclidean', Math.abs(quaternionLength({ x: 3, y: 4, z: 0, w: 0 }) - 5) < 1e-12)

// --- schema --------------------------------------------------------------

check('a valid anchor parses', AnchorInput.safeParse(body()).success)
check(
  'a non-unit rotation is refused by the schema, not just the helper',
  !AnchorInput.safeParse(body({ rotation: { x: 1, y: 1, z: 1, w: 1 } })).success
)
check('empty anchorId is refused', !AnchorInput.safeParse(body({ anchorId: '' })).success)
check(
  'missing rotation is refused',
  !AnchorInput.safeParse({ anchorId: 'x', position: { x: 0, y: 0, z: 0 } }).success
)
check(
  'missing position is refused',
  !AnchorInput.safeParse({ anchorId: 'x', rotation: identity }).success
)
check(
  'a zero scale is refused (an invisible twin reads as broken tracking)',
  !AnchorInput.safeParse(body({ scale: 0 })).success
)
check(
  'a negative scale is refused (it mirrors the scene)',
  !AnchorInput.safeParse(body({ scale: -1 })).success
)
check('a positive scale is accepted', AnchorInput.safeParse(body({ scale: 1.02 })).success)
check(
  'unknown keys are rejected by strict schema',
  !AnchorInput.safeParse(body({ unknownKey: 'foo' })).success
)
check(
  'a non-finite coordinate is refused',
  !AnchorInput.safeParse(body({ position: { x: Infinity, y: 0, z: 0 } })).success
)
check(
  'a malformed expiresAt is refused',
  !AnchorInput.safeParse(body({ expiresAt: 'next tuesday' })).success
)
check(
  'an ISO expiresAt is accepted',
  AnchorInput.safeParse(body({ expiresAt: '2027-09-02T10:00:00Z' })).success
)
check(
  'ttlDays is accepted and produces a future expiry',
  (() => {
    const p = AnchorInput.parse(body({ ttlDays: 30 }))
    const c = toAnchorColumns(p)
    return c.expiresAt !== null && c.expiresAt.getTime() > Date.now()
  })()
)

// --- columns and response ------------------------------------------------

const parsed = AnchorInput.parse(body({ expiresAt: '2027-09-02T10:00:00Z' }))
const cols = toAnchorColumns(parsed)
check(
  'position maps to the right columns in the right order',
  cols.posX === 1.5 && cols.posY === -2 && cols.posZ === 0.25
)
check(
  'rotation maps w correctly (the field most easily transposed)',
  cols.rotX === 0 && cols.rotY === 0 && cols.rotZ === 0 && cols.rotW === 1
)

// Normalisation test: slightly drifted quaternion is normalised on store
const driftParsed = AnchorInput.parse(
  body({ rotation: { x: 0, y: 0, z: 0, w: 1 + QUAT_TOLERANCE / 2 } })
)
const driftCols = toAnchorColumns(driftParsed)
const storedLen = Math.sqrt(
  driftCols.rotX ** 2 + driftCols.rotY ** 2 + driftCols.rotZ ** 2 + driftCols.rotW ** 2
)
check('stored quaternion is strictly normalised to length 1.0', Math.abs(storedLen - 1.0) < 1e-12)

check('expiresAt becomes a Date', cols.expiresAt instanceof Date)
check('omitted deviceLabel becomes null, not undefined', cols.deviceLabel === null)
check(
  'omitted provider is left to the schema default rather than sent as undefined',
  !('provider' in cols)
)
check(
  'omitted scale is left to the schema default',
  !('scale' in cols)
)

const row = {
  id: 'anc_1',
  anchorId: 'ca_abc123',
  provider: 'arcore-cloud-anchors',
  posX: 1.5,
  posY: -2,
  posZ: 0.25,
  rotX: 0,
  rotY: 0,
  rotZ: 0,
  rotW: 1,
  scale: 1,
  expiresAt: new Date('2027-09-02T10:00:00.000Z'),
  deviceLabel: 'pixel-7a',
  createdAt: new Date('2026-09-02T12:00:00.000Z'),
}

const out = serialiseAnchor(row, new Date('2026-09-02T13:00:00Z'))
check(
  'the transform round-trips through serialise unchanged',
  out.position.x === 1.5 &&
    out.position.y === -2 &&
    out.position.z === 0.25 &&
    out.rotation.w === 1
)
check('a future expiry is not marked expired', out.expired === false)
check(
  'a past expiry IS marked expired, so a dead anchor does not look like bad tracking',
  serialiseAnchor(row, new Date('2028-01-01T00:00:00Z')).expired === true
)
check(
  'an anchor with no recorded expiry is not reported as expired',
  serialiseAnchor({ ...row, expiresAt: null }).expired === false
)
check(
  'the response states the frame convention (docs/FRAMES.md)',
  out.frame.handedness === 'left' && out.frame.upAxis === 'y' && out.frame.units === 'metres'
)
check(
  'a client parsing the response never sees the flat columns',
  !('posX' in out) && !('rotW' in out)
)

// A transform that survives validation but is silently transposed is the
// failure docs/FRAMES.md is about, so assert the axes are not swapped anywhere.
check(
  'y and z are not swapped in the round trip',
  out.position.y === row.posY && out.position.z === row.posZ
)

console.log(failures === 0 ? '\nall anchor checks passed' : `\n${failures} FAILED`)
process.exit(failures === 0 ? 0 : 1)
