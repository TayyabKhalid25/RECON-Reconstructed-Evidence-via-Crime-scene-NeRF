import {
  createCipheriv,
  createDecipheriv,
  randomBytes,
  timingSafeEqual,
} from 'node:crypto'

/**
 * Application level envelope encryption for assets. AES-256-GCM.
 *
 * Handbook Section 08 is explicit that three different things get called
 * "encryption at rest" and that we should say which one we did. This is
 * option 3, the strongest of the three: asset bytes are encrypted before they
 * are ever written, the key lives outside the database, and the IV and auth tag
 * are stored alongside the ciphertext.
 *
 * What that buys, stated plainly because the report has to state it plainly:
 * an attacker with only database access gets metadata -- filenames, sizes,
 * SHA-256 of the plaintext, case and scene structure, the audit chain -- and
 * cannot read a single byte of any video or reconstruction. An attacker with
 * both the database and ASSET_ENCRYPTION_KEY reads everything, and an attacker
 * with filesystem access alone gets ciphertext only.
 *
 * ON-DISK LAYOUT
 *
 *   MAGIC(8) | IV(12) | TAG(16) | ciphertext
 *
 * The magic prefix is not decoration. It lets a reader tell an encrypted file
 * from a plaintext one, so encryption can be switched on for a deployment that
 * already has files on disk without a migration, without a schema change, and
 * without a column in the contract. Turning it off again still reads the
 * encrypted files back. Reversible in both directions.
 *
 * KEY HANDLING, at FYP scope and no more
 *
 * One 32 byte key per environment in ASSET_ENCRYPTION_KEY, hex or base64,
 * never in git. Rotation is future work and the report should say so rather
 * than implying an HSM. There is deliberately no key in code and no default:
 * a missing or malformed key throws instead of silently writing plaintext,
 * because "encryption was enabled but quietly did nothing" is the worst
 * outcome available here.
 */

const MAGIC = Buffer.from('RECONAG1', 'ascii') // RECON AES-GCM, format v1
const IV_LEN = 12 // 96 bits, the size GCM is specified for
const TAG_LEN = 16
export const HEADER_LEN = MAGIC.length + IV_LEN + TAG_LEN // 36

export const KEY_LEN = 32 // AES-256

/** Thrown for anything an operator can fix: bad key, tampered file. */
export class EncryptionError extends Error {}

/** Is encryption switched on for this deployment? */
export function encryptionEnabled(): boolean {
  return (process.env.ASSET_ENCRYPTION ?? 'off').toLowerCase() === 'on'
}

/**
 * The key, or a readable failure.
 *
 * Accepts hex or base64 because operators paste both, and validates the decoded
 * length rather than the string length. Base64 of 32 bytes is 44 characters and
 * hex is 64, so guessing from length alone would be fragile.
 */
export function loadKey(raw: string | undefined = process.env.ASSET_ENCRYPTION_KEY): Buffer {
  if (!raw || raw.trim() === '') {
    throw new EncryptionError(
      'ASSET_ENCRYPTION is on but ASSET_ENCRYPTION_KEY is not set. ' +
        'Generate one with: openssl rand -hex 32'
    )
  }
  const s = raw.trim()
  let key: Buffer
  if (/^[0-9a-fA-F]+$/.test(s) && s.length === KEY_LEN * 2) {
    key = Buffer.from(s, 'hex')
  } else {
    key = Buffer.from(s, 'base64')
  }
  if (key.length !== KEY_LEN) {
    throw new EncryptionError(
      `ASSET_ENCRYPTION_KEY decodes to ${key.length} bytes, need ${KEY_LEN}. ` +
        'Generate one with: openssl rand -hex 32'
    )
  }
  return key
}

/** Does this file start with our magic? Safe on a short buffer. */
export function isEncrypted(head: Buffer): boolean {
  if (head.length < MAGIC.length) return false
  // timingSafeEqual needs equal lengths; compare the prefix slice.
  return timingSafeEqual(head.subarray(0, MAGIC.length), MAGIC)
}

export function encryptBuffer(plain: Buffer, key: Buffer = loadKey()): Buffer {
  if (key.length !== KEY_LEN) {
    throw new EncryptionError(`key must be ${KEY_LEN} bytes, got ${key.length}`)
  }
  // A fresh random IV per file. Reusing an IV under the same key is the one
  // catastrophic misuse of GCM, so it is never derived from anything.
  const iv = randomBytes(IV_LEN)
  const cipher = createCipheriv('aes-256-gcm', key, iv)
  const enc = Buffer.concat([cipher.update(plain), cipher.final()])
  return Buffer.concat([MAGIC, iv, cipher.getAuthTag(), enc])
}

export function decryptBuffer(stored: Buffer, key: Buffer = loadKey()): Buffer {
  if (!isEncrypted(stored)) {
    throw new EncryptionError('not a RECON encrypted asset (magic prefix missing)')
  }
  if (stored.length < HEADER_LEN) {
    throw new EncryptionError(
      `truncated encrypted asset: ${stored.length} bytes, header alone is ${HEADER_LEN}`
    )
  }
  const iv = stored.subarray(MAGIC.length, MAGIC.length + IV_LEN)
  const tag = stored.subarray(MAGIC.length + IV_LEN, HEADER_LEN)
  const decipher = createDecipheriv('aes-256-gcm', key, iv)
  decipher.setAuthTag(tag)
  try {
    return Buffer.concat([
      decipher.update(stored.subarray(HEADER_LEN)),
      decipher.final(),
    ])
  } catch {
    // GCM's tag check failing means the bytes are not what we wrote: a wrong
    // key, or tampering. Callers treat this as a custody event, not a 500.
    throw new EncryptionError(
      'asset failed authentication: wrong key, or the stored bytes were modified'
    )
  }
}

/** Split a header for streaming decryption. Exported for the storage layer. */
export function parseHeader(head: Buffer): { iv: Buffer; tag: Buffer } {
  if (!isEncrypted(head)) {
    throw new EncryptionError('not a RECON encrypted asset (magic prefix missing)')
  }
  if (head.length < HEADER_LEN) {
    throw new EncryptionError('truncated encrypted asset header')
  }
  return {
    iv: head.subarray(MAGIC.length, MAGIC.length + IV_LEN),
    tag: head.subarray(MAGIC.length + IV_LEN, HEADER_LEN),
  }
}

/**
 * Decipher for streaming a large asset, with the tag already set.
 *
 * HONEST LIMITATION, and it belongs in the report rather than being buried:
 * GCM authenticates the whole ciphertext, but a streaming decrypt emits
 * plaintext as it goes and only discovers a bad tag at the very end. So a
 * tampered 44 MB .ply is partly written to the response before the stream
 * errors. We stream anyway because buffering a 44 MB file per request to serve
 * a phone over the tunnel is worse, and because the client has a second,
 * independent check: the asset route sends X-Asset-SHA256 from the custody
 * record, and the GPU worker and Unity client both re-hash what they received.
 * Detection is therefore not weakened; only the point of detection moves from
 * the server to the consumer.
 */
export function streamingDecipher(head: Buffer, key: Buffer = loadKey()) {
  const { iv, tag } = parseHeader(head)
  const decipher = createDecipheriv('aes-256-gcm', key, iv)
  decipher.setAuthTag(tag)
  return decipher
}
