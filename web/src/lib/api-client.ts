/**
 * Browser side API client.
 *
 * The dashboard talks to the same Bearer-token API the Unity client uses,
 * rather than reading Prisma from server components. That is a deliberate
 * choice with a real benefit and a real cost.
 *
 * Why: `sessionFromRequest` only accepts `Authorization: Bearer`, with no
 * cookie session, because docs/API.md specifies login as "returns a token" for
 * the Unity client. A server component has no token to send. Adding cookie
 * sessions is a genuine auth change and belongs to D7 (27 Sep), not to a UI
 * task. The upside is that every screen here exercises the exact path Unity
 * depends on, so a broken endpoint shows up in the browser before it shows up
 * on a phone.
 *
 * The cost, stated because it should not be discovered later: the token lives
 * in sessionStorage, which is readable by any script on the page. For a
 * localhost dev dashboard behind a tailnet that is an acceptable trade; the
 * fix is an httpOnly cookie, and it should be part of D7's session work.
 * sessionStorage rather than localStorage so the token dies with the tab.
 */

const TOKEN_KEY = 'recon.token'
const USER_KEY = 'recon.user'

export type Role = 'ADMIN' | 'INVESTIGATOR' | 'VIEWER'

export type SessionUser = {
  id: string
  email: string
  role: Role
}

export type JobStatus = 'PENDING' | 'PROCESSING' | 'READY' | 'FAILED' | 'CANCELLED'

export type CaseRow = {
  id: string
  title: string
  description: string | null
  createdAt: string
  updatedAt: string
  ownerId: string
  sceneCount: number
}

export type SceneRow = {
  id: string
  name: string
  createdAt: string
  unitScale: number | null
  caseId: string
  job: { id: string; status: JobStatus; progress: number } | null
}

export type SceneAsset = {
  id: string
  kind: 'SOURCE_VIDEO' | 'SPLAT_PLY' | 'METADATA_JSON'
  sha256: string
  byteSize: number
  mimeType: string | null
  createdAt: string
}

export type SceneDetail = {
  id: string
  name: string
  createdAt: string
  unitScale: number | null
  caseId: string
  job: { id: string; status: JobStatus; progress: number; error: string | null } | null
  assets: SceneAsset[]
  assetUrl: string
}

export type CustodyReport = {
  ok: boolean
  rows: number
  firstBadSeq?: number | null
  message?: string
}

/** Carries the API's own error code so callers can branch without string matching. */
export class ApiError extends Error {
  constructor(
    readonly code: string,
    message: string,
    readonly status: number
  ) {
    super(message)
  }
}

export function getToken(): string | null {
  if (typeof window === 'undefined') return null
  try {
    return window.sessionStorage.getItem(TOKEN_KEY)
  } catch {
    // Private browsing and some hardened settings throw on storage access.
    return null
  }
}

export function getUser(): SessionUser | null {
  if (typeof window === 'undefined') return null
  try {
    const raw = window.sessionStorage.getItem(USER_KEY)
    return raw ? (JSON.parse(raw) as SessionUser) : null
  } catch {
    return null
  }
}

export function setSession(token: string, user: SessionUser): void {
  try {
    window.sessionStorage.setItem(TOKEN_KEY, token)
    window.sessionStorage.setItem(USER_KEY, JSON.stringify(user))
  } catch {
    // Nothing useful to do: the caller will find getToken() null and route to
    // login rather than showing a half-signed-in dashboard.
  }
}

export function clearSession(): void {
  try {
    window.sessionStorage.removeItem(TOKEN_KEY)
    window.sessionStorage.removeItem(USER_KEY)
  } catch {
    /* nothing to clear */
  }
}

type ApiEnvelope = { error?: { code?: string; message?: string } }

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = getToken()
  const headers = new Headers(init.headers)
  if (token) headers.set('authorization', `Bearer ${token}`)
  if (init.body && !headers.has('content-type')) {
    headers.set('content-type', 'application/json')
  }

  let res: Response
  try {
    res = await fetch(path, { ...init, headers })
  } catch {
    // A dead dev server and a dead database look identical from here, so say
    // the honest thing rather than guessing which.
    throw new ApiError('NETWORK', 'Cannot reach the API. Is the dev server running?', 0)
  }

  if (res.status === 204) return undefined as T

  const text = await res.text()
  let body: unknown = null
  if (text) {
    try {
      body = JSON.parse(text)
    } catch {
      body = null
    }
  }

  if (!res.ok) {
    const e = (body as ApiEnvelope | null)?.error
    throw new ApiError(
      e?.code ?? 'UNKNOWN',
      // The API's own message is the useful one; the status is the fallback.
      e?.message ?? `Request failed with ${res.status}`,
      res.status
    )
  }
  return body as T
}

export const api = {
  login: (email: string, password: string) =>
    request<{ token: string; user: SessionUser }>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),

  cases: () => request<{ cases: CaseRow[] }>('/api/cases'),

  createCase: (title: string, description?: string) =>
    request<{ case: CaseRow }>('/api/cases', {
      method: 'POST',
      body: JSON.stringify({ title, description: description || undefined }),
    }),

  scenes: (status?: JobStatus) =>
    request<{ scenes: SceneRow[] }>(
      status ? `/api/scenes?status=${encodeURIComponent(status)}` : '/api/scenes'
    ),

  scene: (id: string) => request<{ scene: SceneDetail }>(`/api/scenes/${id}`),

  custodyVerify: () => request<CustodyReport>('/api/custody/verify'),
}

/**
 * Downloads an asset with the Bearer header attached.
 *
 * A plain <a href> cannot carry the header, so the bytes are fetched into a
 * blob and handed to a synthetic link. Fine for a 44 MB .ply on a desktop; if
 * assets grow past a few hundred MB this wants a short-lived signed URL
 * instead, which is the same seam FTW-13 left open for object storage.
 */
export async function downloadAsset(sceneId: string, filename: string): Promise<void> {
  const token = getToken()
  const res = await fetch(`/api/scenes/${sceneId}/asset`, {
    headers: token ? { authorization: `Bearer ${token}` } : undefined,
  })
  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as ApiEnvelope | null
    throw new ApiError(
      body?.error?.code ?? 'UNKNOWN',
      body?.error?.message ?? `Download failed with ${res.status}`,
      res.status
    )
  }
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}
