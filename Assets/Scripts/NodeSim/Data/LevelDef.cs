using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>小关的唯一定义入口（§8.1）。</summary>
    [CreateAssetMenu(fileName = "关卡", menuName = "MasterHouse/关卡定义", order = 30)]
    public class LevelDef : ScriptableObject
    {
        [Tooltip("画布形状——内联 GridGroup，已取消独立 CanvasDef 类（§8.1）")]
        public GridGroup Canvas = new GridGroup();

        [Tooltip("本关画布在大场景全局网格中的原点（多小关同场景的前提）")]
        public Vector2Int WorldOrigin;

        [Tooltip("预置节点：资源点、中转节点靠它预置")]
        public List<PresetNodeEntry> PresetNodes = new List<PresetNodeEntry>();

        [Tooltip("本关可建列表：数量上限是关卡难度的主要调节旋钮（§8.3 v1：建造免费 + 上限）")]
        public List<BuildableNodeEntry> BuildableNodes = new List<BuildableNodeEntry>();

        [Tooltip("待定 #1：Goals 结算机制策划未定案，仅保留结构位，勿实现具体结算")]
        public LevelGoalsDef Goals = new LevelGoalsDef();

        [Tooltip("待定 #1：解锁条件形式随 Goals 一起定案，占位")]
        public UnlockRequirementDef UnlockRequirement = new UnlockRequirementDef();
    }

    [Serializable]
    public class PresetNodeEntry
    {
        public NodeDef Node;

        [Tooltip("放置格：画布局部坐标（相对 WorldOrigin），Load 时换算为全局格")]
        public Vector2Int Cell;

        public bool CanMove;
        public bool CanDelete;
    }

    [Serializable]
    public class BuildableNodeEntry
    {
        public NodeDef Node;
        public int MaxCount = 1;
    }

    /// <summary>待定 #1：Goals 占位结构，字段随策划定案补充。</summary>
    [Serializable]
    public class LevelGoalsDef
    {
    }

    /// <summary>待定 #1：解锁条件占位结构。</summary>
    [Serializable]
    public class UnlockRequirementDef
    {
    }
}