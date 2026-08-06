using UnityEngine;

namespace MasterPotion
{
    /// <summary>运行时家具实例：一条配置表行在房间内的一次摆放。</summary>
    public sealed class FurnitureRuntimeItem
    {
        public string Id;
        public FurnitureEntry Entry;
        /// <summary>所在网格 id（基础网格或宿主桌面网格）。</summary>
        public string GridId;
        public int Col;
        public int Row;
        public GameObject Root;
        public SpriteRenderer Renderer;

        public bool IsOnTableGrid => GridId != null && GridId.StartsWith("tbl_");
    }
}
