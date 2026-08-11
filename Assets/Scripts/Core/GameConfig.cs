using UnityEngine;

namespace MasterHouse
{
    [CreateAssetMenu(fileName = "游戏设置", menuName = "GameConfig", order = 2)]
    public class GameConfig : ScriptableObject
    {
        private const string AssetPath = "GameConfig/游戏设置"; // Resources 下的路径

        private static GameConfig _instance;
        public static GameConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<GameConfig>(AssetPath);
                    if (_instance == null)
                        Debug.LogError($"未找到配置资产 Resources/{AssetPath}");
                }
                return _instance;
            }
        }

        [Header("表现")]
        [Tooltip("一格的世界尺寸，View 层换算用；逻辑层只用格坐标")]
        public float GridSize = 1f;

        [Header("时间")]
        [Tooltip("待定 #5：tick 频率，暂按 10 tick/秒")]
        public int TicksPerSecond = 10;

        [Header("局外时间（§16.4）")]
        [Tooltip("待定 #18：局外时钟倍率——每游戏分钟对应的 tick 数。10 tick/秒 ÷ 10 = 「现实 1 秒 = 游戏 1 分钟」的原型节奏")]
        public int HouseTicksPerGameMinute = 10;

        [Header("链接默认参数（待定 #4：先用全局默认）")]
        [Tooltip("节拍：链接每 N tick 发起一次取货")]
        public int DefaultLinkBeatTicks = 10;

        [Tooltip("在途时长（tick）；是否与线长相关：待定 #4")]
        public int DefaultLinkTransitTicks = 10;

        [Header("待定 #4 实验项（策划 A/B 用，定案后清理）")]
        [Tooltip("在途时长按线长计算：默认关（维持全局固定值）。只影响新建链接，切换后重载关卡刷新全部链接")]
        public bool TransitTicksByLength = false;

        [Tooltip("开关开启时每个途径格的在途 tick 数（线长口径 = 途径格数，含两端格）")]
        public int TransitTicksPerCell = 2;
    }
}