using UnityEditor;

namespace MasterHouse
{
    /// <summary>
    /// 快速进 Play（尤其是关闭 Domain Reload）时，RuntimeInitializeOnLoadMethod 不会为新脚本补跑。
    /// 这里确保开发者切场景面板在当前 Play 会话和后续每次进 Play 时都被创建。
    /// </summary>
    [InitializeOnLoad]
    internal static class DeveloperSceneSwitcherPlayModeBootstrap
    {
        static DeveloperSceneSwitcherPlayModeBootstrap()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (EditorApplication.isPlaying)
                EditorApplication.delayCall += EnsureInPlayMode;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                EditorApplication.delayCall += EnsureInPlayMode;
        }

        private static void EnsureInPlayMode()
        {
            if (EditorApplication.isPlaying)
                DeveloperSceneSwitcher.EnsureInstance();
        }
    }
}
