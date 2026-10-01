// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using UnityEditor;

namespace Gsplat.Editor
{
    static class GsplatProjectBootstrap
    {
        [InitializeOnLoadMethod]
        static void EnsureRuntimeSettingsExist()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
                    _ = GsplatSettings.Instance;
            };
        }
    }
}
