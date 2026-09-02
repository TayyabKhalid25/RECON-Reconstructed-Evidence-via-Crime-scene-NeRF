'use client'

import { useRef, useState } from 'react'
import { getToken } from '@/lib/api-client'
import { Btn, Field, Panel, bytes } from './ui'

/**
 * Video upload with real progress.
 *
 * XMLHttpRequest rather than fetch, deliberately: fetch cannot report upload
 * progress, and a 200 MB walkthrough posted with no feedback looks frozen at
 * exactly the wrong moment in a demo. Our own captures are 172 MB and 205 MB,
 * so this is not hypothetical.
 */
export function UploadPanel({
  caseId,
  onUploaded,
}: {
  caseId: string
  onUploaded: (sceneId: string) => void
}) {
  const [file, setFile] = useState<File | null>(null)
  const [sceneName, setSceneName] = useState('')
  const [pct, setPct] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [dragging, setDragging] = useState(false)
  const xhrRef = useRef<XMLHttpRequest | null>(null)

  const busy = pct !== null

  function start() {
    if (!file) return
    setError(null)
    setPct(0)

    const form = new FormData()
    form.append('video', file)
    form.append('caseId', caseId)
    if (sceneName.trim()) form.append('sceneName', sceneName.trim())

    const xhr = new XMLHttpRequest()
    xhrRef.current = xhr
    xhr.open('POST', '/api/uploads')
    const token = getToken()
    if (token) xhr.setRequestHeader('authorization', `Bearer ${token}`)

    xhr.upload.onprogress = (e) => {
      // lengthComputable is false for a chunked body; show indeterminate
      // rather than a fake number.
      if (e.lengthComputable) setPct(Math.round((e.loaded / e.total) * 100))
    }

    xhr.onload = () => {
      setPct(null)
      xhrRef.current = null
      let body: unknown = null
      try {
        body = JSON.parse(xhr.responseText)
      } catch {
        body = null
      }
      if (xhr.status >= 200 && xhr.status < 300) {
        const scene = (body as { scene?: { id?: string } } | null)?.scene
        setFile(null)
        setSceneName('')
        if (scene?.id) onUploaded(scene.id)
        return
      }
      const e = (body as { error?: { code?: string; message?: string } } | null)?.error
      setError(e?.message ?? `Upload failed with ${xhr.status}`)
    }

    xhr.onerror = () => {
      setPct(null)
      xhrRef.current = null
      setError('Upload failed: the connection dropped. Is the dev server still running?')
    }

    xhr.onabort = () => {
      setPct(null)
      xhrRef.current = null
      setError('Upload cancelled.')
    }

    xhr.send(form)
  }

  return (
    <Panel label="Ingest walkthrough">
      <div className="space-y-4">
        <div
          onDragOver={(e) => {
            e.preventDefault()
            setDragging(true)
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={(e) => {
            e.preventDefault()
            setDragging(false)
            const f = e.dataTransfer.files?.[0]
            if (f) setFile(f)
          }}
          className={`notch flex flex-col items-center gap-2 border border-dashed px-4 py-8 text-center transition-colors ${
            dragging ? 'border-signal bg-signal-dim/10' : 'border-edge-bright bg-abyss/60'
          }`}
        >
          <p className="font-mono text-sm text-ink-soft">
            {file ? file.name : 'Drop a walkthrough video, or choose one'}
          </p>
          <p className="text-xs text-ink-faint">
            {file ? bytes(file.size) : '1080p, 60 to 120 s, per docs/CAPTURE.md'}
          </p>
          <label className="mt-1">
            <span className="notch-sm cursor-pointer border border-edge-bright bg-panel-raised px-3.5 py-1.5 font-mono text-xs tracking-wider uppercase hover:border-signal-dim">
              Choose file
            </span>
            <input
              type="file"
              accept="video/*"
              className="hidden"
              disabled={busy}
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
          </label>
        </div>

        <Field
          label="Scene name (optional)"
          value={sceneName}
          onChange={setSceneName}
          placeholder="Living room, north wall"
        />

        {busy && (
          <div className="space-y-1.5">
            <div className="flex items-baseline justify-between">
              <span className="label">Uploading</span>
              <span className="font-mono text-xs tabular-nums text-signal">{pct}%</span>
            </div>
            <div className="h-1.5 overflow-hidden border border-edge bg-abyss">
              <div
                className="h-full bg-signal transition-[width] duration-200"
                style={{ width: `${pct}%` }}
              />
            </div>
            <p className="text-xs text-ink-faint">
              {pct === 100
                ? 'Hashing and enqueueing on the server…'
                : 'Do not close this tab.'}
            </p>
          </div>
        )}

        {error && (
          <div role="alert" className="notch-sm border border-failed/40 bg-failed/5 p-3">
            <p className="font-mono text-xs break-words text-failed">{error}</p>
          </div>
        )}

        <div className="flex gap-2">
          <Btn variant="primary" onClick={start} disabled={!file || busy}>
            {busy ? 'Uploading' : 'Upload and enqueue'}
          </Btn>
          {busy && (
            <Btn variant="danger" onClick={() => xhrRef.current?.abort()}>
              Cancel
            </Btn>
          )}
        </div>
      </div>
    </Panel>
  )
}
