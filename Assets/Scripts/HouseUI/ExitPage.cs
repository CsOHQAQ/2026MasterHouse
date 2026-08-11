using UnityEngine;

namespace MasterHouse
{
    /// <summary>退出确认页：确认后结束运行（编辑器内退出 Play）。</summary>
    public sealed class ExitPage : PaperPage<OutGameExitPageView>
    {
        protected override string PrefabPath => OutGamePrefabResourcePaths.ExitPage;

        protected override void OnBind()
        {
            View.confirmButton.onClick.RemoveAllListeners();
            View.confirmButton.onClick.AddListener(QuitGame);
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
