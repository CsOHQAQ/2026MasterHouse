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

        [Header("链接默认参数（待定 #4：先用全局默认）")]
        [Tooltip("节拍：链接每 N tick 发起一次取货")]
        public int DefaultLinkBeatTicks = 10;

        [Tooltip("在途时长（tick）；是否与线长相关：待定 #4")]
        public int DefaultLinkTransitTicks = 10;
    }
}