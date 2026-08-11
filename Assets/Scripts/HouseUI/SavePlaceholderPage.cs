using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 存档占位页（§16.5 明示豁免）：重写期间存档功能移除，「新游戏/读取存档」不再走槽位选择。
    /// 复用纸张页 Prefab 展示说明文案；待定 #9 统一存档定案后由完整存档页取代。
    /// </summary>
    public sealed class SavePlaceholderPage : PaperPage<OutGamePaperView>
    {
        protected override string PrefabPath => OutGamePrefabResourcePaths.Paper;

        protected override void OnBind()
        {
            View.eyebrow.text = "SAVE SYSTEM REBUILDING";
            View.title.text = "存档功能重构中";
            View.description.text = "局内外将共用同一份统一存档（JSON 文件）。数据结构设计定案后，这里会回归完整的存档位选择（待定 #9）。";
            if (View.saveListRoot != null) View.saveListRoot.gameObject.SetActive(false);
        }
    }
}
