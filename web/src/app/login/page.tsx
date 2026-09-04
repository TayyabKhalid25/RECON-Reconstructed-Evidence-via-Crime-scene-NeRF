'use client'

import { useState } from 'react'
import { useRouter } from 'next/navigation'
import { api, setSession } from '@/lib/api-client'
import { Btn, Field, Failed } from '@/components/ui'

export default function LoginPage() {
  const router = useRouter()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<{ code: string; message: string } | null>(null)

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErr(null)
    try {
      const { token, user } = await api.login(email, password)
      setSession(token, user)
      router.replace('/cases')
    } catch (e) {
      const x = e as { code?: string; message?: string }
      setErr({ code: x.code ?? 'UNKNOWN', message: x.message ?? 'Login failed' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center p-4">
      <div className="w-full max-w-sm space-y-6">
        <div className="space-y-1 text-center">
          <h1 className="font-mono text-2xl font-semibold tracking-[0.3em] text-signal">
            RECON
          </h1>
          <p className="text-[10px] tracking-[0.2em] text-ink-faint uppercase">
            Reconstructed evidence via crime-scene NeRF
          </p>
        </div>

        <form
          onSubmit={submit}
          className="notch space-y-4 border border-edge bg-panel/80 p-6 backdrop-blur-sm"
        >
          <Field
            label="Email"
            type="email"
            value={email}
            onChange={setEmail}
            required
            autoComplete="username"
            placeholder="investigator@fast.edu.pk"
          />
          <Field
            label="Password"
            type="password"
            value={password}
            onChange={setPassword}
            required
            autoComplete="current-password"
          />

          {/* The API's own message, verbatim: "Invalid credentials" and
              "Cannot reach the API" are very different problems and the
              operator should be able to tell them apart. */}
          {err && <Failed code={err.code} message={err.message} />}

          <Btn type="submit" variant="primary" disabled={busy} className="w-full">
            {busy ? 'Authenticating' : 'Sign in'}
          </Btn>
        </form>

        <p className="text-center font-mono text-[10px] text-ink-faint">
          Seeded accounts come from <span className="text-ink-soft">npm run db:seed</span>
        </p>
      </div>
    </main>
  )
}
