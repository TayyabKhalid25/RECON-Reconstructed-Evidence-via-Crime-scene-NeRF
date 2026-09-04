/**
 * AES-256-GCM at-rest checks. No database, no network.
 *
 * Covers the properties the report's "encryption at rest" claim rests on:
 * a round trip returns the exact bytes, the wrong key fails loudly rather than
 * returning garbage, tampering with any part of the stored file is detected,
 * IVs are never reused, and a plaintext file written before encryption was
 * switched on still reads back.
 *
 *   npx tsx scripts/check-encryption.ts
 */
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { randomBytes } from 'node:crypto'

import {
  EncryptionError,
  HEADER_LEN,
  KEY_LEN,
  decryptBuffer,
  encryptBuffer,
  isEncrypted,
  loadKey,
} from '../src/lib/encryption'

let failures = 0
function check(name: string, condition: boolean) {
  console.log(`${condition ? 'PASS' : 'FAIL'}  ${name}`)
  if (!condition) failures += 1
}

function threw(fn: () => unknown): boolean {
  try {
    fn()
    return false
  } catch {
    return true
  }
}

const key = randomBytes(KEY_LEN)
const other = randomBytes(KEY_LEN)
const plain = Buffer.from('ply\nformat binary_little_endian 1.0\nend_header\n\x00\x01\x02')

// --- round trip ----------------------------------------------------------

const enc = encryptBuffer(plain, key)
check('round trip returns the exact plaintext', decryptBuffer(enc, key).equals(plain))
check('ciphertext is not the plaintext', !enc.subarray(HEADER_LEN).equals(plain))
check(
  `stored file is exactly ${HEADER_LEN} bytes larger than the plaintext`,
  enc.length === plain.length + HEADER_LEN
)
check('stored file is recognised as encrypted', isEncrypted(enc))
check('plaintext is not mistaken for encrypted', !isEncrypted(plain))
check('an empty buffer round trips', decryptBuffer(encryptBuffer(Buffer.alloc(0), key), key).length === 0)

// AAD binding test: encrypting for key A and decrypting with key B fails!
const encA = encryptBuffer(plain, 'scenes/1/video.mp4', key)
check(
  'decrypting with matching storageKey succeeds',
  decryptBuffer(encA, 'scenes/1/video.mp4', key).equals(plain)
)
check(
  'substituting ciphertext under a different storageKey fails authentication',
  threw(() => decryptBuffer(encA, 'scenes/2/video.mp4', key))
)

// A .ply-sized payload, because GCM is a stream cipher underneath and a
// multi-block input is the realistic case.
const big = randomBytes(1 << 20)
check('1 MiB round trips byte for byte', decryptBuffer(encryptBuffer(big, key), key).equals(big))

// --- wrong key -----------------------------------------------------------

check('the wrong key fails rather than returning garbage', threw(() => decryptBuffer(enc, other)))
check(
  'the wrong key raises EncryptionError, not a bare crypto throw',
  (() => {
    try {
      decryptBuffer(enc, other)
      return false
    } catch (e) {
      return e instanceof EncryptionError
    }
  })()
)

// --- tamper detection, byte by region ------------------------------------

function tamperAt(offset: number): Buffer {
  const copy = Buffer.from(enc)
  copy[offset] ^= 0xff
  return copy
}

check('tampering with the ciphertext is detected', threw(() => decryptBuffer(tamperAt(HEADER_LEN), key)))
check('tampering with the last ciphertext byte is detected', threw(() => decryptBuffer(tamperAt(enc.length - 1), key)))
check('tampering with the IV is detected', threw(() => decryptBuffer(tamperAt(8), key)))
check('tampering with the auth tag is detected', threw(() => decryptBuffer(tamperAt(8 + 12), key)))
check('a corrupted magic prefix is rejected', threw(() => decryptBuffer(tamperAt(0), key)))
check('a truncated file is rejected', threw(() => decryptBuffer(enc.subarray(0, HEADER_LEN - 1), key)))
check(
  'a header-only file with no ciphertext is rejected or empty, never garbage',
  (() => {
    const headerOnly = enc.subarray(0, HEADER_LEN)
    try {
      return decryptBuffer(headerOnly, key).length === 0
    } catch {
      return true
    }
  })()
)

// --- IV uniqueness, the one catastrophic GCM misuse ----------------------

const ivs = new Set<string>()
for (let i = 0; i < 200; i += 1) {
  ivs.add(encryptBuffer(plain, key).subarray(8, 8 + 12).toString('hex'))
}
check('200 encryptions of identical plaintext produce 200 distinct IVs', ivs.size === 200)
check(
  'identical plaintext encrypts to different ciphertext each time',
  encryptBuffer(plain, key).subarray(HEADER_LEN).toString('hex') !==
    encryptBuffer(plain, key).subarray(HEADER_LEN).toString('hex')
)

// --- key loading ---------------------------------------------------------

