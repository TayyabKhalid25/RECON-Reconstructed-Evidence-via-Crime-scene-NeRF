using UnityEngine;
using UnityEngine.XR.ARFoundation;

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

            // TWEAK THIS: Higher divisor = smaller buttons. (e.g. 10 makes it smaller than 5)
            int btnSize = Mathf.Max(40, minDim / 6);
            int margin = 20;

            GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
            btnStyle.fontSize = Mathf.Max(18, minDim / 35);

            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = Mathf.Max(18, minDim / 35);

            // Toggle button in top right
            int btnWidth = (int)(btnSize * 2.0f);
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
                topY += btnSize + 10;
            }

            // AR/VR Camera Toggle
            if (Camera.main != null)
            {
                ARCameraBackground arCamBg = Camera.main.GetComponent<ARCameraBackground>();
                if (arCamBg != null)
                {
                    if (GUI.Button(new Rect(rightColX, topY, btnWidth, btnSize), arCamBg.enabled ? "Cam: AR (On)" : "Cam: VR (Off)", btnStyle))
                    {
                        arCamBg.enabled = !arCamBg.enabled;
                        if (!arCamBg.enabled)
                        {
                            Camera.main.clearFlags = CameraClearFlags.SolidColor;
                            Camera.main.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f); // Dark gray
                        }
                    }
                    topY += btnSize + 10;
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
                // TWEAK THESE to tighten/loosen the grid
                int dpadX = margin + (int)(btnSize * 1.5f);
                int boxHeight = (int)(btnSize * 3.5f);
                int boxWidth = (int)(btnSize * 5.8f);
                int dpadY = h - margin - boxHeight + (int)(btnSize * 1.25f);

                GUI.Box(new Rect(margin, h - margin - boxHeight, boxWidth, boxHeight), "Move (Cam Relative)", boxStyle);

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
                int actionX = dpadX + (int)(btnSize * 2.5f);
                int actionWidth = (int)(btnSize * 1.5f);
                if (GUI.RepeatButton(new Rect(actionX, dpadY - btnSize, actionWidth, btnSize), "Up", btnStyle))
                    splatTransform.position += Vector3.up * moveSpeed;
                if (GUI.RepeatButton(new Rect(actionX, dpadY + btnSize, actionWidth, btnSize), "Dn", btnStyle))
                    splatTransform.position += Vector3.down * moveSpeed;
            }
            else
            {
                // ----- Rotation & Scale Controls -----
                int rotX = margin + (int)(btnSize * 1.5f);
                int boxHeight = (int)(btnSize * 3.5f);
                int boxWidth = (int)(btnSize * 5.8f);
                int rotY = h - margin - boxHeight + (int)(btnSize * 1.25f);

                GUI.Box(new Rect(margin, h - margin - boxHeight, boxWidth, boxHeight), "Rotate & Scale", boxStyle);

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
                int actionX = rotX + (int)(btnSize * 2.5f);
                int actionWidth = (int)(btnSize * 1.5f);
                if (GUI.RepeatButton(new Rect(actionX, rotY - btnSize, actionWidth, btnSize), "Scale +", btnStyle))
                    splatTransform.localScale += Vector3.one * moveSpeed * 0.5f;

                if (GUI.RepeatButton(new Rect(actionX, rotY + btnSize, actionWidth, btnSize), "Scale -", btnStyle))
                {
                    splatTransform.localScale -= Vector3.one * moveSpeed * 0.5f;
                    if (splatTransform.localScale.x < 0.01f)
                        splatTransform.localScale = Vector3.one * 0.01f;
                }
            }
        }
    }
}
