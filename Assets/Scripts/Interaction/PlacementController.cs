using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 节点放置 Controller（§2/§9）。
    /// 只把输入翻译成对 Manager 的调用，不直接修改任何数据类（§2）。
    /// </summary>
    public class PlacementController : MonoBehaviour
    {
        private void Update()
        {
            // TODO：
            // - 从可建列表进入放置模式（v1 建造免费 + 数量上限 §8.3：LevelManager.CanBuild）
            // - 幽灵预览吸附网格，逐格合法性反馈（LevelManager.CanPlaceNode）
            // - 确认放置 → LevelManager.PlaceNode；取消退出
        }
    }
}