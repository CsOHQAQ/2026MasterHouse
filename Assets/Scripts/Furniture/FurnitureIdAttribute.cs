using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 把一个 string 字段标成「家具表（FurnitureTable）的行 id」：Inspector 里画成从家具表读出的**下拉**，
    /// 策划不用手打字符串（抽屉见 `Furniture/Editor/FurnitureIdDrawer.cs`）。
    ///
    /// 标在 `List&lt;string&gt;` 上时 Unity 会**逐元素**应用抽屉，于是列表每一行都是一个下拉，
    /// 增删仍走原生列表 UI。
    ///
    /// 放在家具模块而不是使用方模块：它绑定的是家具表，任何「要填一个家具 id」的字段都能复用（§15.3）。
    /// 属性类本身必须在运行时程序集——被标注的字段就在运行时 Def 上。
    /// </summary>
    public sealed class FurnitureIdAttribute : PropertyAttribute
    {
    }
}
