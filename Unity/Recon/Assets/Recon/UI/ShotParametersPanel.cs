using Recon.Ballistics;
using Recon.Colliders;
using Recon.Spatter;
using UnityEngine;

namespace Recon.UI
{
    /// <summary>
    /// The parameter input from handbook Section 09 step 5, and the control surface for the Challenge
    /// 1 measurement: pick a collider set, fire the same shot into it, read the impact counts per set.
    ///
    /// IMGUI on purpose, like FpsCounter and DevSplatLoader. No Canvas, no EventSystem, no prefab, so
    /// it works in a scene created entirely by script and nobody has to own a UI asset in git.
    /// </summary>
    [AddComponentMenu("Recon/UI/Shot Parameters Panel")]
    [DisallowMultipleComponent]
    public sealed class ShotParametersPanel : MonoBehaviour
    {
        [SerializeField] bool show = true;
        [SerializeField] ShotController shotController;
        [SerializeField] SpatterEmitter spatterEmitter;
        [SerializeField] ColliderMeshLoader colliderMeshLoader;

        GUIStyle m_label;
        GUIStyle m_button;

        void OnEnable()
        {
            if (shotController == null) shotController = GetComponent<ShotController>();
            if (spatterEmitter == null) spatterEmitter = GetComponent<SpatterEmitter>();
            if (colliderMeshLoader == null) colliderMeshLoader = GetComponent<ColliderMeshLoader>();
        }

        public bool Show
        {
            get => show;
            set => show = value;
        }

        void OnGUI()
        {
            if (!show || shotController == null) return;

            float scale = Screen.dpi > 0 ? Screen.dpi / 160f : 1.5f;
            int font = Mathf.RoundToInt(11f * scale);
            if (m_label == null)
                m_label = new GUIStyle(GUI.skin.label) { fontSize = font, wordWrap = true };
            if (m_button == null)
                m_button = new GUIStyle(GUI.skin.button) { fontSize = font };
            m_label.fontSize = font;
            m_button.fontSize = font;

            float width = Mathf.Min(Screen.width - 24f, 132f * scale);
            float rowHeight = 26f * scale;
            var area = new Rect(12f, 100f * scale, width, Screen.height - 120f * scale);

            GUILayout.BeginArea(area);
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label($"Speed  {shotController.Speed:F0} m/s", m_label);
            shotController.Speed = GUILayout.HorizontalSlider(shotController.Speed, 5f, 400f, GUILayout.Height(rowHeight * 0.6f));
            GUILayout.Label($"dt {shotController.Dt * 1000f:F2} ms, step {shotController.Speed * shotController.Dt * 100f:F1} cm", m_label);

            shotController.UseDrag = GUILayout.Toggle(shotController.UseDrag, " Air drag", m_label);
            if (spatterEmitter != null)
                spatterEmitter.EmitOnImpact = GUILayout.Toggle(spatterEmitter.EmitOnImpact, " Spatter", m_label);

            GUILayout.Space(4f * scale);
            GUILayout.Label("Collider set", m_label);
            GUILayout.BeginHorizontal();
            SetButton(ColliderSet.ArPlanes, "Planes", rowHeight);
            SetButton(ColliderSet.SplatMesh, "Mesh", rowHeight);
            SetButton(ColliderSet.Both, "Both", rowHeight);
            GUILayout.EndHorizontal();

            GUILayout.Space(4f * scale);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("FIRE", m_button, GUILayout.Height(rowHeight * 1.6f))) shotController.Fire();
            if (GUILayout.Button("Clear", m_button, GUILayout.Height(rowHeight * 1.6f)))
            {
                shotController.ClearImpacts();
                if (spatterEmitter != null) spatterEmitter.ClearDecals();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f * scale);
            GUILayout.Label(
                $"Impacts  planes {shotController.ImpactCountFor(ColliderSet.ArPlanes)}" +
                $"   mesh {shotController.ImpactCountFor(ColliderSet.SplatMesh)}" +
                $"   other {shotController.ImpactCountFor(ColliderSet.Both)}" +
                $"\nshots fired {shotController.ShotsFired}", m_label);

            if (colliderMeshLoader != null)
            {
                GUILayout.Space(4f * scale);
                if (GUILayout.Button(colliderMeshLoader.DebugVisible ? "Hide mesh" : "Show mesh", m_button, GUILayout.Height(rowHeight)))
                    colliderMeshLoader.SetDebugVisible(!colliderMeshLoader.DebugVisible);
                GUILayout.Label(colliderMeshLoader.Status, m_label);
            }

            GUILayout.Space(4f * scale);
            GUILayout.Label(shotController.Status, m_label);
            if (spatterEmitter != null) GUILayout.Label(spatterEmitter.Status, m_label);

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void SetButton(ColliderSet set, string caption, float rowHeight)
        {
            bool selected = shotController.TargetSet == set;
            bool usable = CollisionLayers.IsUsable(set);
            var previous = GUI.color;
            // Grey means the layer is missing, so a shot at that set would hit nothing.
            GUI.color = !usable ? Color.grey : selected ? Color.cyan : previous;
            if (GUILayout.Button(selected ? "[" + caption + "]" : caption, m_button, GUILayout.Height(rowHeight)))
                shotController.TargetSet = set;
            GUI.color = previous;
        }
    }
}
