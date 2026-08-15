using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// 感谢试玩页（家具库存与交互重做说明 §6.5）：日程表跑完最后一天的【结束今天】日结之后出现，
    /// 是本 demo 的结局。它取代了原先「日程消费完 → 停止投放 + 打一条 Warning」的占位处理。
    ///
    /// **复用纸张页 Prefab**（与存档占位页同例），不新建 Prefab、不新建 View 组件、不动生成器——
    /// 结局页只需要一段文案，为它单开一套布局是白花力气（§15.3 不预设抽象）。
    /// Prefab 缺失由 HousePage.Show 打 Error 且不回退代码布局（§16.2）。
    /// 返回按钮与 ESC 由 PaperPage 基类统一回标题页，正是结局想要的去处。
    ///
    /// 走整页路由还有一个副作用是刻意的：离开 Hub ⇒ EClockStopReason.OffHubPage 生效 ⇒ tick 停 ⇒
    /// 访客天然不再投放，不需要额外写一条「停止投放」的开关。
    /// </summary>
    public sealed class ThanksForPlayingPage : PaperPage<OutGamePaperView>
    {
        protected override string PrefabPath => OutGamePrefabResourcePaths.Paper;

        protected override void OnBind()
        {
            View.eyebrow.text = "THANKS FOR PLAYING";
            View.title.text = "感谢试玩";
            View.description.text =
                "这就是目前全部的日程了。\n\n" +
                "谢谢你陪 House 走过这几天——招待访客、布置房间、把每个人安顿好。\n" +
                "后面的故事还在写，届时欢迎再来坐坐。";
            if (View.saveListRoot != null) View.saveListRoot.gameObject.SetActive(false);
        }
    }
}
