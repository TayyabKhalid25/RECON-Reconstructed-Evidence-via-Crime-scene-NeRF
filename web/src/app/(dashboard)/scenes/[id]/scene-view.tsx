'use client'

import { useState } from 'react'
import Link from 'next/link'
import { downloadAsset } from '@/lib/api-client'
import { useScene } from '@/lib/hooks'
import { JobError, StatusBadge } from '@/components/job-status'
import { Btn, Datum, Failed, Hash, Panel, Skeleton, bytes } from '@/components/ui'

const KIND_LABEL: Record<string, string> = {
  SOURCE_VIDEO: 'Source video',
  SPLAT_PLY: 'Splat cloud',
  METADATA_JSON: 'Metadata',
}

export function SceneView({ sceneId }: { sceneId: string }) {
  const { scene, error, isLoading, reload } = useScene(sceneId)
  const [dlError, setDlError] = useState<string | null>(null)
  const [dling, setDling] = useState(false)

  if (error) {
    return (
      <Failed
        code={error.code}
        message={error.message ?? 'Request failed'}
        onRetry={() => void reload()}
      />
    )
  }
  if (isLoading || !scene) return <Skeleton rows={5} />

  const ply = scene.assets.find((a) => a.kind === 'SPLAT_PLY')

  async function download() {
    setDlError(null)
    setDling(true)
    try {
      await downloadAsset(sceneId, `${scene!.name || sceneId}.ply`)
    } catch (e) {
      setDlError((e as { message?: string }).message ?? 'Download failed')
    } finally {
      setDling(false)
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <Link
          href={`/cases/${scene.caseId}`}
          className="font-mono text-xs tracking-wider text-ink-faint uppercase hover:text-signal"
        >
          ← Case
        </Link>
        <h1 className="text-sm text-ink">{scene.name}</h1>
        <StatusBadge status={scene.job?.status} progress={scene.job?.progress} />
      </div>

      {scene.job?.status === 'FAILED' && scene.job.error && (
        <JobError error={scene.job.error} />
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <Panel label="Reconstruction">
          <Datum k="Scene id" v={scene.id} />
          <Datum
            k="Scale"
            v={
              scene.unitScale ? (
                `${scene.unitScale.toFixed(6)} m / unit`
              ) : (
                <span className="text-warn">not metric</span>
              )
            }
          />
          <Datum k="Created" v={new Date(scene.createdAt).toLocaleString()} mono={false} />
          {/* Stated on screen because it is the thing most easily got wrong
              downstream: the .ply is in scene units and Unity applies
              unitScale. docs/FRAMES.md. */}
          <Datum k="Frame" v="left handed, Y up" />
          <Datum k="Units" v="scene units, not metres" />
        </Panel>

        <Panel
          label="Assets"
          right={
            ply ? (
              <Btn variant="primary" onClick={download} disabled={dling}>
                {dling ? 'Fetching' : 'Download .ply'}
              </Btn>
            ) : undefined
          }
        >
          {scene.assets.length === 0 ? (
            <p className="py-4 text-center font-mono text-xs text-ink-faint">
              Nothing produced yet.
            </p>
          ) : (
            <div className="space-y-3">
              {scene.assets.map((a) => (
                <div key={a.id} className="border-b border-edge/60 pb-2 last:border-0">
                  <div className="flex items-baseline justify-between gap-3">
                    <span className="label">{KIND_LABEL[a.kind] ?? a.kind}</span>
                    <span className="font-mono text-xs text-ink tabular-nums">
                      {bytes(a.byteSize)}
                    </span>
                  </div>
                  {/* The custody hash, on screen. A reviewer can compare this
                      against what they downloaded without opening a terminal. */}
                  <div className="mt-1 flex items-baseline justify-between gap-3">
                    <span className="text-[10px] tracking-widest text-ink-faint uppercase">
                      sha256
                    </span>
                    <Hash value={a.sha256} />
                  </div>
                </div>
              ))}
            </div>
          )}
          {dlError && (
            <div role="alert" className="notch-sm mt-3 border border-failed/40 bg-failed/5 p-3">
              <p className="font-mono text-xs text-failed">{dlError}</p>
            </div>
          )}
        </Panel>
      </div>
    </div>
  )
}
