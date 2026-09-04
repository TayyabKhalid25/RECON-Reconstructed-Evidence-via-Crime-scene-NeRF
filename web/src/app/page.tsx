import { redirect } from 'next/navigation'

/** Nothing lives at the root; the dashboard starts at the case list. */
export default function Home() {
  redirect('/cases')
}
