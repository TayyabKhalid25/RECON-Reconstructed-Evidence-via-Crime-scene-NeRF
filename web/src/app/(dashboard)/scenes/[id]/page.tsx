import { SceneView } from './scene-view'

export default async function Page(props: PageProps<'/scenes/[id]'>) {
  const { id } = await props.params
  return <SceneView sceneId={id} />
}
