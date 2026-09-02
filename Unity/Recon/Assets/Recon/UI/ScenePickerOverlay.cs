using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Recon.Api;
using Recon.Config;
using Recon.Contract;
using Recon.Scenes;
using UnityEngine;

namespace Recon.UI
{
    /// <summary>
    /// Log in, list READY scenes, load one. IMGUI on purpose, like FpsCounter: no Canvas, no
    /// EventSystem, no prefab, so it works in a scene created by script and never turns up as
    /// unmergeable YAML in a PR. Deliberately ugly and small — the I2 gate is "the scene arrives
    /// and lines up", not a designed UI.
    ///
    /// The source toggle is the point of this overlay: <c>api</c> is the demo path, <c>file</c>
    /// reads scenes pushed to the device with Unity/push-dev-scene.ps1 and is what keeps Track C
    /// working before the web DB migration exists. Both are the same
    /// <see cref="ISceneSource"/> behind <see cref="SceneLoader"/>, so switching proves the load
    /// path rather than a second code path.
    /// </summary>
    [AddComponentMenu("Recon/UI/Scene Picker Overlay")]
    [DisallowMultipleComponent]
    public sealed class ScenePickerOverlay : MonoBehaviour
    {
        [SerializeField] bool show = true;
        [SerializeField, Tooltip("Start on the live API, or on scenes already pushed to the device.")]
        bool useApiSource = true;

        [SerializeField, Tooltip("Seeded dev login from web/README.md. Dev only.")]
        string email = "investigator@recon.local";

        ReconApiClient m_client;
        ISceneSource m_apiSource;
        ISceneSource m_fileSource;
        SceneLoader m_loader;

        string m_password = "";
        string m_message = "";
        bool m_busy;
        Vector2 m_scroll;
        IReadOnlyList<SceneSummary> m_scenes = Array.Empty<SceneSummary>();

        public ISceneSource ActiveSource => useApiSource ? m_apiSource : m_fileSource;

        public bool Show { get => show; set => show = value; }

        void Awake()
        {
            var settings = ReconSettings.Instance;

            // InMemory, not PlayerPrefs: a JWT is a bearer credential for a whole case list, and
            // persisting it is a credential at rest (handbook Section 10, FTW-59). A dev who wants
            // the convenience can swap the store here.
            m_client = new ReconApiClient(new InMemorySessionStore());
            m_apiSource = new ApiSceneSource(m_client);
            m_fileSource = new FileSceneSource(
                Path.Combine(Application.persistentDataPath, settings.sceneCacheFolder),
                settings.plyFileName, settings.metadataFileName);

            m_loader = FindFirstObjectByType<SceneLoader>(FindObjectsInactive.Include);
            if (m_loader == null)
                Debug.LogError("[Recon] ScenePickerOverlay found no SceneLoader. Run Recon → Add I2 components to open scene.");
        }

        void OnGUI()
        {
            if (!show) return;

            float w = Mathf.Min(560f, Screen.width - 24f);
            GUILayout.BeginArea(new Rect(12, 80, w, Screen.height - 240), GUI.skin.box);

            GUILayout.Label("<b>RECON — scenes</b>", RichLabel());

            // Source toggle, as a pair of radio buttons: clicking the inactive one switches, and
            // clicking the active one is a no-op rather than leaving no source selected.
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(useApiSource, " API", GUILayout.Width(90)) && !useApiSource) SetSource(true);
            if (GUILayout.Toggle(!useApiSource, " File", GUILayout.Width(90)) && useApiSource) SetSource(false);
            GUILayout.EndHorizontal();
            GUILayout.Label(ActiveSource != null ? ActiveSource.DisplayName : "(no source)", Wrapped());

            if (useApiSource) DrawLogin();

            GUI.enabled = !m_busy;
            if (GUILayout.Button("Refresh READY scenes")) Run(RefreshAsync());
            GUI.enabled = true;

