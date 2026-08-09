using System.Collections.Generic;
using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 访客配置表（§16.3 Visitor 模块的 Model）：业务访客 + 氛围邻居。
    /// 【务必独占本文件】ScriptableObject 必须与文件同名，否则 Unity 不为其生成 MonoScript，
    /// 已创建的 .asset 会在域重载后丢失脚本引用（m_Script: {fileID: 0}）而损坏。条目类见 VisitorDef.cs。
    /// </summary>
    [CreateAssetMenu(menuName = "MasterHouse/访客配置表", fileName = "VisitorTable")]
    public sealed class VisitorTable : ScriptableObject
    {
        [Tooltip("业务访客。列表顺序即业务下标——存档按下标对齐，重排/插入会串档（待定 #9 统一存档时改用 id 键）")]
        public List<VisitorDef> visitors = new List<VisitorDef>();

        [Tooltip("串门邻居名册，顺序 = 轮换名册顺序")]
        public List<AmbientVisitorDef> ambientVisitors = new List<AmbientVisitorDef>();
    }
}
