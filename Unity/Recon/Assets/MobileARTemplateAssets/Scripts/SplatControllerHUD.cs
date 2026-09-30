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
        
        private enum ControlMode { Move, RotateScale }
        private ControlMode currentMode = ControlMode.Move;

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
            
            // Scale UI for high-DPI mobile screens based on the smallest dimension
            int minDim = Mathf.Min(w, h);
            int btnSize = Mathf.Max(50, minDim / 10);
            int margin = 20;

            GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
            btnStyle.fontSize = Mathf.Max(18, minDim / 30);
            
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = Mathf.Max(18, minDim / 30);

            // Toggle button in top right
            int btnWidth = (int)(btnSize * 3.5f);
            int rightColX = w - btnWidth - margin;
            int topY = margin;

            if (GUI.Button(new Rect(rightColX, topY, btnWidth, btnSize), showMenu ? "Hide Controls" : "Show Controls", btnStyle))
            {
                showMenu = !showMenu;
            }

            if (!showMenu) return;
            topY += btnSize + 10;
            
            // Mode Toggle
            if (GUI.Button(new Rect(rightColX, topY, btnWidth, btnSize), currentMode == ControlMode.Move ? "Mode: Move" : "Mode: Rotate", btnStyle))
            {
                currentMode = currentMode == ControlMode.Move ? ControlMode.RotateScale : ControlMode.Move;
            }
            topY += btnSize + 10;

            // Reset Button
            if (GUI.Button(new Rect(rightColX, topY, btnWidth, btnSize), "Recenter Splat", btnStyle))
            {
                splatTransform.position = initialPosition;
                splatTransform.rotation = initialRotation;
                splatTransform.localScale = initialScale;
            }
            topY += btnSize + 10;

            // Splat Selector Button
            if (splats != null && splats.Length > 1)
            {
                if (GUI.Button(new Rect(rightColX, topY, btnWidth, btnSize), $"Next Splat ({currentSplatIndex + 1}/{splats.Length})", btnStyle))
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

            if (currentMode == ControlMode.Move)
            {
                // ----- Translation D-PAD -----
                int dpadX = margin + btnSize * 2;
                int dpadY = h - margin - (int)(btnSize * 2.5f);

                GUI.Box(new Rect(margin, dpadY - (int)(btnSize * 1.5f), btnSize * 4.5f, btnSize * 4f), "Move (Cam Relative)", boxStyle);

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
                if (GUI.RepeatButton(new Rect(dpadX + btnSize * 2.5f, dpadY - btnSize, btnSize * 1.5f, btnSize), "Up", btnStyle))
                    splatTransform.position += Vector3.up * moveSpeed;
                if (GUI.RepeatButton(new Rect(dpadX + btnSize * 2.5f, dpadY + btnSize, btnSize * 1.5f, btnSize), "Dn", btnStyle))
                    splatTransform.position += Vector3.down * moveSpeed;
            }
            else
            {
                // ----- Rotation & Scale Controls -----
                int rotX = margin + btnSize * 2;
                int rotY = h - margin - (int)(btnSize * 2.5f);

                GUI.Box(new Rect(margin, rotY - (int)(btnSize * 1.5f), btnSize * 5f, btnSize * 4f), "Rotate & Scale", boxStyle);

                // Rotate (Pitch - Up/Down relative to camera view)
                if (GUI.RepeatButton(new Rect(rotX, rotY - btnSize, btnSize, btnSize), "Up", btnStyle))
                    splatTransform.Rotate(right, rotSpeed, Space.World);

                if (GUI.RepeatButton(new Rect(rotX, rotY + btnSize, btnSize, btnSize), "Dn", btnStyle))
                    splatTransform.Rotate(right, -rotSpeed, Space.World);

                // Rotate (Yaw - Left/Right)
                if (GUI.RepeatButton(new Rect(rotX - btnSize, rotY, btnSize, btnSize), "L", btnStyle))
                    splatTransform.Rotate(Vector3.up, rotSpeed, Space.World);

                if (GUI.RepeatButton(new Rect(rotX + btnSize, rotY, btnSize, btnSize), "R", btnStyle))
                    splatTransform.Rotate(Vector3.up, -rotSpeed, Space.World);

                // Scale
                if (GUI.RepeatButton(new Rect(rotX + btnSize * 2.5f, rotY - btnSize, btnSize * 1.5f, btnSize), "Scale +", btnStyle))
                    splatTransform.localScale += Vector3.one * moveSpeed * 0.5f;

                if (GUI.RepeatButton(new Rect(rotX + btnSize * 2.5f, rotY + btnSize, btnSize * 1.5f, btnSize), "Scale -", btnStyle))
                {
                    splatTransform.localScale -= Vector3.one * moveSpeed * 0.5f;
                    if (splatTransform.localScale.x < 0.01f) 
                        splatTransform.localScale = Vector3.one * 0.01f;
                }
            }
        }
    }
}
