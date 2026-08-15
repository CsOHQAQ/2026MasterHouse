using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 把一个 string 字段标成「家具族表（FurnitureFamilyTable）的行 id」：Inspector 里画成从族表读出的**下拉**
    /// （抽屉见 `Furniture/Editor/FurnitureFamilyIdDrawer.cs`）。
    ///
    /// 与 <see cref="FurnitureIdAttribute"/> 是一对：那个选「就要那张蓝的」，这个选「随便什么颜色的单人沙发」。
    /// 同样标在 `List&lt;string&gt;` 上逐元素生效。
    /// </summary>
    public sealed class FurnitureFamilyIdAttribute : PropertyAttribute
    {
    }
}
