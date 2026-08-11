using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// View 总协调器（§2.1）：订阅 Manager 结构变化广播，维护数据对象 → 表现物的映射。
    /// 读档/Load 一律从纯数据全量重建（真相源）；广播只做运行中的增量创建/销毁。
    /// View 只读：本类只生成/销毁表现物，不修改任何数据（§2、§11.4）。
    /// </summary>
    public class ViewManager : MonoBehaviour
    {
        [Tooltip("画布格管理；留空时自动在本物体上补挂")]
        public GridManager gridManager;

        // 映射仅做键查询，不做枚举遍历
        private readonly Dictionary<LevelData, Transform> levelContainers =
            new Dictionary<LevelData, Transform>();
        private readonly Dictionary<NodeData, NodeGO> nodeViews =
            new Dictionary<NodeData, NodeGO>();
        private readonly Dictionary<LinkData, LinkGO> linkViews =
            new Dictionary<LinkData, LinkGO>();

        private LevelManager levelManager;
        private LinkManager linkManager;

        private void Start()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                Debug.LogError("场景缺少 GameManager，ViewManager 停用");
                enabled = false;
                return;
            }

            if (gridManager == null)
            {
                gridManager = GetComponent<GridManager>();
                if (gridManager == null)
                    gridManager = gameObject.AddComponent<GridManager>();
            }

            levelManager = gm.LevelManager;
            linkManager = gm.LinkManager;
            levelManager.OnLevelLoaded += HandleLevelLoaded;
            levelManager.OnLevelUnloaded += HandleLevelUnloaded;
            levelManager.OnNodePlaced += HandleNodePlaced;
            levelManager.OnNodeRemoved += HandleNodeRemoved;
            // OnNodeMoved 无需订阅：NodeGO 每帧读 Origin 自行跟随（§2.1 连续量轮询）
            linkManager.OnLinkCreated += HandleLinkCreated;
            linkManager.OnLinkDeleted += HandleLinkDeleted;

            // GameManager.Start 可能先于本 Start 加载了 startLevel（脚本执行顺序未约定），补建表现
            foreach (var level in levelManager.LoadedLevels)
                HandleLevelLoaded(level);
        }

        private void OnDestroy()
        {
            if (levelManager != null)
            {
                levelManager.OnLevelLoaded -= HandleLevelLoaded;
                levelManager.OnLevelUnloaded -= HandleLevelUnloaded;
                levelManager.OnNodePlaced -= HandleNodePlaced;
                levelManager.OnNodeRemoved -= HandleNodeRemoved;
            }
            if (linkManager != null)
            {
                linkManager.OnLinkCreated -= HandleLinkCreated;
                linkManager.OnLinkDeleted -= HandleLinkDeleted;
            }
        }

        // ───────────────── 关卡 Load/Unload：全量重建 / 全量销毁 ─────────────────

        private void HandleLevelLoaded(LevelData level)
        {
            if (levelContainers.ContainsKey(level)) return;

            var container = new GameObject($"关卡视图_{level.Def.name}").transform;
            container.SetParent(transform, false);
            levelContainers.Add(level, container);

            gridManager.ShowCanvas(level);

            // 全量重建是真相源（§2.1）：从纯数据重建全部表现物
            foreach (var node in level.Nodes)
                CreateNodeView(level, node);
            foreach (var link in level.Links)
                CreateLinkView(level, link);
        }

        private void HandleLevelUnloaded(LevelData level)
        {
            if (!levelContainers.TryGetValue(level, out var container)) return;
            levelContainers.Remove(level);

            // 数据仍在（Unload 不清 LevelData），只销毁表现物与映射
            foreach (var node in level.Nodes)
                nodeViews.Remove(node);
            foreach (var link in level.Links)
                linkViews.Remove(link);

            Destroy(container.gameObject);
            gridManager.HideCanvas(level);
        }

        // ───────────────── 结构变化增量响应 ─────────────────

        private void HandleNodePlaced(LevelData level, NodeData node) =>
            CreateNodeView(level, node);

        private void HandleNodeRemoved(LevelData level, NodeData node)
        {
            if (!nodeViews.TryGetValue(node, out var view)) return;
            nodeViews.Remove(node);
            Destroy(view.gameObject);
        }

        private void HandleLinkCreated(LevelData level, LinkData link) =>
            CreateLinkView(level, link);

        private void HandleLinkDeleted(LevelData level, LinkData link)
        {
            if (!linkViews.TryGetValue(link, out var view)) return;
            linkViews.Remove(link);
            Destroy(view.gameObject);
        }

        // ───────────────── 表现物工厂 ─────────────────

        private void CreateNodeView(LevelData level, NodeData node)
        {
            if (nodeViews.ContainsKey(node)) return;
            if (!levelContainers.TryGetValue(level, out var container)) return;

            var go = new GameObject($"节点_{node.Def.name}_{node.NodeId}");
            go.transform.SetParent(container, false);
            var view = go.AddComponent<NodeGO>();
            view.Bind(node);
            nodeViews.Add(node, view);
        }

        private void CreateLinkView(LevelData level, LinkData link)
        {
            if (linkViews.ContainsKey(link)) return;
            if (!levelContainers.TryGetValue(level, out var container)) return;

            var go = new GameObject($"链接_{link.LinkId}");
            go.transform.SetParent(container, false);
            var view = go.AddComponent<LinkGO>();
            view.Bind(link);
            linkViews.Add(link, view);
        }
    }
}
