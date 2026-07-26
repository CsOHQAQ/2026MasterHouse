using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    /// <summary>根据 NodeDef 程序化构建节点卡片（背景、标题、信息文字、端口、进度条）。</summary>
    public static class NodeFactory
    {
        private const float HeaderHeight = 0.5f;
        private const float PortStartOffset = 0.9f;  // 端口列距卡片顶部的距离
        private const float PortSpacing = 0.45f;

        public static NodeBase CreateNode(NodeDef def, Vector3 position)
        {
            var go = new GameObject($"Node_{def.displayName}");
            go.transform.position = new Vector3(position.x, position.y, 0f);

            NodeBase node = def switch
            {
                ResourceNodeDef _ => go.AddComponent<ResourceNode>(),
                ProcessorNodeDef _ => go.AddComponent<ProcessorNode>(),
                StorageNodeDef _ => go.AddComponent<StorageNode>(),
                _ => null,
            };
            if (node == null)
            {
                Debug.LogError($"未知的 NodeDef 类型: {def.GetType().Name}");
                Object.Destroy(go);
                return null;
            }

            var col = go.AddComponent<BoxCollider2D>();
            col.size = def.size;
            col.isTrigger = true;

            node.Init(def);
            BuildVisuals(node, def);
            BuildPorts(node, def);
            node.OnConnectionsChanged();
            return node;
        }

        private static void BuildVisuals(NodeBase node, NodeDef def)
        {
            var t = node.transform;
            NewSprite(t, "BG", Vector3.zero,
                new Vector3(def.size.x, def.size.y, 1f), def.cardColor, SortingOrders.Card);

            float headerY = def.size.y * 0.5f - HeaderHeight * 0.5f;
            NewSprite(t, "Header", new Vector3(0f, headerY, 0f),
                new Vector3(def.size.x, HeaderHeight, 1f), Darken(def.cardColor, 0.65f), SortingOrders.CardDecor);
            VisualAssets.CreateWorldText(t, "Title", new Vector3(0f, headerY, 0f),
                def.displayName, 0.3f, TextAnchor.MiddleCenter, Color.white, SortingOrders.Text);

            var info = VisualAssets.CreateWorldText(t, "Info",
                new Vector3(0f, -def.size.y * 0.5f + 0.1f, 0f),
                "", 0.2f, TextAnchor.LowerCenter, new Color(0.92f, 0.92f, 0.92f), SortingOrders.Text);
            node.SetInfoText(info);

            if (node is ProcessorNode proc)
            {
                float barW = def.size.x - 0.7f;
                float barY = def.size.y * 0.5f - HeaderHeight - 0.18f;
                var barRoot = new GameObject("ProgressBar");
                barRoot.transform.SetParent(t, false);
                barRoot.transform.localPosition = new Vector3(0f, barY, 0f);
                NewSprite(barRoot.transform, "BarBG", Vector3.zero,
                    new Vector3(barW, 0.12f, 1f), new Color(0f, 0f, 0f, 0.5f), SortingOrders.CardDecor);
                var fill = NewSprite(barRoot.transform, "BarFill", Vector3.zero,
                    new Vector3(0.0001f, 0.09f, 1f), new Color(0.3f, 0.9f, 0.4f), SortingOrders.Pin);

                var bar = barRoot.AddComponent<ProgressBar>();
                bar.target = proc;
                bar.fill = fill.transform;
                bar.width = barW;
            }
        }

        private static void BuildPorts(NodeBase node, NodeDef def)
        {
            List<ResourceDef> inputs = new(), outputs = new();
            switch (def)
            {
                case ResourceNodeDef r:
                    foreach (var p in r.productions)
                        AddUnique(outputs, p.resource);
                    break;
                case ProcessorNodeDef p:
                    foreach (var rec in p.recipes)
                    {
                        if (rec == null) continue;
                        foreach (var i in rec.inputs) AddUnique(inputs, i.resource);
                        foreach (var o in rec.outputs) AddUnique(outputs, o.resource);
                    }
                    break;
                case StorageNodeDef s:
                    foreach (var r in s.resources)
                    {
                        AddUnique(inputs, r);
                        AddUnique(outputs, r);
                    }
                    break;
            }

            CreatePortColumn(node, inputs, PortDirection.Input, -def.size.x * 0.5f, def.size.y);
            CreatePortColumn(node, outputs, PortDirection.Output, def.size.x * 0.5f, def.size.y);
        }

        private static void CreatePortColumn(NodeBase node, List<ResourceDef> resources,
            PortDirection dir, float x, float cardHeight)
        {
            float startY = cardHeight * 0.5f - PortStartOffset;
            for (int i = 0; i < resources.Count; i++)
            {
                var r = resources[i];
                float y = startY - i * PortSpacing;

                var go = new GameObject($"{dir}_{r.name}");
                go.transform.SetParent(node.transform, false);
                go.transform.localPosition = new Vector3(x, y, 0f);
                go.transform.localScale = new Vector3(0.24f, 0.24f, 1f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, 45f); // 菱形 pin

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = VisualAssets.WhiteSprite;
                sr.sharedMaterial = VisualAssets.UnlitMaterial;
                sr.color = r.color;
                sr.sortingOrder = SortingOrders.Pin;

                var col = go.AddComponent<CircleCollider2D>();
                col.radius = 0.85f; // 世界半径约 0.2，比 pin 略大便于点选
                col.isTrigger = true;

                var port = go.AddComponent<Port>();
                port.Init(node, r, dir);
                node.RegisterPort(port);

                // pin 标签放在卡片内侧（作为卡片子物体，避免继承 pin 的缩放旋转）
                bool isInput = dir == PortDirection.Input;
                float labelX = x + (isInput ? 0.22f : -0.22f);
                VisualAssets.CreateWorldText(node.transform, $"PinLabel_{r.name}",
                    new Vector3(labelX, y, 0f), r.displayName, 0.17f,
                    isInput ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight,
                    r.color, SortingOrders.Text);
            }
        }

        private static void AddUnique(List<ResourceDef> list, ResourceDef r)
        {
            if (r != null && !list.Contains(r)) list.Add(r);
        }

        private static SpriteRenderer NewSprite(Transform parent, string name,
            Vector3 localPos, Vector3 localScale, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = VisualAssets.WhiteSprite;
            sr.sharedMaterial = VisualAssets.UnlitMaterial;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        private static Color Darken(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);
    }
}
