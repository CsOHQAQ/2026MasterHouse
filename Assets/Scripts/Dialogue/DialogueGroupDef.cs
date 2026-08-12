using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 对话组（Model 层，运行时只读；设计说明 §4.4）：一个对话组一个 SO 资产，
    /// 存放于 Assets/GameData/Dialogue/&lt;种族&gt;/。
    ///
    /// 选独立资产而非 sub-asset 的理由：diff 友好、多人并行写台词不冲突、
    /// Unity 原生的拖拽引用与「查找引用」直接可用。配置时的聚合浏览由对话编辑器窗口解决（§11.2），
    /// 不靠文件结构。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/对话组", fileName = "DialogueGroup")]
    public sealed class DialogueGroupDef : ScriptableObject
    {
        [Tooltip("稳定键（日志与校验用；不参与玩法逻辑）")]
        public string id;

        [TextArea(1, 3)]
        [Tooltip("备注：这组在讲什么。仅编辑器可见，不进游戏")]
        public string note;

        [Tooltip("按顺序播放的步骤。分支可出现在任意位置（§4.3）")]
        public List<DialogueStep> steps = new List<DialogueStep>();

        /// <summary>展示名：优先 id，回落资产名（编辑器窗口与日志用）。</summary>
        public string DisplayId => string.IsNullOrEmpty(id) ? name : id;
    }
}
