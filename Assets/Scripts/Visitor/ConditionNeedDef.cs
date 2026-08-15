using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 条件类需求（访客需求重做说明 §4.1/§6）：所住房间里存在下列家具中的**任意一件**即通过（OR 语义）。
    ///
    /// 验收走对话分支条件 RoomHasAnyFurnitureCondition，判定数据源是 FurniturePlacementQuery（§6.1）——
    /// 「新买的 / 从别的房间搬来的 / 房间初始就摆着的 / 摆在桌面上的」一律算数，客人不关心杯子怎么来的。
    /// 条件类固定判「完美」（用户定案，§6.3）。
    ///
    /// **两个列表并存**（家具族体系说明 §4.2）：<see cref="familyIds"/> 满足「随便什么颜色的单人沙发都行」，
    /// <see cref="furnitureIds"/> 满足「就要那张蓝的」。两者之间同样是 **OR**——任一命中即通过。
    /// 都留空 = 永远不满足（校验器会报错，「没写要什么」不该被当成「什么都算数」）。
    ///
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 .asset 的脚本引用会损坏（见 RETRO）。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/访客需求·条件类", fileName = "Need_")]
    public sealed class ConditionNeedDef : NeedDef
    {
        public override ENeedType NeedType => ENeedType.Condition;

        [FurnitureFamilyId] // 逐元素画成家具族表下拉
        [Tooltip("按族匹配：房间里有该族的任意配色即算满足。与下面的精确 id 列表是 OR 关系")]
        public List<string> familyIds = new List<string>();

        [FurnitureId] // 逐元素画成家具表下拉，策划不手打字符串（§4.5）
        [Tooltip("按具体配色匹配：家具表（FurnitureTable）的行 id 列表；房间内存在任一即算满足。\n" +
                 "两个列表都留空 = 永远不满足")]
        public List<string> furnitureIds = new List<string>();

        /// <summary>一条都没配（两个列表都空）= 配置事故。校验器与编辑器用它统一判断。</summary>
        public bool IsEmpty =>
            (familyIds == null || familyIds.Count == 0) && (furnitureIds == null || furnitureIds.Count == 0);
    }
}
