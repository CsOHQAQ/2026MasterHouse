using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 局外玩法启动入口（挂在 OutGameTest 场景，承接局外初始化）。
    /// 旧分支靠 RuntimeInitializeOnLoadMethod 在任意场景自动注入局外 UI，主线已停用该路径；
    /// 现在只有放了本脚本的场景会显式拉起局外玩法，与局内 TestScene 互不干扰（架构设计 §16.10）。
    /// </summary>
    public sealed class OutGameBootstrap : MonoBehaviour
    {
        [Tooltip("同时创建 HouseGmConsole（F1 开关的 GM 面板），冒烟时可用「恢复初始态」")]
        [SerializeField] private bool spawnGmConsole = true;

        private void Start()
        {
            // 局外时钟由 GameManager 的全局固定 tick 驱动（§16.4）；测试场景保持只含 Bootstrap（§16.10），
            // 缺 GameManager 时代码创建（startLevel 为空，局内侧不加载任何小关）
            if (GameManager.Instance == null && FindObjectOfType<GameManager>() == null)
            {
                var gm = new GameObject("GameManager", typeof(GameManager));
                DontDestroyOnLoad(gm); // 与局外 UI 同寿命（OutGameUI 自身是 DontDestroyOnLoad）
            }

            OutGameUI.Build();

            // 旧分支的 GM 面板与局外 UI 一同自动注入，点亮基线保持同样组合
            if (spawnGmConsole && FindObjectOfType<HouseGmConsole>() == null)
            {
                var go = new GameObject("HouseGmConsole", typeof(HouseGmConsole));
                DontDestroyOnLoad(go);
            }
        }
    }
}
