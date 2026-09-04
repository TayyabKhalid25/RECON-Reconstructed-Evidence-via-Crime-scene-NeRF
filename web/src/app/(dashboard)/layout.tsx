'use client'

import Link from 'next/link'
import { usePathname, useRouter } from 'next/navigation'
import { clearSession, type Role } from '@/lib/api-client'
import { SessionGate } from '@/components/session-gate'

/**
 * `adminOnly` mirrors the server's own rule: GET /api/custody/verify returns
 * FORBIDDEN for anyone but an ADMIN. Hiding the link is a courtesy so a viewer
 * is not offered a door that will not open — it is NOT the access control,
 * which is enforced in the route handler where it belongs.
 */
const NAV: { href: string; label: string; adminOnly?: boolean }[] = [
  { href: '/cases', label: 'Cases' },
  { href: '/custody', label: 'Custody', adminOnly: true },
]

/** Role badge. Colour carries meaning: an ADMIN should be visibly an ADMIN. */
function RoleTag({ role }: { role: Role }) {
  const look =
    role === 'ADMIN'
      ? 'border-warn/50 bg-warn/10 text-warn'
      : role === 'VIEWER'
        ? 'border-edge-bright bg-panel-raised text-ink-soft'
        : 'border-signal-dim/50 bg-signal-dim/10 text-signal'
  return (
    <span
      className={`notch-sm border px-2 py-0.5 font-mono text-[10px] tracking-widest uppercase ${look}`}
    >
      {role}
    </span>
  )
}

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const pathname = usePathname()
  const router = useRouter()

  return (
    <SessionGate>
      {(user) => (
        <div className="min-h-screen">
          <header className="sticky top-0 z-10 border-b border-edge bg-abyss/90 backdrop-blur">
            {/* Wraps at narrow widths: the panel will see this on a projector
                and possibly a phone, per the dashboard skill's gotcha table. */}
            <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
              <Link href="/cases" className="group flex items-baseline gap-2">
                <span className="font-mono text-sm font-semibold tracking-[0.25em] text-signal">
                  RECON
                </span>
                <span className="hidden text-[10px] tracking-widest text-ink-faint uppercase sm:inline">
                  evidence reconstruction
                </span>
              </Link>

              <nav className="flex items-center gap-1">
                {NAV.filter((n) => !n.adminOnly || user.role === 'ADMIN').map((n) => {
                  const active = pathname === n.href || pathname.startsWith(`${n.href}/`)
                  return (
                    <Link
                      key={n.href}
                      href={n.href}
                      className={`notch-sm border px-3 py-1.5 font-mono text-xs tracking-wider uppercase transition-colors ${
                        active
                          ? 'border-signal-dim bg-signal-dim/15 text-signal'
                          : 'border-transparent text-ink-soft hover:bg-panel-raised hover:text-ink'
                      }`}
                    >
                      {n.label}
                    </Link>
                  )
                })}
              </nav>

              <div className="ml-auto flex items-center gap-3">
                <div className="hidden text-right sm:block">
                  <p className="font-mono text-xs text-ink">{user.email}</p>
                </div>
                <RoleTag role={user.role} />
                <button
                  onClick={() => {
                    clearSession()
                    router.replace('/login')
                  }}
                  className="notch-sm border border-transparent px-2.5 py-1.5 font-mono text-xs tracking-wider text-ink-faint uppercase hover:border-failed/40 hover:text-failed"
                >
                  Sign out
                </button>
              </div>
            </div>
          </header>

          <main className="mx-auto max-w-6xl px-4 py-6">{children}</main>

          <footer className="mx-auto max-w-6xl px-4 pt-2 pb-8">
            <p className="font-mono text-[10px] tracking-wider text-ink-faint">
              Assets are served from the GPU machine over the tailnet. Scene units are
              metric only where a marker set unitScale.
            </p>
          </footer>
        </div>
      )}
    </SessionGate>
  )
}
