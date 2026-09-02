'use client'

import useSWR from 'swr'
import { api, type CaseRow, type CustodyReport, type SceneDetail } from './api-client'

/**
 * Data hooks, on SWR.
 *
 * Hand-rolled effect fetching was the first attempt and it does not survive
 * this toolchain: `react-hooks/set-state-in-effect` (React Compiler's rule set,
 * on by default in eslint-config-next 16) rejects setState reachable from an
 * effect body, await or no await, and points at "You Might Not Need an Effect".
 * The rule is right — cascading renders on every fetch — and the accepted
 * answer is a data layer, which is what the dashboard skill recommended in the
 * first place.
 *
 * Polling policy, which is the part that matters on free tiers: the interval is
 * decided per hook by the caller and returns 0 once a job reaches a terminal
 * state, so a finished scene stops hitting the database entirely. SWR pauses
 * automatically while the tab is hidden, so a dashboard left open on a
 * projector overnight is not still polling in the morning.
 */

const POLL_MS = 5000

/** Shared defaults. Deliberately quiet: the panel should not see a refetch storm. */
const BASE = {
  revalidateOnFocus: false,
  shouldRetryOnError: false,
} as const

export function useCases() {
  const { data, error, isLoading, mutate } = useSWR<{ cases: CaseRow[] }>(
    '/api/cases',
    () => api.cases(),
    BASE
  )
  return {
    cases: data?.cases,
    error: error as { code?: string; message?: string } | undefined,
    isLoading,
    reload: mutate,
  }
}

/**
 * One scene, polled only while its job can still change.
 *
 * `refreshInterval` takes a function so the decision is re-made after every
 * response rather than fixed at mount: the moment a job lands on READY or
 * FAILED the polling stops by itself.
 */
export function useScene(sceneId: string | null) {
  const { data, error, isLoading, mutate } = useSWR<{ scene: SceneDetail }>(
    sceneId ? `/api/scenes/${sceneId}` : null,
    () => api.scene(sceneId as string),
    {
      ...BASE,
      refreshInterval: (latest) => {
        const s = latest?.scene.job?.status
        return s === 'PENDING' || s === 'PROCESSING' ? POLL_MS : 0
      },
    }
  )
  return {
    scene: data?.scene,
    error: error as { code?: string; message?: string } | undefined,
    isLoading,
    reload: mutate,
  }
}

/**
 * Scenes for one case, polled while any of them is still working.
 *
 * There is no /api/scenes?caseId= filter in the contract, so this fetches the
 * caller's scenes and filters client side. Fine at six scenes; if the corpus
 * grows this wants a query parameter, which is a contract change and not one to
 * make casually.
 */
export function useCaseScenes(caseId: string) {
  const { data, error, isLoading, mutate } = useSWR(
    ['/api/scenes', caseId],
    async () => {
      const { scenes } = await api.scenes()
      return scenes.filter((s) => s.caseId === caseId)
    },
    {
      ...BASE,
      refreshInterval: (latest) =>
        latest?.some((s) => s.job?.status === 'PENDING' || s.job?.status === 'PROCESSING')
          ? POLL_MS
          : 0,
    }
  )
  return {
    scenes: data,
    error: error as { code?: string; message?: string } | undefined,
    isLoading,
    reload: mutate,
  }
}

/**
 * Custody verification.
 *
 * Never polled. Walking the whole hash chain is real work on the server, and
 * the answer only changes when someone writes to the log, so this is an
 * explicit action rather than a background refresh.
 */
export function useCustody() {
  const { data, error, isLoading, mutate } = useSWR<CustodyReport>(
    '/api/custody/verify',
    () => api.custodyVerify(),
    { ...BASE, revalidateOnMount: true }
  )
  return {
    report: data,
    error: error as { code?: string; message?: string } | undefined,
    isLoading,
    reload: mutate,
  }
}
