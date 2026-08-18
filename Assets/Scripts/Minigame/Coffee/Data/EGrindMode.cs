namespace MasterHouse
{
    /// <summary>
    /// 磨豆子环节的操作方式（2026-08-19 临时测试新增，逐关配在 CoffeeLevelDef 上）。
    ///
    /// 新玩法是**试玩性质**：AutoSpin 是原玩法且是默认值，把关卡资产的 GrindMode 改回
    /// AutoSpin（或直接换回原来的关卡资产）就完整切回去，两套判定互不影响。
    /// 试玩结论出来后，要么把 MouseCrank 分支连同本枚举一起删掉，要么反过来删 AutoSpin。
    /// </summary>
    public enum EGrindMode
    {
        /// <summary>原玩法：指针自己匀速转，左键点击切换内外环避障。</summary>
        AutoSpin = 0,

        /// <summary>试玩：按住左键、用鼠标绕圆心顺时针画圈来「摇磨柄」，指针跟着鼠标走。</summary>
        MouseCrank = 1,
    }
}
