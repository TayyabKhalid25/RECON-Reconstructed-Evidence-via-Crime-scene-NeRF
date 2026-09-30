using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace RECON.Performance
{
    public class MobilePerformanceHUD : MonoBehaviour
    {
        private float deltaTime = 0.0f;
        private List<float> frameTimes = new List<float>();
        private const int maxFrames = 300; // About 5 seconds of frame history at 60fps

        void Update()
        {
            deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
            
            frameTimes.Add(Time.unscaledDeltaTime);
            if (frameTimes.Count > maxFrames)
            {
                frameTimes.RemoveAt(0);
            }
        }

        void OnGUI()
        {
            int w = Screen.width, h = Screen.height;
            GUIStyle style = new GUIStyle();
            
            // Position the HUD slightly lower so it doesn't overlap with standard safe areas/notches
            Rect rect = new Rect(40, 100, w, h * 2 / 100);
            style.alignment = TextAnchor.UpperLeft;
            style.fontSize = Mathf.Max(24, h * 2 / 50); // Ensure it's readable on high DPI screens
            style.normal.textColor = Color.green;

            float fps = 1.0f / deltaTime;

            // Calculate 1% low
            float onePercentLowFps = fps;
            if (frameTimes.Count > 0)
            {
                var sortedTimes = frameTimes.OrderByDescending(t => t).ToList(); // Longest frame times first
                int onePercentIndex = Mathf.Max(0, (int)(sortedTimes.Count * 0.01f));
                float onePercentLowTime = sortedTimes[onePercentIndex];
                onePercentLowFps = 1.0f / (onePercentLowTime > 0 ? onePercentLowTime : 0.01f);
            }

            // Battery - SystemInfo returns -1 if the device doesn't support battery level reading
            float batteryPct = SystemInfo.batteryLevel * 100f;
            string batteryStatus = SystemInfo.batteryStatus.ToString();
            
            long systemRam = SystemInfo.systemMemorySize;
            long graphicsRam = SystemInfo.graphicsMemorySize;

            string text = string.Format("FPS: {0:0.} (1% Low: {1:0.})\nBattery: {2:0.0}% ({3})\nSys RAM: {4} MB\nVRAM: {5} MB", 
                fps, onePercentLowFps, batteryPct > 0 ? batteryPct.ToString("0.0") : "N/A", batteryStatus, systemRam, graphicsRam);
                
            // Drop shadow for readability against splat backgrounds
            GUIStyle shadowStyle = new GUIStyle(style);
            shadowStyle.normal.textColor = Color.black;
            Rect shadowRect = new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height);
            
            GUI.Label(shadowRect, text, shadowStyle);
            GUI.Label(rect, text, style);
        }
    }
}
