'use client'

import { useState } from 'react'
import Link from 'next/link'
import { api, getUser } from '@/lib/api-client'
import { useCases } from '@/lib/hooks'
import { Btn, Empty, Failed, Field, Panel, Skeleton } from '@/components/ui'

export default function CasesPage() {
  const { cases, error, isLoading, reload } = useCases()
  const [creating, setCreating] = useState(false)
  const [title, setTitle] = useState('')
  const [busy, setBusy] = useState(false)
  const [createErr, setCreateErr] = useState<string | null>(null)

  const role = getUser()?.role
  const canCreate = role === 'ADMIN' || role === 'INVESTIGATOR'

  async function create(e: React.FormEvent) {
    e.preventDefault()
    if (!title.trim()) return
    setBusy(true)
    setCreateErr(null)
    try {
      await api.createCase(title.trim())
      setTitle('')
      setCreating(false)
      await reload()
    } catch (e) {
      const x = e as { message?: string }
      setCreateErr(x.message ?? 'Could not create case')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-4">
      <Panel
        label="Cases"
        right={
          canCreate && !creating ? (
            <Btn variant="primary" onClick={() => setCreating(true)}>
              Open case
            </Btn>
          ) : undefined
        }
      >
        {creating && (
          <form onSubmit={create} className="mb-4 space-y-3 border-b border-edge pb-4">
            <Field
              label="Case title"
              value={title}
              onChange={setTitle}
              required
              placeholder="Warehouse, 12 Aug"
            />
            <div className="flex gap-2">
              <Btn type="submit" variant="primary" disabled={busy || !title.trim()}>
                {busy ? 'Opening' : 'Create'}
              </Btn>
              <Btn variant="ghost" onClick={() => setCreating(false)}>
                Cancel
              </Btn>
            </div>
          </form>
        )}

        {createErr && (
          <div role="alert" className="notch-sm mb-4 border border-failed/40 bg-failed/5 p-3">
            <p className="font-mono text-xs text-failed">{createErr}</p>
          </div>
        )}

        {/* Four states, all designed: error, loading, empty, data. */}
        {error ? (
          <Failed
            code={error.code}
            message={error.message ?? 'Request failed'}
            onRetry={() => void reload()}
          />
        ) : isLoading || !cases ? (
          <Skeleton rows={3} />
        ) : cases.length === 0 ? (
          <Empty
            title="No cases yet"
            hint={
              canCreate
                ? 'Open a case, then upload a walkthrough video to it.'
                : 'A viewer cannot open cases. Ask an investigator to share one.'
            }
            action={
              canCreate ? (
                <Btn variant="primary" onClick={() => setCreating(true)}>
                  Open the first case
                </Btn>
              ) : undefined
            }
          />
        ) : (
          <ul className="divide-y divide-edge/60">
            {cases.map((c) => (
              <li key={c.id}>
                <Link
                  href={`/cases/${c.id}`}
                  className="flex items-center justify-between gap-4 px-1 py-3 transition-colors hover:bg-panel-raised/60"
                >
                  <div className="min-w-0">
                    <p className="truncate text-sm text-ink">{c.title}</p>
                    <p className="font-mono text-[11px] text-ink-faint">
                      {new Date(c.createdAt).toLocaleString()}
                    </p>
                  </div>
                  <span className="shrink-0 font-mono text-xs text-ink-soft tabular-nums">
                    {c.sceneCount} {c.sceneCount === 1 ? 'scene' : 'scenes'}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  )
}
