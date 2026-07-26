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
        [Tooltip("场景启动时生成的固定节点（如资源产出点）")]
        public List<PresetNode> presetNodes = new();

        private void Start()
        {
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

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
                if (p.def != null) NodeFactory.CreateNode(p.def, p.position);
            }
        }
    }
}
