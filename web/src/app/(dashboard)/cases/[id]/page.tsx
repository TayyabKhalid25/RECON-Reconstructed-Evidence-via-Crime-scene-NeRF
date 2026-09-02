import { CaseDetail } from './case-detail'

/**
 * Thin server page. `params` is a Promise in Next 16, so it is awaited here and
 * the id handed to a client component — the data fetching itself has to be
 * client side because the API is Bearer-token only.
 */
export default async function Page(props: PageProps<'/cases/[id]'>) {
  const { id } = await props.params
  return <CaseDetail caseId={id} />
}
