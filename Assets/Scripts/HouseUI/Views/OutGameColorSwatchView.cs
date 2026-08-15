using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 配色色块模板（§16.2 动态列表项 = 模板 Prefab + 运行时实例化）。
    ///
    /// 结构固定为「外框 + 内芯」两层：外框走 store/color-* 三态素材（默认/悬停/选中），
    /// 内芯填家具表的「色值」。**商城选色与收纳栏槽位共用这一个模板**——
    /// 玩家在商城学会的「点色块换配色」在收纳栏原样成立，代码也只有一份（见 <see cref="ColorSwatchStrip"/>）。
    /// </summary>
    public sealed class OutGameColorSwatchView : MonoBehaviour
    {
        [Tooltip("外框：按选中/悬停/默认换 store/color-* 素材")] public Image frame;
        [Tooltip("内芯：填家具表的色值")] public Image fill;
        [Tooltip("点击选中此配色；不可交互的场合（获得弹窗）由代码禁用")] public Button button;
    }
}
