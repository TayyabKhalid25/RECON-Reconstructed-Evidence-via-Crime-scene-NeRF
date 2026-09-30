using UnityEngine;

namespace RECON.Performance
{
    public class SplatControllerHUD : MonoBehaviour
    {
        [Tooltip("Assign multiple Splat GameObjects here to toggle between them.")]
        public GameObject[] splats;
        
        private Transform splatTransform;
        
        private int currentSplatIndex = 0;
        
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Vector3 initialScale;

        private bool showMenu = true;

        void Start()
        {
            if (splats == null || splats.Length == 0)
            {
                Debug.LogError("SplatControllerHUD: No Splats assigned! Please assign at least one Splat GameObject to the Splats array in the Inspector.");
                return;
            }

            for (int i = 0; i < splats.Length; i++)
            {
                if (splats[i] != null) splats[i].SetActive(i == currentSplatIndex);
            }
            
            if (splats[currentSplatIndex] != null)
            {
                splatTransform = splats[currentSplatIndex].transform;
                initialPosition = splatTransform.position;
                initialRotation = splatTransform.rotation;
                initialScale = splatTransform.localScale;
            }
        }

        void OnGUI()
        {
            if (splatTransform == null) return;

            int w = Screen.width;
            int h = Screen.height;
            
            // Scale UI for high-DPI mobile screens
            int btnSize = Mathf.Max(60, h / 12);
            int margin = 20;

            GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
            btnStyle.fontSize = Mathf.Max(20, h / 40);
            
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = Mathf.Max(20, h / 40);

            // Toggle button in top right
            if (GUI.Button(new Rect(w - btnSize * 3 - margin, margin, btnSize * 3, btnSize), showMenu ? "Hide Controls" : "Show Controls", btnStyle))
            {
                showMenu = !showMenu;
            }

            if (!showMenu) return;

            // Reset Button below Toggle
            if (GUI.Button(new Rect(w - btnSize * 3 - margin, margin + btnSize + 10, btnSize * 3, btnSize), "Recenter Splat", btnStyle))
            {
                splatTransform.position = initialPosition;
                splatTransform.rotation = initialRotation;
                splatTransform.localScale = initialScale;
            }

            // Splat Selector Button
            if (splats != null && splats.Length > 1)
            {
                if (GUI.Button(new Rect(w - btnSize * 3 - margin, margin + btnSize * 2 + 20, btnSize * 3, btnSize), $"Next Splat ({currentSplatIndex + 1}/{splats.Length})", btnStyle))
                {
                    if (splats[currentSplatIndex] != null) splats[currentSplatIndex].SetActive(false);
                    currentSplatIndex = (currentSplatIndex + 1) % splats.Length;
                    if (splats[currentSplatIndex] != null)
                    {
                        splats[currentSplatIndex].SetActive(true);
                        splatTransform = splats[currentSplatIndex].transform;
                        splatTransform.position = initialPosition;
                        splatTransform.rotation = initialRotation;
                        splatTransform.localScale = initialScale;
                    }
                }
            }

            // Time independent movement
            float moveSpeed = 1.0f * Time.unscaledDeltaTime;
            float rotSpeed = 45.0f * Time.unscaledDeltaTime;

            // Calculate camera-relative directions
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            if (cam != null)
            {
                forward = cam.forward;
                forward.y = 0; // Keep movement horizontal
                if (forward.sqrMagnitude > 0.001f)
                    forward.Normalize();
                else
                    forward = Vector3.forward;

                right = cam.right;
                right.y = 0;
                if (right.sqrMagnitude > 0.001f)
                    right.Normalize();
                else
                    right = Vector3.right;
            }

            // ----- Translation D-PAD (Bottom Left) -----
            int dpadX = margin + btnSize;
            int dpadY = h - margin - btnSize * 3;

            GUI.Box(new Rect(margin, dpadY - (int)(btnSize * 1.5f), btnSize * 4.5f, btnSize * 4.5f), "Move (Cam Relative)", boxStyle);

            // Forward (Push Away)
            if (GUI.RepeatButton(new Rect(dpadX, dpadY - btnSize, btnSize, btnSize), "^", btnStyle)) 
                splatTransform.position += forward * moveSpeed;
            
            // Back (Pull Closer)
            if (GUI.RepeatButton(new Rect(dpadX, dpadY + btnSize, btnSize, btnSize), "v", btnStyle)) 
                splatTransform.position -= forward * moveSpeed;

            // Left
            if (GUI.RepeatButton(new Rect(dpadX - btnSize, dpadY, btnSize, btnSize), "<", btnStyle)) 
                splatTransform.position -= right * moveSpeed;

            // Right
            if (GUI.RepeatButton(new Rect(dpadX + btnSize, dpadY, btnSize, btnSize), ">", btnStyle)) 
                splatTransform.position += right * moveSpeed;
                
            // Up/Down (Y Axis)
            if (GUI.RepeatButton(new Rect(dpadX + btnSize * 2.2f, dpadY - btnSize, btnSize * 1.2f, btnSize), "Up", btnStyle))
                splatTransform.position += Vector3.up * moveSpeed;
            if (GUI.RepeatButton(new Rect(dpadX + btnSize * 2.2f, dpadY + btnSize, btnSize * 1.2f, btnSize), "Dn", btnStyle))
                splatTransform.position += Vector3.down * moveSpeed;


            // ----- Rotation & Scale Controls (Bottom Right) -----
            int rotX = w - margin - btnSize * 4;
            int rotY = h - margin - btnSize * 3;

            GUI.Box(new Rect(rotX, rotY - (int)(btnSize * 1.5f), btnSize * 4, btnSize * 4.5f), "Rotate & Scale", boxStyle);

            // Rotate (Yaw)
            if (GUI.RepeatButton(new Rect(rotX, rotY - btnSize/2, btnSize * 1.5f, btnSize * 1.5f), "Rot L", btnStyle))
                splatTransform.Rotate(Vector3.up, rotSpeed, Space.World);

            if (GUI.RepeatButton(new Rect(rotX + btnSize * 2, rotY - btnSize/2, btnSize * 1.5f, btnSize * 1.5f), "Rot R", btnStyle))
                splatTransform.Rotate(Vector3.up, -rotSpeed, Space.World);

            // Scale
            if (GUI.RepeatButton(new Rect(rotX, rotY + btnSize * 1.2f, btnSize * 1.5f, btnSize), "Scale +", btnStyle))
                splatTransform.localScale += Vector3.one * moveSpeed * 0.5f;

            if (GUI.RepeatButton(new Rect(rotX + btnSize * 2, rotY + btnSize * 1.2f, btnSize * 1.5f, btnSize), "Scale -", btnStyle))
            {
                splatTransform.localScale -= Vector3.one * moveSpeed * 0.5f;
                // Prevent negative or zero scale
                if (splatTransform.localScale.x < 0.01f) 
                    splatTransform.localScale = Vector3.one * 0.01f;
            }
        }
    }
}
