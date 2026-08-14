using UnityEngine;

namespace MasterHouse
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
        /// <summary>左右镜像摆放（F 键切换；随会话/初始摆放持久）。</summary>
        public bool Flipped;
        public GameObject Root;
        public SpriteRenderer Renderer;
        /// <summary>地面投影（柔和椭圆，宽度随家具显示宽；壁挂与可叠放件无投影）。</summary>
        public SpriteRenderer Shadow;

        public bool IsOnTableGrid => GridId != null && GridId.StartsWith("tbl_");
    }
}
