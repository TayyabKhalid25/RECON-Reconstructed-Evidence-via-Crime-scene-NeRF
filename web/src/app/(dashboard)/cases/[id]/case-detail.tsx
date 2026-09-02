'use client'

import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { getUser } from '@/lib/api-client'
import { useCaseScenes } from '@/lib/hooks'
import { StatusBadge } from '@/components/job-status'
import { UploadPanel } from '@/components/upload-panel'
import { Empty, Failed, Panel, Skeleton } from '@/components/ui'

export function CaseDetail({ caseId }: { caseId: string }) {
  const router = useRouter()
  const { scenes, error, isLoading, reload } = useCaseScenes(caseId)
  const role = getUser()?.role
  const canUpload = role === 'ADMIN' || role === 'INVESTIGATOR'

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <Link
          href="/cases"
          className="font-mono text-xs tracking-wider text-ink-faint uppercase hover:text-signal"
        >
          ← Cases
        </Link>
        <span className="font-mono text-xs text-ink-faint">{caseId}</span>
      </div>

      {canUpload && (
        <UploadPanel
          caseId={caseId}
          onUploaded={(sceneId) => {
            void reload()
            router.push(`/scenes/${sceneId}`)
          }}
        />
      )}

      <Panel label="Scenes">
        {error ? (
          <Failed
            code={error.code}
            message={error.message ?? 'Request failed'}
            onRetry={() => void reload()}
          />
        ) : isLoading || !scenes ? (
          <Skeleton rows={2} />
        ) : scenes.length === 0 ? (
          <Empty
            title="No scenes in this case"
            hint={
              canUpload
                ? 'Upload a walkthrough video above to create the first one.'
                : 'Nothing has been reconstructed for this case yet.'
            }
          />
        ) : (
          <ul className="divide-y divide-edge/60">
            {scenes.map((s) => (
              <li key={s.id}>
                <Link
                  href={`/scenes/${s.id}`}
                  className="flex items-center justify-between gap-4 px-1 py-3 hover:bg-panel-raised/60"
                >
                  <div className="min-w-0">
                    <p className="truncate text-sm text-ink">{s.name}</p>
                    <p className="font-mono text-[11px] text-ink-faint">
                      {new Date(s.createdAt).toLocaleString()}
                      {s.unitScale
                        ? ` · metric, ${s.unitScale.toFixed(6)} m/unit`
                        : ' · not yet metric'}
                    </p>
                  </div>
                  <StatusBadge status={s.job?.status} progress={s.job?.progress} />
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  )
}
