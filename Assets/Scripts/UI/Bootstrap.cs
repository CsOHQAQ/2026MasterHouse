using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MasterPotion
{
    /// <summary>启动入口：保证 EventSystem 存在、应用配置、构建工具栏、生成固定资源节点。</summary>
    public class Bootstrap : MonoBehaviour
    {
        [Serializable]
        public class PresetNode
        {
            public NodeDef def;
            public Vector2 position;
        }

        public GameConfig config;
        [Tooltip("场景启动时生成的固定节点（如资源产出点），位置会吸附到画布单元格")]
        public List<PresetNode> presetNodes = new();

        private void Start()
        {
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            // 旧场景兜底：保证画布与画布编辑器存在
            if (BoardGrid.Instance == null) gameObject.AddComponent<BoardGrid>();
            if (BoardEditController.Instance == null) gameObject.AddComponent<BoardEditController>();

            if (config != null)
            {
                if (LinkManager.Instance != null)
                    LinkManager.Instance.transferInterval = config.linkTransferInterval;
                PaletteUI.Build(config);
            }
            else
            {
                Debug.LogWarning("Bootstrap 未指定 GameConfig，工具栏不会生成。");
            }

            foreach (var p in presetNodes)
            {
                if (p.def == null) continue;
                var origin = BoardGrid.SnapOrigin(p.position, p.def.gridSize);
                if (BoardGrid.Instance.CanPlace(origin, p.def.gridSize))
                    NodeFactory.CreateNodeAt(p.def, origin);
                else
                    Debug.LogWarning($"预置节点「{p.def.displayName}」在 {origin} 处无法完整落在画布内，已跳过。");
            }

            // web-demo 局外界面：最后创建，确保覆盖节点玩法的运行时工具栏。
            OutGameUI.Build();
        }
    }
}