            DrawScenes();

            if (!string.IsNullOrEmpty(m_message)) GUILayout.Label(m_message, Wrapped());
            if (m_loader != null) GUILayout.Label("Loader: " + m_loader.Status, Wrapped());

            GUILayout.EndArea();
        }

        /// <summary>Switches source and drops the listing, which belonged to the other one.</summary>
        void SetSource(bool api)
        {
            useApiSource = api;
            m_scenes = Array.Empty<SceneSummary>();
            m_message = "";
        }

        void DrawLogin()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("email", GUILayout.Width(60));
            email = GUILayout.TextField(email ?? "");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("pass", GUILayout.Width(60));
            m_password = GUILayout.PasswordField(m_password ?? "", '*');
            GUILayout.EndHorizontal();

            // Seeded dev logins, web/README.md: admin@recon.local / investigator@recon.local /
            // viewer@recon.local, passwords <role>-dev. Shown as a hint, never prefilled.
            GUILayout.Label("dev: admin|investigator|viewer@recon.local / <role>-dev", Wrapped());

            GUI.enabled = !m_busy;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_client.Session.HasToken ? "Re-login" : "Login")) Run(LoginAsync());
            if (m_client.Session.HasToken && GUILayout.Button("Logout", GUILayout.Width(90))) m_client.Logout();
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        void DrawScenes()
        {
            if (m_scenes.Count == 0)
            {
                GUILayout.Label("No scenes listed yet.", Wrapped());
                return;
            }

            m_scroll = GUILayout.BeginScrollView(m_scroll, GUILayout.MinHeight(140));
            foreach (var scene in m_scenes)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = !m_busy;
                if (GUILayout.Button("Load", GUILayout.Width(70))) Run(LoadAsync(scene.Id));
                GUI.enabled = true;
                // unitScale is shown because null ("not measured") and 0.0 ("measured, not metric")
                // are both refusals and it saves guessing which one a scene is.
                GUILayout.Label($"{scene.Name}  [{scene.Job?.Status}]  " +
                                (scene.HasUnitScale ? $"unitScale {scene.UnitScale.Value:F6}" : "unitScale not measured"),
                                Wrapped());
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        async Task LoginAsync()
        {
            var response = await m_client.LoginAsync(email, m_password);
            m_password = "";
            m_message = "Logged in as " + response.User;
        }

        async Task RefreshAsync()
        {
            var source = ActiveSource;
            m_scenes = await source.ListReadyAsync();
            m_message = $"{m_scenes.Count} READY scene(s) from {source.DisplayName}";
        }

        async Task LoadAsync(string sceneId)
        {
            if (m_loader == null)
            {
                m_message = "No SceneLoader in the scene.";
                return;
            }
            // SceneLoader never throws: it puts the refusal in its own Status, which is drawn above.
            await m_loader.LoadAsync(sceneId, ActiveSource);
            m_message = "";
        }

        /// <summary>
        /// Fires a task from an IMGUI button. async void is the pragmatic shape for a UI handler in
        /// Unity; what matters is that nothing escapes it, so a failed login shows the server's
        /// message on screen instead of a silently swallowed exception.
        /// </summary>
        async void Run(Task work)
        {
            m_busy = true;
            m_message = "working…";
            try
            {
                await work;
            }
            catch (Exception e)
            {
                m_message = e.Message;
                Debug.LogWarning("[Recon] " + e.Message);
            }
            finally
            {
                m_busy = false;
            }
        }

        static GUIStyle RichLabel()
        {
            return new GUIStyle(GUI.skin.label) { richText = true, fontSize = FontSize(0.10f) };
        }

        static GUIStyle Wrapped()
        {
            return new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = FontSize(0.075f) };
        }

        static int FontSize(float dpiFraction) =>
            Mathf.RoundToInt(Screen.dpi > 0 ? Screen.dpi * dpiFraction : 14f);
    }
}
