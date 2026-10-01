// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;


namespace Gsplat
{
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public static class GsplatPlayerLoopHook
    {
#if UNITY_EDITOR
        static GsplatPlayerLoopHook()
        {
            Install();
            EditorApplication.playModeStateChanged += _ => { Install(); };
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RuntimeInit()
        {
            // Entering play mode can reset Unity's player loop without resetting managed
            // statics when domain reload is disabled.
            Install();
        }

        static void Install()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (Contains(ref loop, typeof(GsplatPlayerLoopHook)))
                return;

            if (!InsertBefore<PostLateUpdate>(ref loop, typeof(GsplatPlayerLoopHook), Update))
            {
                Debug.LogError("[Gsplat] Could not install the Gaussian-splat player-loop hook.");
                return;
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        static bool Contains(ref PlayerLoopSystem root, System.Type type)
        {
            if (root.type == type)
                return true;
            if (root.subSystemList == null)
                return false;
            for (var i = 0; i < root.subSystemList.Length; ++i)
                if (Contains(ref root.subSystemList[i], type))
                    return true;
            return false;
        }

        static bool InsertBefore<T>(ref PlayerLoopSystem root, System.Type type, PlayerLoopSystem.UpdateFunction fn)
        {
            if (root.subSystemList == null)
                return false;

            for (var i = 0; i < root.subSystemList.Length; i++)
            {
                ref var sys = ref root.subSystemList[i];
                if (sys.type == typeof(T))
                {
                    var list = sys.subSystemList?.ToList() ?? new List<PlayerLoopSystem>();
                    list.Insert(0, new PlayerLoopSystem { type = type, updateDelegate = fn });
                    sys.subSystemList = list.ToArray();
                    return true;
                }

                if (InsertBefore<T>(ref sys, type, fn))
                    return true;
            }

            return false;
        }

        static void Update()
        {
            // Camera controllers commonly finalize their transform in LateUpdate. Synchronize
            // the proxy now, then let Unity render its target-texture camera through the normal
            // camera loop before the Game camera. Manual SRP render requests can produce a
            // different result from the normal Play-mode camera path.
            GsplatRelighting.SynchronizeGameCameras();
            GsplatSorter.Instance.Update();
        }
    }
}
