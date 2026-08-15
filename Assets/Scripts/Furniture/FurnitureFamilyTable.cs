using System;
using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 家具族配置表中的一行（家具族体系说明 §3.2）：**同族必然相同**的属性都住在这里。
    ///
    /// 划分依据是实测而非拍脑袋：121 行家具按 id 前缀分成 15 个族，本类的每一个字段
    /// 在族内都是 100% 一致的（分类/表面/占格/装饰分/音效/桌面格全部逐族核对过）。
    /// 逐变体不同的（显示名、显示宽高、精灵图、色值）留在 <see cref="FurnitureEntry"/>。
    ///
    /// **不做「族默认值 + 变体覆写」**：那会让同一个数值有两个家，违反项目既定原则（§3.2）。
    /// 一个字段要么整族一个值，要么逐行填，没有中间态。
    /// </summary>
    [Serializable]
    public sealed class FurnitureFamilyEntry
    {
        [Tooltip("族 id（稳定键）：家具表的「族id」列引用它")] public string familyId;
        [Tooltip("族显示名（商城卡片标题、收纳栏槽位名），如「单人沙发」；变体名仍是「单人沙发·02」")]
        public string displayName;
        [Tooltip("商店分类（盆栽/摆件/桌椅/壁挂/灯具，商店页签用）")] public string category;
        [Tooltip("商店描述文案")] public string description;
        [Tooltip("可吸附的表面类型（可多选：如纸箱既可地面也可桌面；表格里用 / 分隔）")]
        public List<FurnitureSurfaceType> surfaces = new List<FurnitureSurfaceType> { FurnitureSurfaceType.Floor };
        [Tooltip("可叠放（地毯类）：平铺在地面、不挡其他家具落格，渲染压在所有立式家具之下；同为可叠放的彼此仍互斥")]
        public bool stackable;
        [Tooltip("占格：列数")] public int cols = 1;
        [Tooltip("占格：行数")] public int rows = 1;
        [Tooltip("摆放后对 House 装饰分的贡献")] public int decorationScore = 10;
        [Tooltip("专属拿起音效（空 = 用全局默认 FurniturePickup）")] public AudioClip pickupSound;
        [Tooltip("专属放下音效（空 = 用全局默认 FurniturePlace）")] public AudioClip putdownSound;
        [Tooltip("桌面格配置（仅地面家具生效）")] public FurnitureTableSurfaceConfig tableSurface = new FurnitureTableSurfaceConfig();
    }

    /// <summary>
    /// 家具族配置表（一张表，一行一个族）。把「同款家具的不同换色」从**隐式的 id 命名约定**
    /// 扶正为显式的族：改一处族属性、整族生效；需求可以按族配；商城与收纳栏按族折叠。
    ///
    /// **运行时职责只有两件**：①提供族显示名（商城卡片标题、收纳栏槽位名）②给需求编辑器列族下拉。
    /// 族级数值不在运行时查表——导表时已由 <c>FurnitureCsvImporter</c> **逐行展开**进
    /// <see cref="FurnitureEntry"/>（§3.3），所以摆放/烘焙/装饰分/需求匹配等消费方一行都没改。
    /// SO 里因此有数据冗余，但那是机器生成的产物，不是策划的负担。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/家具族配置表", fileName = "FurnitureFamilyTable")]
    public sealed class FurnitureFamilyTable : ScriptableObject
    {
        public List<FurnitureFamilyEntry> entries = new List<FurnitureFamilyEntry>();

        public FurnitureFamilyEntry Find(string familyId)
        {
            if (string.IsNullOrEmpty(familyId)) return null;
            for (var i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].familyId == familyId) return entries[i];
            return null;
        }

        /// <summary>族显示名；族不存在时回落传入的族 id（宁可显示 id 也不显示空白，便于发现配置事故）。</summary>
        public string DisplayNameOf(string familyId)
        {
            var family = Find(familyId);
            return family != null && !string.IsNullOrEmpty(family.displayName) ? family.displayName : familyId;
        }
    }
}
