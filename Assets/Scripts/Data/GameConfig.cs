using System.Collections.Generic;
using UnityEngine;

namespace MasterPotion
{
    [CreateAssetMenu(menuName = "MasterPotion/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Tooltip("每条链接每隔多少秒运送 1 件资源")]
        public float linkTransferInterval = 1f;
        [Tooltip("底部工具栏中可供玩家放置的节点")]
        public List<NodeDef> buildableNodes = new();
    }
}
