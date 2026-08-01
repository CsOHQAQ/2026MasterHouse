using UnityEngine;

namespace MasterHouse
{
    [CreateAssetMenu(fileName = "游戏设置", menuName = "GameConfig", order = 2)]
    public class GameConfig : ScriptableObject
    {
        private const string AssetPath = "GameConfig"; // Resources 下的路径

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

        public float GridSize = 1f;
        
    }
}