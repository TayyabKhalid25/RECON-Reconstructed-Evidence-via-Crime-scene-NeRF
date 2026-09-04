/**
 * Dashboard primitives.
 *
 * Hand rolled rather than pulling in shadcn/ui and its radix, cva, clsx and
 * lucide dependencies. Two reasons: the repo records every installed version in
 * docs/STACK.md and does not add five packages casually, and the angular
 * detective-HUD look here is not shadcn's default look, so most of the library
 * would be fought rather than used. What is genuinely needed is a handful of
 * shapes, and this is that handful.
 *
 * The four-states rule from the dashboard skill lives here as real components:
 * Skeleton, Empty and Failed exist so no screen can default to a blank div,
 * which reads as broken during a demo.
 */
import type { ReactNode } from 'react'

function cx(...parts: (string | false | null | undefined)[]): string {
  return parts.filter(Boolean).join(' ')
}

/** A bordered surface. `label` renders the small uppercase caption on the edge. */
export function Panel({
  label,
  right,
  children,
  className,
}: {
  label?: string
  right?: ReactNode
  children: ReactNode
  className?: string
}) {
  return (
    <section
      className={cx(
        'notch border border-edge bg-panel/80 backdrop-blur-sm',
        className
      )}
    >
      {(label || right) && (
        <header className="flex items-center justify-between gap-3 border-b border-edge px-4 py-2.5">
          {label ? <h2 className="label">{label}</h2> : <span />}
          {right}
        </header>
      )}
      <div className="p-4">{children}</div>
    </section>
  )
}

export function Btn({
  children,
  onClick,
  type = 'button',
  variant = 'default',
  disabled,
  className,
}: {
  children: ReactNode
  onClick?: () => void
  type?: 'button' | 'submit'
  variant?: 'default' | 'primary' | 'danger' | 'ghost'
  disabled?: boolean
  className?: string
}) {
  const styles = {
    default:
      'border-edge-bright bg-panel-raised text-ink hover:bg-panel-hover hover:border-signal-dim',
    primary:
      'border-signal-dim bg-signal-dim/20 text-signal-glow hover:bg-signal-dim/35 hover:border-signal',
    danger: 'border-failed/50 bg-failed/10 text-failed hover:bg-failed/20',
    ghost: 'border-transparent bg-transparent text-ink-soft hover:text-ink hover:bg-panel-raised',
  }[variant]
  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className={cx(
        'notch-sm border px-3.5 py-1.5 font-mono text-xs tracking-wider uppercase',
        'transition-colors disabled:cursor-not-allowed disabled:opacity-40',
        styles,
        className
      )}
    >
      {children}
    </button>
  )
}

export function Field({
  label,
  value,
  onChange,
  type = 'text',
  placeholder,
  required,
  autoComplete,
}: {
  label: string
  value: string
  onChange: (v: string) => void
  type?: string
  placeholder?: string
  required?: boolean
  autoComplete?: string
}) {
  return (
    <label className="block">
      <span className="label mb-1.5 block">{label}</span>
      <input
        type={type}
        value={value}
        required={required}
        placeholder={placeholder}
        autoComplete={autoComplete}
        onChange={(e) => onChange(e.target.value)}
        className={cx(
          'notch-sm w-full border border-edge-bright bg-abyss px-3 py-2',
          'font-mono text-sm text-ink placeholder:text-ink-faint',
          'focus:border-signal-dim focus:outline-none'
        )}
      />
    </label>
  )
}

/** Loading: a skeleton, never a spinner on white. */
export function Skeleton({ rows = 3 }: { rows?: number }) {
  return (
    <div className="space-y-2" aria-busy="true" aria-live="polite">
      {Array.from({ length: rows }).map((_, i) => (
        <div
          key={i}
          className="relative h-11 overflow-hidden border border-edge bg-panel-raised/40"
        >
          <div className="sweep absolute inset-y-0 w-1/3 bg-gradient-to-r from-transparent via-signal-dim/15 to-transparent" />
        </div>
      ))}
      <span className="sr-only">Loading</span>
    </div>
  )
}

/** Empty: says what is missing and offers the action that fixes it. */
export function Empty({ title, hint, action }: { title: string; hint?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-3 py-12 text-center">
      <div className="notch-sm flex size-11 items-center justify-center border border-edge text-ink-faint">
        <span aria-hidden>∅</span>
      </div>
      <p className="font-mono text-sm text-ink-soft">{title}</p>
      {hint && <p className="max-w-sm text-xs text-ink-faint">{hint}</p>}
      {action}
    </div>
  )
}

/**
 * Error: the API's own message, verbatim, plus a retry.
 *
 * Showing the real message is what makes debugging between three cities
 * possible; a generic "something went wrong" throws away the only useful thing
 * the server said.
 */
export function Failed({
  message,
  code,
  onRetry,
}: {
  message: string
  code?: string
  onRetry?: () => void
}) {
  return (
    <div
      role="alert"
      className="notch-sm space-y-3 border border-failed/40 bg-failed/5 p-4"
    >
      <div className="flex items-baseline gap-2">
        <span className="font-mono text-xs tracking-widest text-failed uppercase">
          {code ?? 'Error'}
        </span>
      </div>
      <p className="font-mono text-sm break-words text-ink">{message}</p>
      {onRetry && (
        <Btn variant="default" onClick={onRetry}>
          Retry
        </Btn>
      )}
    </div>
  )
}

/** A key/value readout row, monospaced because it is data. */
export function Datum({ k, v, mono = true }: { k: string; v: ReactNode; mono?: boolean }) {
  return (
    <div className="flex items-baseline justify-between gap-4 border-b border-edge/60 py-2 last:border-0">
      <span className="label shrink-0">{k}</span>
      <span className={cx('text-right text-sm text-ink', mono && 'font-mono')}>{v}</span>
    </div>
  )
}

/** Long hashes need to be readable and copyable without blowing up the layout. */
export function Hash({ value, chars = 12 }: { value: string; chars?: number }) {
  return (
    <span title={value} className="font-mono text-xs text-ink-soft">
      {value.slice(0, chars)}…{value.slice(-4)}
    </span>
  )
}

export function bytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 ** 2) return `${(n / 1024).toFixed(1)} KB`
  if (n < 1024 ** 3) return `${(n / 1024 ** 2).toFixed(1)} MB`
  return `${(n / 1024 ** 3).toFixed(2)} GB`
}
