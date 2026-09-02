import { createReadStream } from 'node:fs'
import { mkdir, stat, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { Readable } from 'node:stream'

/**
 * Asset storage. FTW-13 decided disk plus tunnel, so this writes under
 * ASSET_DIR and the asset route serves the bytes.
 *
 * docs/API.md specifies GET /api/scenes/:id/asset as "the .ply, or a redirect
 * to it", which is the seam that keeps the decision reversible: an object-store
 * driver would implement `urlFor` and the route would redirect instead of
 * streaming. Nothing outside this module should join paths itself.
 */

const ASSET_DIR = process.env.ASSET_DIR ?? './storage'

/**
 * Absolute root. Relative ASSET_DIR is relative to web/.
 *
 * turbopackIgnore: a non-literal path here makes the bundler trace the whole
 * project into the server output, since it cannot prove which files are read.
 * The path is server-only and built from env plus keys validated below, so
 * excluding it from static tracing is safe.
 */
function root(): string {
  return path.resolve(/*turbopackIgnore: true*/ process.cwd(), ASSET_DIR)
}

/**
 * Rejects a key that would escape the storage root.
 *
 * Keys are built server-side today, but this is the guard that stops a future
 * caller-supplied key from turning into path traversal and reading, say,
 * ../../.env.
 */
function resolveKey(storageKey: string): string {
  const abs = path.resolve(root(), storageKey)
  const base = root() + path.sep
  if (abs !== root() && !abs.startsWith(base)) {
    throw new Error(`storage key escapes ASSET_DIR: ${storageKey}`)
  }
  return abs
}

/**
 * Deterministic key for an asset. Scene-scoped so everything about one capture
 * sits together on disk, which matters when copying report assets off the box.
 */
export function assetKey(sceneId: string, filename: string): string {
  // Keep only the basename: an uploaded filename is attacker-controlled and may
  // contain slashes or "..".
  const safe = path.basename(filename).replace(/[^A-Za-z0-9._-]/g, '_')
  return path.posix.join('scenes', sceneId, safe)
}

export async function writeAsset(storageKey: string, data: Buffer): Promise<void> {
  const abs = resolveKey(storageKey)
  await mkdir(path.dirname(abs), { recursive: true })
  await writeFile(abs, data)
}

export async function assetExists(storageKey: string): Promise<boolean> {
  try {
    await stat(resolveKey(storageKey))
    return true
  } catch {
    return false
  }
}

export async function assetByteSize(storageKey: string): Promise<number> {
  return (await stat(resolveKey(storageKey))).size
}

/**
 * Web-standard stream for a route response. A 44 MB .ply must not be buffered
 * into memory to be served, and it will be fetched by a phone over the tunnel.
 */
export function assetStream(storageKey: string): ReadableStream<Uint8Array> {
  const nodeStream = createReadStream(resolveKey(storageKey))
  return Readable.toWeb(nodeStream) as ReadableStream<Uint8Array>
}

/**
 * Null on disk storage: the route streams bytes rather than redirecting. An
 * object-store driver returns a presigned URL here and the route 302s to it.
 */
export function urlFor(storageKey: string): string | null {
  void storageKey
  return null
}
