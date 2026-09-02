'use client'

import { getUser } from '@/lib/api-client'
import { useCustody } from '@/lib/hooks'
import { Btn, Datum, Empty, Failed, Panel, Skeleton } from '@/components/ui'

const REASON_COPY: Record<string, string> = {
  'hash-mismatch':
    'A row’s contents no longer match its own hash. That row was edited after it was written.',
  'chain-break':
    'A row’s prevHash does not match the previous row’s hash. A row was inserted or removed.',
  'sequence-gap':
    'The sequence numbers skip. A row was deleted outright.',
}

/**
 * Custody verification.
 *
 * Deliberately an explicit action, not a background poll: walking the whole
 * chain is real server work and the answer only changes when someone writes to
 * the log. This is also the screen that backs the report's tamper-evidence
 * claim, so it states what a pass actually proves rather than just showing a
 * green tick.
 */
export default function CustodyPage() {
  const isAdmin = getUser()?.role === 'ADMIN'
  const { report, error, isLoading, reload } = useCustody()

  // Reachable by typing the URL even though the nav link is hidden. Say why
  // rather than rendering a bare FORBIDDEN from the API.
  if (!isAdmin) {
    return (
      <Panel label="Chain of custody">
        <Empty
          title="Admin only"
          hint="Verifying the audit chain is restricted to administrators. The restriction is enforced on the server, not by hiding this page."
        />
      </Panel>
    )
  }

  return (
    <div className="space-y-4">
      <Panel
        label="Chain of custody"
        right={
          <Btn variant="primary" onClick={() => void reload()} disabled={isLoading}>
            {isLoading ? 'Verifying' : 'Re-verify'}
          </Btn>
        }
      >
        {error ? (
          <Failed
            code={error.code}
            message={error.message ?? 'Verification request failed'}
            onRetry={() => void reload()}
          />
        ) : isLoading || !report ? (
          <Skeleton rows={2} />
        ) : (
          <div className="space-y-4">
            <div
              className={`notch-sm border p-4 ${
                report.ok
                  ? 'border-ready/50 bg-ready/5'
                  : 'border-failed/50 bg-failed/5'
              }`}
            >
              <p
                className={`font-mono text-sm tracking-wider uppercase ${
                  report.ok ? 'text-ready' : 'text-failed'
                }`}
              >
                {report.ok ? 'Chain intact' : 'Tampering detected'}
              </p>
              <p className="mt-1.5 text-xs text-ink-soft">
                {report.ok
                  ? `${report.rows} audit rows verified. Every row’s hash covers its own contents plus the previous row’s hash, so any edit to any earlier row would break every hash after it.`
                  : REASON_COPY[report.reason] ?? 'The chain does not verify.'}
              </p>
            </div>

            <div>
              <Datum k="Rows checked" v={report.rows} />
              <Datum k="Result" v={report.ok ? 'pass' : 'fail'} />
              {!report.ok && (
                <>
                  <Datum k="First bad row" v={`seq ${report.failedAtSeq}`} />
                  <Datum k="Reason" v={report.reason} />
                </>
              )}
            </div>

            <p className="font-mono text-[10px] leading-relaxed text-ink-faint">
              What this does not prove: an attacker who can write to the database can
              append a consistent chain of their own. The hash chain makes edits to
              existing history detectable, not impossible.
            </p>
          </div>
        )}
      </Panel>
    </div>
  )
}