check('a 64 char hex key loads', loadKey(key.toString('hex')).equals(key))
check('a base64 key loads', loadKey(key.toString('base64')).equals(key))
check('hex and base64 of the same key agree', loadKey(key.toString('hex')).equals(loadKey(key.toString('base64'))))
check('a missing key throws rather than writing plaintext', threw(() => loadKey(undefined)))
check('an empty key throws', threw(() => loadKey('   ')))
check('a short key throws', threw(() => loadKey(randomBytes(16).toString('hex'))))
check('a long key throws', threw(() => loadKey(randomBytes(48).toString('hex'))))
check('a non-key string throws', threw(() => loadKey('hunter2')))

// --- storage layer round trip, on real files -----------------------------

async function storageChecks() {
  const dir = await mkdtemp(path.join(tmpdir(), 'recon-enc-'))
  const prevDir = process.env.ASSET_DIR
  const prevOn = process.env.ASSET_ENCRYPTION
  const prevKey = process.env.ASSET_ENCRYPTION_KEY
  const prevCwd = process.cwd()
  try {
    process.chdir(dir)
    process.env.ASSET_DIR = './storage'
    process.env.ASSET_ENCRYPTION = 'on'
    process.env.ASSET_ENCRYPTION_KEY = key.toString('hex')

    // Imported after the env is set: the module reads it per call, but the
    // storage module resolves ASSET_DIR at import time.
    const storage = await import('../src/lib/storage')

    const k = storage.assetKey('scn_test', 'splat_unity.ply')
    await storage.writeAsset(k, big)

    const onDisk = await readFile(path.join(dir, 'storage', k))
    check('a written asset is ciphertext on disk', !onDisk.equals(big))
    check('a written asset carries the magic prefix', isEncrypted(onDisk))
    check(
      'plaintext bytes do not appear on disk',
      !onDisk.subarray(HEADER_LEN).equals(big)
    )

    const stream = await storage.assetStream(k)
    const chunks: Buffer[] = []
    for await (const c of stream as unknown as AsyncIterable<Uint8Array>) {
      chunks.push(Buffer.from(c))
    }
    check('streaming an encrypted asset returns the exact plaintext', Buffer.concat(chunks).equals(big))

    check('on-disk size includes the header', (await storage.assetByteSize(k)) === big.length + HEADER_LEN)
    check('plaintext size excludes the header', (await storage.assetPlaintextSize(k)) === big.length)

    // The rollout case: a file written before encryption was switched on.
    process.env.ASSET_ENCRYPTION = 'off'
    const legacyKey = storage.assetKey('scn_test', 'legacy.ply')
    await storage.writeAsset(legacyKey, plain)
    const legacyDisk = await readFile(path.join(dir, 'storage', legacyKey))
    check('with encryption off, bytes are written as plaintext', legacyDisk.equals(plain))

    process.env.ASSET_ENCRYPTION = 'on'
    const legacyStream = await storage.assetStream(legacyKey)
    const lc: Buffer[] = []
    for await (const c of legacyStream as unknown as AsyncIterable<Uint8Array>) {
      lc.push(Buffer.from(c))
    }
    check(
      'a plaintext file still reads back with encryption on (safe rollout)',
      Buffer.concat(lc).equals(plain)
    )
    check(
      'plaintext size of a legacy file is its real size',
      (await storage.assetPlaintextSize(legacyKey)) === plain.length
    )

    // Tampering on disk must surface, not serve silently.
    const tamperKey = storage.assetKey('scn_test', 'tampered.ply')
    await storage.writeAsset(tamperKey, plain)
    const p = path.join(dir, 'storage', tamperKey)
    const bytes = await readFile(p)
    bytes[bytes.length - 1] ^= 0xff
    await writeFile(p, bytes)
    let errored = false
    try {
      const ts = await storage.assetStream(tamperKey)
      for await (const c of ts as unknown as AsyncIterable<Uint8Array>) void c
    } catch {
      errored = true
    }
    check('a tampered asset errors while streaming rather than serving silently', errored)
  } finally {
    process.chdir(prevCwd)
    if (prevDir === undefined) delete process.env.ASSET_DIR
    else process.env.ASSET_DIR = prevDir
    if (prevOn === undefined) delete process.env.ASSET_ENCRYPTION
    else process.env.ASSET_ENCRYPTION = prevOn
    if (prevKey === undefined) delete process.env.ASSET_ENCRYPTION_KEY
    else process.env.ASSET_ENCRYPTION_KEY = prevKey
    await rm(dir, { recursive: true, force: true })
  }
}

storageChecks()
  .then(() => {
    console.log(failures === 0 ? '\nall encryption checks passed' : `\n${failures} FAILED`)
    process.exit(failures === 0 ? 0 : 1)
  })
  .catch((e) => {
    console.error('\nencryption checks threw:', e)
    process.exit(1)
  })
