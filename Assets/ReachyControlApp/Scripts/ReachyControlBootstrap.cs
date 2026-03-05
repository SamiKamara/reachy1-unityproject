using UnityEngine;

namespace Reachy.ControlApp
{
    public static class ReachyControlBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeControlUI()
        {
            ReachyRuntimeControlUI existing = Object.FindObjectOfType<ReachyRuntimeControlUI>();
            if (existing != null)
            {
                return;
            }

            var go = new GameObject("ReachyRuntimeControlUI");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ReachyRuntimeControlUI>();
        }
    }
}
